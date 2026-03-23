using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Numerics;
using UniversalVoiceChat.Net;
using UniversalVoiceChat.Audio;

namespace UniversalVoiceChat.Core
{
    public static unsafe class EntryPoint
    {
        private const uint DLL_PROCESS_DETACH = 0;
        private const uint DLL_PROCESS_ATTACH = 1;
        
        private static IntPtr _hModule;
        private static bool _isRunning = false;
        
        private static WsVoiceClient? _ws;
        private static VoiceCapture _mic;
        private static VoiceEncoder _encoder = new VoiceEncoder();
        private static byte[] _pcmBuffer = new byte[1920]; // 20ms at 48kHz Mono
        private static int _pcmBufferPos = 0;
        
        private static bool _firstMicLog = false;
        private static DateTime _lastPttLog = DateTime.MinValue;
        private static DateTime _lastSendLog = DateTime.MinValue;
        
        private class RemotePlayerSession
        {
            public VoicePlayback Playback { get; set; } = new VoicePlayback();
            public VoiceDecoder Decoder { get; set; } = new VoiceDecoder();
        }

        private static ConcurrentDictionary<uint, RemotePlayerSession> _remotePlayers = new ConcurrentDictionary<uint, RemotePlayerSession>();

        [UnmanagedCallersOnly(EntryPoint = "DllMain", CallConvs = new[] { typeof(CallConvStdcall) })]
        public static bool DllMain(IntPtr hModule, uint ul_reason_for_call, IntPtr lpReserved)
        {
            switch (ul_reason_for_call)
            {
                case DLL_PROCESS_ATTACH:
                    _hModule = hModule;
                    // Use CreateThread (Native) instead of new Thread() (Managed) to avoid Loader Lock
                    IntPtr handle = CreateThread(IntPtr.Zero, IntPtr.Zero, &InitializeThreadWrapper, IntPtr.Zero, 0, IntPtr.Zero);
                    if (handle != IntPtr.Zero)
                    {
                        CloseHandle(handle);
                    }
                    break;
                case DLL_PROCESS_DETACH:
                    _isRunning = false;
                    break;
            }
            return true;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
        private static uint InitializeThreadWrapper(IntPtr lpParam)
        {
            try
            {
                CoInitializeEx(IntPtr.Zero, 0); // COINIT_MULTITHREADED
                Initialize();
            }
            catch (Exception ex)
            {
                Logger.Log("[UniversalVoiceChat] CRASH: " + ex.ToString());
            }
            return 0;
        }

        private static void Initialize()
        {
            _isRunning = true;
            
            Logger.Log("[UniversalVoiceChat] Mod Initialized (Native AOT)!");

            // Load configuration from UniversalVoiceChat.ini
            Config.Load();

            // Main Mod Loop
            int ticks = 0;
            string currentIp = "";
            int currentPort = 0;

            while (_isRunning)
            {
                bool hasServer = Memory.SAMPOffsets.TryGetServerInfo(out string ip, out int port);
                
                // If we found a server and haven't connected yet
                if (hasServer && (_ws == null || !_ws.IsConnected))
                {
                    if (ip != currentIp || port != currentPort)
                    {
                        currentIp = ip;
                        currentPort = port;
                        Logger.Log($"[UniversalVoiceChat] Attached to Server: {ip}:{port}");
                        
                        // Relay URL from INI config
                        string relayUrl = Config.RelayUrl;
                        uint myPlayerId = (uint)new Random().Next(1000, 999999);
                        
                        _ws = new WsVoiceClient(relayUrl, ip, port, myPlayerId);
                        _ws.OnAudioReceived += OnRemoteAudioReceived;
                        _ = _ws.ConnectAsync();
                        
                        // Start Microphone Capture
                        if (_mic == null)
                        {
                            try 
                            {
                                _mic = new VoiceCapture();
                                _mic.DataAvailable += (s, e) => 
                                {
                                    try 
                                    {
                                        if (!_firstMicLog) {
                                            Logger.Log($"[UniversalVoiceChat] DIAGNOSTIC: DataAvailable FIRED! Bytes={e.BytesRecorded}");
                                            _firstMicLog = true;
                                        }
                                        
                                        if (_ws != null && _ws.IsConnected && Memory.API.TryGetPlayerPosition(out var pos))
                                        {
                                            // 1. Check Push-to-Talk
                                            if (Config.PushToTalkKey != 0)
                                            {
                                                if ((GetAsyncKeyState(Config.PushToTalkKey) & 0x8000) == 0) return;
                                                
                                                if ((DateTime.UtcNow - _lastPttLog).TotalSeconds >= 2) {
                                                    Logger.Log("[UniversalVoiceChat] DIAGNOSTIC: PTT Key Pressed!");
                                                    _lastPttLog = DateTime.UtcNow;
                                                }
                                            }

                                            // 2. Buffer PCM until we have 20ms (1920 bytes)
                                            int remaining = e.BytesRecorded;
                                            int sourceOffset = 0;
                                            
                                            if (!_firstMicLog) {
                                                Logger.Log($"[UniversalVoiceChat] DIAGNOSTIC: First DataAvailable Hit! Bytes={remaining}");
                                                _firstMicLog = true;
                                            }

                                            while (remaining > 0)
                                            {
                                                int toCopy = Math.Min(remaining, _pcmBuffer.Length - _pcmBufferPos);
                                                Buffer.BlockCopy(e.Buffer, sourceOffset, _pcmBuffer, _pcmBufferPos, toCopy);
                                                
                                                _pcmBufferPos += toCopy;
                                                sourceOffset += toCopy;
                                                remaining -= toCopy;

                                                if (_pcmBufferPos >= _pcmBuffer.Length)
                                                {
                                                    // Apply Gain if needed
                                                    if (Config.InputGain != 1.0f)
                                                        ApplyGain(_pcmBuffer, _pcmBuffer.Length, Config.InputGain);

                                                    // Encode and Send
                                                    byte[]? opusData = _encoder.Encode(_pcmBuffer, _pcmBuffer.Length, out int opusLen);
                                                    if (opusData != null && opusLen > 0)
                                                    {
                                                        if ((DateTime.UtcNow - _lastSendLog).TotalSeconds >= 2) {
                                                            Logger.Log($"[UniversalVoiceChat] DIAGNOSTIC: Sending {opusLen} bytes of audio");
                                                            _lastSendLog = DateTime.UtcNow;
                                                        }
                                                        _ws.SendAudio(pos, opusData, opusLen);
                                                    }

                                                    _pcmBufferPos = 0;
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception bgEx)
                                    {
                                        Logger.Log("[UniversalVoiceChat] BUG in Mic capture: " + bgEx.Message);
                                    }
                                };
                                _mic.Start();
                                Logger.Log("[UniversalVoiceChat] Microphone Started!");
                            }
                            catch (Exception micEx)
                            {
                                Logger.Log("[UniversalVoiceChat] Mic Init Error: " + micEx.Message);
                            }
                        }
                    }
                }

                if (ticks % 50 == 0) // Print every ~5 seconds to avoid spam
                {
                    if (!hasServer)
                    {
                        Logger.Log($"[UniversalVoiceChat] Waiting to connect to a server...");
                    }
                }

                if (Memory.API.TryGetPlayerPosition(out var pos))
                {
                    if (_ws != null && _ws.IsConnected && ticks % 10 == 0) // 1 heartbeat per sec
                    {
                        _ws.SendPosition(pos);
                    }
                    
                    if (ticks % 50 == 0) Logger.Log($"[UniversalVoiceChat] Pos: X:{pos.X:F1} Y:{pos.Y:F1} Z:{pos.Z:F1}");
                }

                ticks++;
                Thread.Sleep(100); // 10 ticks per second
            }
        }

        private static void OnRemoteAudioReceived(uint senderId, Vector3 senderPos, byte[] audioData)
        {
            try 
            {
                // If the audio session doesn't exist for this player, create it
                if (!_remotePlayers.TryGetValue(senderId, out RemotePlayerSession session))
                {
                    session = new RemotePlayerSession();
                    _remotePlayers.TryAdd(senderId, session);
                    Logger.Log($"[UniversalVoiceChat] Incoming Audio from Player {senderId}");
                }

                // 1. Decode Opus to PCM
                byte[] pcmData = session.Decoder.Decode(audioData, audioData.Length);

                // 2. Feed the PCM data to playback
                session.Playback.FeedAudio(pcmData, pcmData.Length);
                
                // 3. Adjust 3D Spatial Audio Volume & Panning
                if (Memory.API.TryGetPlayerPosition(out var localPos))
                {
                    SpatialMath.Calculate3DAudio(localPos, senderPos, 0f, out float vol, out float pan);
                    session.Playback.UpdateSpatialAudio(vol, pan);
                }
            } 
            catch (Exception ex)
            {
                Logger.Log("[UniversalVoiceChat] BUG in Audio Playback: " + ex.Message);
            }
        }

        private static void ApplyGain(byte[] buffer, int length, float gain)
        {
            // Process 16-bit Mono PCM
            for (int i = 0; i < length; i += 2)
            {
                short sample = BitConverter.ToInt16(buffer, i);
                float fSample = sample * gain;
                
                // Clamp to avoid clipping
                if (fSample > short.MaxValue) fSample = short.MaxValue;
                if (fSample < short.MinValue) fSample = short.MinValue;

                short finalSample = (short)fSample;
                buffer[i] = (byte)(finalSample & 0xFF);
                buffer[i + 1] = (byte)((finalSample >> 8) & 0xFF);
            }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("kernel32.dll")]
        private static extern IntPtr CreateThread(IntPtr lpThreadAttributes, IntPtr dwStackSize, delegate* unmanaged[Stdcall]<IntPtr, uint> lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);
    }
}
