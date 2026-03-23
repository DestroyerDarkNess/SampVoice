using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;
using UniversalVoiceChat.Audio;
using UniversalVoiceChat.Core;
using UniversalVoiceChat.Net;

namespace UniversalVoiceChat
{
    public static unsafe class EntryPoint
    {
        private const uint DLL_PROCESS_DETACH = 0;
        private const uint DLL_PROCESS_ATTACH = 1;

        private static nint _hModule;
        private static bool _isRunning = false;

        private static WsVoiceClient? _ws;
        private static VoiceCapture? _mic;
        private static readonly VoiceEncoder _encoder = new VoiceEncoder();
        private static readonly byte[] _pcmBuffer = new byte[1920]; // 20ms at 48kHz Mono
        private static readonly uint _localPlayerId = (uint)Random.Shared.Next(1000, 999999);
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
        private static readonly ConcurrentDictionary<uint, RemotePlayerSession> _remotePlayers = new ConcurrentDictionary<uint, RemotePlayerSession>();

        private static int _pcmBufferPos = 0;
        private static DateTime _nextReconnectAttempt = DateTime.MinValue;

        private static bool _firstMicLog = false;
        private static DateTime _lastPttLog = DateTime.MinValue;
        private static DateTime _lastSendLog = DateTime.MinValue;

        private class RemotePlayerSession
        {
            public VoicePlayback Playback { get; set; } = new VoicePlayback();
            public VoiceDecoder Decoder { get; set; } = new VoiceDecoder();
        }

