using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace UniversalVoiceChat.Audio
{
    /// <summary>
    /// Pure P/Invoke audio playback using winmm.dll waveOut* functions.
    /// Features a jitter buffer to smooth out network packet bursts.
    /// WaveHdr structs are in native memory for proper dwFlags polling.
    /// </summary>
    public class VoicePlayback : IDisposable
    {
        private IntPtr _hWaveOut = IntPtr.Zero;
        
        private IntPtr _dataBuf1, _dataBuf2;
        private IntPtr _hdr1Ptr, _hdr2Ptr;
        
        private bool _playing = false;
        private Thread? _playThread;

        // Ring buffer for incoming PCM
        private readonly byte[] _ringBuffer;
        private int _writePos = 0;
        private int _readPos = 0;
        private int _available = 0;
        private readonly object _ringLock = new object();

        private float _volume = 1.0f;

        // Jitter buffer: don't start playing until we have this much data buffered
        private bool _buffering = true;
        private const int JITTER_BUFFER_FRAMES = 4; // Buffer 4 frames (~80ms) before starting playback

        private const int SAMPLE_RATE = 48000;
        private const int CHANNELS = 1;
        private const int BITS_PER_SAMPLE = 16;
        private const int BUFFER_MS = 20;
        private const int BUFFER_SIZE = SAMPLE_RATE * CHANNELS * (BITS_PER_SAMPLE / 8) * BUFFER_MS / 1000; // 1920
        private const int JITTER_THRESHOLD = BUFFER_SIZE * JITTER_BUFFER_FRAMES; // ~15360 bytes
        private const int RING_BUFFER_SIZE = BUFFER_SIZE * 100; // ~2 seconds

        private static readonly int FLAGS_OFFSET = IntPtr.Size + 4 + 4 + IntPtr.Size;
        private static readonly int HDR_SIZE = Marshal.SizeOf<WaveHdr>();
        
        // Volume boost multiplier (2.5x for better audibility)
        private const float VOLUME_BOOST = 2.5f;

        public VoicePlayback()
        {
            _ringBuffer = new byte[RING_BUFFER_SIZE];

            var fmt = new WaveFormatEx
            {
                wFormatTag = 1,
                nChannels = (short)CHANNELS,
                nSamplesPerSec = SAMPLE_RATE,
                wBitsPerSample = (short)BITS_PER_SAMPLE,
                nBlockAlign = (short)(CHANNELS * BITS_PER_SAMPLE / 8),
                nAvgBytesPerSec = SAMPLE_RATE * CHANNELS * BITS_PER_SAMPLE / 8,
                cbSize = 0
            };

            int result = waveOutOpen(ref _hWaveOut, 0xFFFFFFFF, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (result != 0)
            {
                Core.Logger.Log($"[VoicePlayback] waveOutOpen failed: {result}");
                return;
            }

            _dataBuf1 = Marshal.AllocHGlobal(BUFFER_SIZE);
            _dataBuf2 = Marshal.AllocHGlobal(BUFFER_SIZE);
            _hdr1Ptr = Marshal.AllocHGlobal(HDR_SIZE);
            _hdr2Ptr = Marshal.AllocHGlobal(HDR_SIZE);

            for (int i = 0; i < HDR_SIZE; i++)
            {
                Marshal.WriteByte(_hdr1Ptr, i, 0);
                Marshal.WriteByte(_hdr2Ptr, i, 0);
            }

            SetHeaderData(_hdr1Ptr, _dataBuf1);
            SetHeaderData(_hdr2Ptr, _dataBuf2);

            waveOutPrepareHeader(_hWaveOut, _hdr1Ptr, HDR_SIZE);
            waveOutPrepareHeader(_hWaveOut, _hdr2Ptr, HDR_SIZE);

            _playing = true;
            _playThread = new Thread(PlayLoop) { IsBackground = true, Name = "VoicePlayback" };
            _playThread.Start();
        }

        private void SetHeaderData(IntPtr hdrPtr, IntPtr dataPtr)
        {
            Marshal.WriteIntPtr(hdrPtr, 0, dataPtr);
            Marshal.WriteInt32(hdrPtr, IntPtr.Size, BUFFER_SIZE);
        }

        private uint ReadFlags(IntPtr hdrPtr)
        {
            return (uint)Marshal.ReadInt32(hdrPtr, FLAGS_OFFSET);
        }

        public void FeedAudio(byte[] pcmData, int length)
        {
            lock (_ringLock)
            {
                for (int i = 0; i < length; i++)
                {
                    _ringBuffer[_writePos] = pcmData[i];
                    _writePos = (_writePos + 1) % RING_BUFFER_SIZE;
                    if (_available < RING_BUFFER_SIZE)
                        _available++;
                    else
                        _readPos = (_readPos + 1) % RING_BUFFER_SIZE;
                }
            }
        }

        public void UpdateSpatialAudio(float volume, float pan)
        {
            _volume = volume;
        }

        private void PlayLoop()
        {
            // Write initial silence to prime the device
            WriteBuffer(_hdr1Ptr, _dataBuf1, true);
            WriteBuffer(_hdr2Ptr, _dataBuf2, true);

            while (_playing)
            {
                try
                {
                    if ((ReadFlags(_hdr1Ptr) & 0x00000001) != 0)
                    {
                        waveOutUnprepareHeader(_hWaveOut, _hdr1Ptr, HDR_SIZE);
                        WriteBuffer(_hdr1Ptr, _dataBuf1, false);
                    }

                    if ((ReadFlags(_hdr2Ptr) & 0x00000001) != 0)
                    {
                        waveOutUnprepareHeader(_hWaveOut, _hdr2Ptr, HDR_SIZE);
                        WriteBuffer(_hdr2Ptr, _dataBuf2, false);
                    }
                }
                catch (Exception ex)
                {
                    Core.Logger.Log($"[VoicePlayback] Error: {ex.Message}");
                }

                Thread.Sleep(2);
            }
        }

        private void WriteBuffer(IntPtr hdrPtr, IntPtr dataPtr, bool silence)
        {
            byte[] chunk = new byte[BUFFER_SIZE];
            bool hasData = false;

            if (!silence)
            {
                lock (_ringLock)
                {
                    // Jitter buffer: wait until enough data has accumulated
                    if (_buffering)
                    {
                        if (_available >= JITTER_THRESHOLD)
                        {
                            _buffering = false;
                        }
                        // While buffering, output silence
                    }

                    if (!_buffering && _available >= BUFFER_SIZE)
                    {
                        for (int i = 0; i < BUFFER_SIZE; i++)
                        {
                            chunk[i] = _ringBuffer[_readPos];
                            _readPos = (_readPos + 1) % RING_BUFFER_SIZE;
                        }
                        _available -= BUFFER_SIZE;
                        hasData = true;
                        
                        // Re-enter buffering mode if we run dry
                        if (_available < BUFFER_SIZE)
                        {
                            _buffering = true;
                        }
                    }
                }

                // Apply volume with boost
                if (hasData)
                {
                    float totalGain = _volume * VOLUME_BOOST;
                    for (int i = 0; i < BUFFER_SIZE; i += 2)
                    {
                        short s = (short)(chunk[i] | (chunk[i + 1] << 8));
                        float f = s * totalGain;
                        if (f > short.MaxValue) f = short.MaxValue;
                        if (f < short.MinValue) f = short.MinValue;
                        short fs = (short)f;
                        chunk[i] = (byte)(fs & 0xFF);
                        chunk[i + 1] = (byte)((fs >> 8) & 0xFF);
                    }
                }
            }

            Marshal.Copy(chunk, 0, dataPtr, BUFFER_SIZE);
            Marshal.WriteInt32(hdrPtr, IntPtr.Size + 4, 0);
            Marshal.WriteInt32(hdrPtr, FLAGS_OFFSET, 0);
            waveOutPrepareHeader(_hWaveOut, hdrPtr, HDR_SIZE);
            waveOutWrite(_hWaveOut, hdrPtr, HDR_SIZE);
        }

        public void Dispose()
        {
            _playing = false;
            _playThread?.Join(500);

            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);
                waveOutUnprepareHeader(_hWaveOut, _hdr1Ptr, HDR_SIZE);
                waveOutUnprepareHeader(_hWaveOut, _hdr2Ptr, HDR_SIZE);
                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
            }

            if (_dataBuf1 != IntPtr.Zero) { Marshal.FreeHGlobal(_dataBuf1); _dataBuf1 = IntPtr.Zero; }
            if (_dataBuf2 != IntPtr.Zero) { Marshal.FreeHGlobal(_dataBuf2); _dataBuf2 = IntPtr.Zero; }
            if (_hdr1Ptr != IntPtr.Zero) { Marshal.FreeHGlobal(_hdr1Ptr); _hdr1Ptr = IntPtr.Zero; }
            if (_hdr2Ptr != IntPtr.Zero) { Marshal.FreeHGlobal(_hdr2Ptr); _hdr2Ptr = IntPtr.Zero; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHdr
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(ref IntPtr hWaveOut, uint deviceId, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint dwFlags);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hWaveOut);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveOutHdr, int uSize);
    }
}
