using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Numerics;

namespace UniversalVoiceChat.Net
{
    public class UdpVoiceClient : IDisposable
    {
        private UdpClient _udpClient;
        
        private readonly string _sampServerIp;
        private readonly uint _playerId;
        private bool _isConnected;

        // Custom Event for when audio is received
        // Passes senderId, position, and the audio payload
        public delegate void AudioReceivedHandler(uint senderId, Vector3 senderPos, byte[] audioData);
        public event AudioReceivedHandler OnAudioReceived;

        public UdpVoiceClient(string relayIp, int relayPort, string sampServerIp, uint playerId)
        {
            _sampServerIp = sampServerIp;
            _playerId = playerId;
            
            try
            {
                _udpClient = new UdpClient();
                _udpClient.Connect(relayIp, relayPort); // Establish NAT association
                _isConnected = true;
                
                // Start receiving loop async
                _udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), null);
                Console.WriteLine($"[UniversalVoiceChat-UDP] Connected to Relay Server {relayIp}:{relayPort}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UniversalVoiceChat-UDP] Failed to connect: {ex.Message}");
                _isConnected = false;
            }
        }

        public bool IsConnected => _isConnected;

        public void SendAudio(Vector3 currentPos, byte[] opusData, int length)
        {
            if (!_isConnected) return;
            
            try
            {
                // Packet format: [IP_LEN] [IP_STRING] [PLAYER_ID(4)] [X(4)] [Y(4)] [Z(4)] [AUDIO...]
                byte[] ipBytes = Encoding.ASCII.GetBytes(_sampServerIp);
                
                int packetSize = 1 + ipBytes.Length + 16 + length;
                byte[] packet = new byte[packetSize];
                
                packet[0] = (byte)ipBytes.Length;
                Buffer.BlockCopy(ipBytes, 0, packet, 1, ipBytes.Length);
                
                int offset = 1 + ipBytes.Length;
                
                // Native AOT safe serialization (Zero Allocations)
                BitConverter.TryWriteBytes(new Span<byte>(packet, offset, 4), _playerId);
                BitConverter.TryWriteBytes(new Span<byte>(packet, offset + 4, 4), currentPos.X);
                BitConverter.TryWriteBytes(new Span<byte>(packet, offset + 8, 4), currentPos.Y);
                BitConverter.TryWriteBytes(new Span<byte>(packet, offset + 12, 4), currentPos.Z);
                
                if (length > 0)
                {
                    Buffer.BlockCopy(opusData, 0, packet, offset + 16, length);
                }
                
                _udpClient.Send(packet, packet.Length);
            }
            catch { }
        }
        
        // Overload for just sending position update (Heartbeat)
        public void SendPosition(Vector3 currentPos)
        {
            SendAudio(currentPos, Array.Empty<byte>(), 0);
        }

        private void ReceiveCallback(IAsyncResult ar)
        {
            if (!_isConnected) return;
            
            try
            {
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = _udpClient.EndReceive(ar, ref ep);
                
                // Forwarded packet from Relay: [SENDER_ID (4)] [SENDER_X (4)] [SENDER_Y (4)] [SENDER_Z (4)] [PAYLOAD ...]
                if (data.Length > 16)
                {
                    uint senderId = BitConverter.ToUInt32(data, 0);
                    float x = BitConverter.ToSingle(data, 4);
                    float y = BitConverter.ToSingle(data, 8);
                    float z = BitConverter.ToSingle(data, 12);
                    
                    int audioLen = data.Length - 16;
                    byte[] audioPayload = new byte[audioLen];
                    Buffer.BlockCopy(data, 16, audioPayload, 0, audioLen);
                    
                    OnAudioReceived?.Invoke(senderId, new Vector3(x, y, z), audioPayload);
                }
                
                _udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), null);
            }
            catch
            {
                // Socket closed or error
            }
        }

        public void Dispose()
        {
            _isConnected = false;
            _udpClient?.Close();
        }
    }
}