        [UnmanagedCallersOnly(EntryPoint = "DllMain", CallConvs = new[] { typeof(CallConvStdcall) })]
        public static bool DllMain(nint hModule, uint ul_reason_for_call, nint lpReserved)
        {
            switch (ul_reason_for_call)
            {
                case DLL_PROCESS_ATTACH:
                    _hModule = hModule;
                    // Use CreateThread (Native) instead of new Thread() (Managed) to avoid Loader Lock
                    nint handle = CreateThread(nint.Zero, nint.Zero, &InitializeThreadWrapper, nint.Zero, 0, nint.Zero);
                    if (handle != nint.Zero)
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
        private static uint InitializeThreadWrapper(nint lpParam)
        {
            try
            {
                CoInitializeEx(nint.Zero, 0); // COINIT_MULTITHREADED
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

                if (hasServer)
                {
                    if (ip != currentIp || port != currentPort)
                    {
                        currentIp = ip;
                        currentPort = port;
                        Logger.Log($"[UniversalVoiceChat] Attached to Server: {ip}:{port}");
                        DisposeWsClient();
                        ResetVoiceSessions();
                        _nextReconnectAttempt = DateTime.MinValue;
                    }

                    EnsureMicrophoneStarted();

                    if ((_ws == null || (!_ws.IsConnected && !_ws.IsConnecting)) && DateTime.UtcNow >= _nextReconnectAttempt)
                    {
                        ConnectToRelay(currentIp, currentPort);
                    }
                }
                else if (currentIp.Length > 0 || _ws != null)
                {
                    Logger.Log("[UniversalVoiceChat] Detached from SA:MP server.");
                    currentIp = "";
                    currentPort = 0;
                    DisposeWsClient();
                    ResetVoiceSessions();
                    _nextReconnectAttempt = DateTime.MinValue;
                }

                if (ticks % 50 == 0 && !hasServer) // Print every ~5 seconds to avoid spam
                {
                    Logger.Log("[UniversalVoiceChat] Waiting to connect to a server...");
                }

                if (Memory.API.TryGetPlayerPosition(out var pos))
                {
                    if (_ws != null && _ws.IsConnected && ticks % 10 == 0) // 1 heartbeat per sec
                    {
                        _ws.SendPosition(pos);
                    }

                    if (ticks % 50 == 0)
                    {
                        Logger.Log($"[UniversalVoiceChat] Pos: X:{pos.X:F1} Y:{pos.Y:F1} Z:{pos.Z:F1}");
                    }
                }

                ticks++;
                Thread.Sleep(100); // 10 ticks per second
            }

            Shutdown();
        }

        private static void EnsureMicrophoneStarted()
        {
            if (_mic != null)
            {
                return;
            }

            try
            {
                _mic = new VoiceCapture();
                _mic.DataAvailable += OnMicDataAvailable;
                _mic.Start();
                Logger.Log("[UniversalVoiceChat] Microphone Started!");
            }
            catch (Exception micEx)
            {
                Logger.Log("[UniversalVoiceChat] Mic Init Error: " + micEx.Message);
                _mic?.Dispose();
                _mic = null;
            }
        }

        private static void ConnectToRelay(string ip, int port)
        {
            DisposeWsClient();

            _ws = new WsVoiceClient(Config.RelayUrl, ip, port, _localPlayerId);
            _ws.OnAudioReceived += OnRemoteAudioReceived;
            _ = _ws.ConnectAsync();

            _nextReconnectAttempt = DateTime.UtcNow + ReconnectDelay;
            Logger.Log($"[UniversalVoiceChat] Connecting to relay for room {ip}:{port}");
        }

        private static void DisposeWsClient()
        {
            if (_ws == null)
            {
                return;
            }

            _ws.OnAudioReceived -= OnRemoteAudioReceived;
            _ws.Dispose();
            _ws = null;
        }

        private static void ResetVoiceSessions()
        {
            _pcmBufferPos = 0;

            foreach (var session in _remotePlayers.Values)
            {
                session.Playback.Dispose();
            }

            _remotePlayers.Clear();
        }

        private static void Shutdown()
        {
            DisposeWsClient();
            ResetVoiceSessions();

            if (_mic != null)
            {
                _mic.DataAvailable -= OnMicDataAvailable;
                _mic.Dispose();
                _mic = null;
            }
        }

        private static void OnMicDataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                if (!_firstMicLog)
                {
                    Logger.Log($"[UniversalVoiceChat] DIAGNOSTIC: DataAvailable FIRED! Bytes={e.BytesRecorded}");
                    _firstMicLog = true;
                }

                if (_ws == null || !_ws.IsConnected || !Memory.API.TryGetPlayerPosition(out var pos))
                {
                    return;
                }

                // 1. Check Push-to-Talk
                if (Config.PushToTalkKey != 0)
                {
                    if ((GetAsyncKeyState(Config.PushToTalkKey) & 0x8000) == 0)
                    {
                        return;
                    }

                    if ((DateTime.UtcNow - _lastPttLog).TotalSeconds >= 2)
                    {
                        Logger.Log("[UniversalVoiceChat] DIAGNOSTIC: PTT Key Pressed!");
                        _lastPttLog = DateTime.UtcNow;
                    }
                }

                // 2. Buffer PCM until we have 20ms (1920 bytes)
                int remaining = e.BytesRecorded;
                int sourceOffset = 0;

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
                        {
                            ApplyGain(_pcmBuffer, _pcmBuffer.Length, Config.InputGain);
                        }

                        // Encode and Send
                        byte[]? opusData = _encoder.Encode(_pcmBuffer, _pcmBuffer.Length, out int opusLen);
                        if (opusData != null && opusLen > 0)
                        {
                            if ((DateTime.UtcNow - _lastSendLog).TotalSeconds >= 2)
                            {
                                Logger.Log($"[UniversalVoiceChat] DIAGNOSTIC: Sending {opusLen} bytes of audio");
                                _lastSendLog = DateTime.UtcNow;
                            }

                            _ws.SendAudio(pos, opusData, opusLen);
                        }

                        _pcmBufferPos = 0;
                    }
                }
            }
            catch (Exception bgEx)
            {
                Logger.Log("[UniversalVoiceChat] BUG in Mic capture: " + bgEx.Message);
            }
        }

        private static void OnRemoteAudioReceived(uint senderId, Vector3 senderPos, byte[] audioData)
        {
            try
            {
                // If the audio session doesn't exist for this player, create it
                if (!_remotePlayers.TryGetValue(senderId, out RemotePlayerSession? session))
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
        private static extern nint CreateThread(nint lpThreadAttributes, nint dwStackSize, delegate* unmanaged[Stdcall]<nint, uint> lpStartAddress, nint lpParameter, uint dwCreationFlags, nint lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(nint hObject);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);
    }
}
