using System;
using Concentus.Enums;
using Concentus.Structs;

namespace UniversalVoiceChat.Audio
{
    public class VoiceEncoder
    {
        private readonly OpusEncoder _encoder;
        private readonly short[] _inputBuffer;
        private readonly byte[] _outputBuffer;
        private const int FrameSize = 960; // 20ms at 48kHz

        public VoiceEncoder()
        {
            _encoder = new OpusEncoder(48000, 1, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = 48000;
            _inputBuffer = new short[FrameSize];
            _outputBuffer = new byte[1024];
        }

        public byte[]? Encode(byte[] pcmData, int length, out int encodedLength)
        {
            if (length != FrameSize * 2) 
            {
                encodedLength = 0;
                return null;
            }

            // Convert byte[] to short[]
            for (int i = 0; i < FrameSize; i++)
            {
                _inputBuffer[i] = BitConverter.ToInt16(pcmData, i * 2);
            }

            encodedLength = _encoder.Encode(_inputBuffer, 0, FrameSize, _outputBuffer, 0, _outputBuffer.Length);
            return _outputBuffer;
        }
    }
}
