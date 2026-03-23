using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniversalVoiceChat.Core;

namespace UniversalVoiceChat.Net
{
    public class WsVoiceClient : IDisposable
    {
        private const int MaxQueuedPackets = 64;

        private ClientWebSocket? _ws;
        private readonly string _relayUrl;
        private readonly string _sampServerKey;
        private readonly uint _playerId;
        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _sendSignal = new SemaphoreSlim(0);

        private volatile bool _isConnected;
        private volatile bool _isConnecting;
        private int _queuedPackets;
        private DateTime _lastQueueFullLog = DateTime.MinValue;

        private CancellationTokenSource? _cts;
        private Task? _sendLoopTask;
        private Task? _receiveLoopTask;

        public delegate void AudioReceivedHandler(uint senderId, Vector3 senderPos, byte[] audioData);
        public event AudioReceivedHandler? OnAudioReceived;

        public WsVoiceClient(string relayUrl, string sampServerIp, int sampServerPort, uint playerId)
        {
            _relayUrl = relayUrl;
            _sampServerKey = $"{sampServerIp}:{sampServerPort}";
            _playerId = playerId;
        }

        public bool IsConnected => _isConnected;
        public bool IsConnecting => _isConnecting;

        public async Task ConnectAsync()
        {
            if (_isConnected || _isConnecting)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _isConnecting = true;

            try
            {
                _ws = new ClientWebSocket();

                // Keep the connection alive with Pings every 20 seconds
                // This prevents Render.com from closing idle connections if no audio/position is sent
                _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

                await _ws.ConnectAsync(new Uri(_relayUrl), _cts.Token).ConfigureAwait(false);
                _isConnected = true;
                Logger.Log($"[UniversalVoiceChat-WS] Connected to Relay: {_relayUrl}");

                _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
                _sendLoopTask = Task.Run(() => SendLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                Logger.Log($"[UniversalVoiceChat-WS] Connection failed: {ex.Message}");
                _isConnected = false;
                _ws?.Dispose();
                _ws = null;
                ClearSendQueue();
            }
            finally
            {
                _isConnecting = false;
            }
        }

        public void SendAudio(Vector3 pos, byte[] audioData, int length)
        {
            if (!_isConnected || _ws == null || _ws.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                // Packet: [IP_LEN(1)] [IP_STRING] [PLAYER_ID(4)] [X(4)] [Y(4)] [Z(4)] [AUDIO...]
                byte[] serverKeyBytes = Encoding.ASCII.GetBytes(_sampServerKey);
                int packetSize = 1 + serverKeyBytes.Length + 16 + length;
                byte[] packet = new byte[packetSize];

                packet[0] = (byte)serverKeyBytes.Length;
                Buffer.BlockCopy(serverKeyBytes, 0, packet, 1, serverKeyBytes.Length);

                int offset = 1 + serverKeyBytes.Length;
                BitConverter.TryWriteBytes(new Span<byte>(packet, offset, 4), _playerId);
                BitConverter.TryWriteBytes(new Span<byte>(packet, offset + 4, 4), pos.X);
                BitConverter.TryWriteBytes(new Span<byte>(packet, offset + 8, 4), pos.Y);
                BitConverter.TryWriteBytes(new Span<byte>(packet, offset + 12, 4), pos.Z);

                if (length > 0)
                {
                    Buffer.BlockCopy(audioData, 0, packet, offset + 16, length);
                }

                if (!TryEnqueuePacket(packet) && (DateTime.UtcNow - _lastQueueFullLog).TotalSeconds >= 2)
                {
                    Logger.Log("[UniversalVoiceChat-WS] Send queue full, dropping outgoing packets.");
                    _lastQueueFullLog = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[UniversalVoiceChat-WS] Send Error: {ex.Message}");
            }
        }

        public void SendPosition(Vector3 pos)
        {
            SendAudio(pos, Array.Empty<byte>(), 0);
        }

        private bool TryEnqueuePacket(byte[] packet)
        {
            int queuedPackets = Interlocked.Increment(ref _queuedPackets);
            if (queuedPackets > MaxQueuedPackets)
            {
                Interlocked.Decrement(ref _queuedPackets);
                return false;
            }

            _sendQueue.Enqueue(packet);
            _sendSignal.Release();
            return true;
        }

        private async Task SendLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await _sendSignal.WaitAsync(ct).ConfigureAwait(false);

                    while (_sendQueue.TryDequeue(out byte[]? packet))
                    {
                        Interlocked.Decrement(ref _queuedPackets);

                        if (packet == null || _ws == null || _ws.State != WebSocketState.Open)
                        {
                            continue;
                        }

                        await _ws.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Log($"[UniversalVoiceChat-WS] Send loop failed: {ex.Message}");
                _isConnected = false;
            }
            finally
            {
                ClearSendQueue();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            byte[] buffer = new byte[65536];

            while (_ws != null && _ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                try
                {
                    var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Logger.Log("[UniversalVoiceChat-WS] Server closed connection.");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Binary && result.Count > 16)
                    {
                        // Forwarded packet: [SENDER_ID(4)] [X(4)] [Y(4)] [Z(4)] [AUDIO...]
                        uint senderId = BitConverter.ToUInt32(buffer, 0);
                        float x = BitConverter.ToSingle(buffer, 4);
                        float y = BitConverter.ToSingle(buffer, 8);
                        float z = BitConverter.ToSingle(buffer, 12);

                        int audioLen = result.Count - 16;
                        byte[] audioPayload = new byte[audioLen];
                        Buffer.BlockCopy(buffer, 16, audioPayload, 0, audioLen);

                        OnAudioReceived?.Invoke(senderId, new Vector3(x, y, z), audioPayload);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[UniversalVoiceChat-WS] Receive Error: {ex.Message}");
                    break;
                }
            }

            _isConnected = false;
            ClearSendQueue();
        }

        private void ClearSendQueue()
        {
            while (_sendQueue.TryDequeue(out _))
            {
            }

            Interlocked.Exchange(ref _queuedPackets, 0);
        }

        public void Dispose()
        {
            _isConnected = false;
            _cts?.Cancel();
            _sendSignal.Release();

            try { _sendLoopTask?.Wait(500); } catch { }
            try { _receiveLoopTask?.Wait(500); } catch { }

            _ws?.Dispose();
            _cts?.Dispose();
            ClearSendQueue();
        }
    }
}
