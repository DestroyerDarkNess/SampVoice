using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalVoiceChat.Net
{
    public class SignalingClient
    {
        private ClientWebSocket _webSocket;
        private readonly string _signalingServerUrl;
        private readonly string _sampServerIp;
        private readonly int _sampServerPort;
        private readonly string _playerId;
        private bool _isConnected;
        private CancellationTokenSource _cts;

        public SignalingClient(string url, string serverIp, int serverPort)
        {
            _signalingServerUrl = url;
            _sampServerIp = serverIp;
            _sampServerPort = serverPort;
            _playerId = Guid.NewGuid().ToString("N"); // Unique ID for this session
        }

        public bool IsConnected => _isConnected;

        public async Task ConnectAsync()
        {
            _cts = new CancellationTokenSource();
            try
            {
                _webSocket = new ClientWebSocket();
                
                // We pass the Server IP and Port so the Signaling Backend knows which "Room" to put us in
                Uri serverUri = new Uri($"{_signalingServerUrl}?server={_sampServerIp}:{_sampServerPort}&player={_playerId}");
                
                Console.WriteLine($"[UniversalVoiceChat-Net] Connecting to Signaling: {serverUri}");
                await _webSocket.ConnectAsync(serverUri, _cts.Token);
                _isConnected = true;
                
                Console.WriteLine("[UniversalVoiceChat-Net] Connected successfully to Signaling Server!");
                
                // Fire and forget the listen loop
                _ = ReceiveLoopAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UniversalVoiceChat-Net] Connection failed: {ex.Message}");
                _isConnected = false;
            }
        }

        public void Disconnect()
        {
            if (_isConnected && _cts != null)
            {
                _cts.Cancel();
                _webSocket?.Dispose();
                _isConnected = false;
                Console.WriteLine("[UniversalVoiceChat-Net] Disconnected from Signaling Server.");
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            while (_webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("[UniversalVoiceChat-Net] Server closed connection.");
                        break;
                    }

                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    HandleSignalingMessage(message);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UniversalVoiceChat-Net] WebSocket Receive Error: {ex.Message}");
                    break;
                }
            }
            
            _isConnected = false;
        }

        private void HandleSignalingMessage(string jsonMessage)
        {
            // Parse WebRTC Exchange messages (Offers, Answers, ICE Candidates)
            Console.WriteLine($"[Signaling -> Client] {jsonMessage}");
            
            // TODO: Route parsed JSON to WebRTC Manager
        }

        public async Task SendJsonAsync(string jsonPayload)
        {
            if (!_isConnected || _webSocket.State != WebSocketState.Open) return;

            try
            {
                // In Native AOT, manual JSON building is safer than Reflection-based JsonSerializer
                byte[] buffer = Encoding.UTF8.GetBytes(jsonPayload);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UniversalVoiceChat-Net] Send Error: {ex.Message}");
            }
        }
    }
}
