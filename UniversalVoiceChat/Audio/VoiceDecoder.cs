using System;
using Concentus.Structs;

namespace UniversalVoiceChat.Audio
{
    public class VoiceDecoder
    {
        private readonly OpusDecoder _decoder;
        private readonly short[] _outputBuffer;
        private readonly byte[] _pcmBuffer;
        private const int FrameSize = 960; // 20ms at 48kHz

        public VoiceDecoder()
        {
            _decoder = new OpusDecoder(48000, 1);
            _outputBuffer = new short[FrameSize];
            _pcmBuffer = new byte[FrameSize * 2];
        }

        public byte[] Decode(byte[] encodedData, int length)
        {
            int decodedSamples = _decoder.Decode(encodedData, 0, length, _outputBuffer, 0, FrameSize, false);
            
            for (int i = 0; i < decodedSamples; i++)
            {
                short sample = _outputBuffer[i];
                _pcmBuffer[i * 2] = (byte)(sample & 0xFF);
                _pcmBuffer[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return _pcmBuffer;
        }
    }
}
