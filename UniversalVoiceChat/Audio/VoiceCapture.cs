using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.Wave;

namespace UniversalVoiceChat.Audio
{
    /// <summary>
    /// Pure P/Invoke microphone capture using winmm.dll waveIn* functions.
    /// WaveHdr structs are allocated in NATIVE MEMORY so Windows can update
    /// dwFlags (WHDR_DONE) reliably, even if the GC moves managed objects.
    /// </summary>
    public class VoiceCapture : IDisposable
    {
        public event EventHandler<WaveInEventArgs> DataAvailable;

        private IntPtr _hWaveIn = IntPtr.Zero;
        
        // Native memory for audio data buffers
        private IntPtr _dataBuf1, _dataBuf2;
        // Native memory for WaveHdr structs
        private IntPtr _hdr1Ptr, _hdr2Ptr;
        
        private bool _recording = false;
        private Thread? _pollThread;

        private const int SAMPLE_RATE = 48000;
        private const int CHANNELS = 1;
        private const int BITS_PER_SAMPLE = 16;
        private const int BUFFER_MS = 20;
        private const int BUFFER_SIZE = SAMPLE_RATE * CHANNELS * (BITS_PER_SAMPLE / 8) * BUFFER_MS / 1000; // 1920

        // Offsets into WaveHdr struct
        private static readonly int BYTES_RECORDED_OFFSET = IntPtr.Size + 4; // after lpData + dwBufferLength
        private static readonly int FLAGS_OFFSET = IntPtr.Size + 4 + 4 + IntPtr.Size; // after lpData + dwBufferLength + dwBytesRecorded + dwUser
        private static readonly int HDR_SIZE = Marshal.SizeOf<WaveHdr>();

        public VoiceCapture() { }

        public void Start()
        {
            var fmt = new WaveFormatEx
            {
                wFormatTag = 1,
                nChannels = CHANNELS,
                nSamplesPerSec = SAMPLE_RATE,
                wBitsPerSample = BITS_PER_SAMPLE,
                nBlockAlign = (short)(CHANNELS * BITS_PER_SAMPLE / 8),
                nAvgBytesPerSec = SAMPLE_RATE * CHANNELS * BITS_PER_SAMPLE / 8,
                cbSize = 0
            };

            int result = waveInOpen(ref _hWaveIn, 0xFFFFFFFF, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (result != 0)
            {
                Core.Logger.Log($"[VoiceCapture] waveInOpen failed: {result}");
                return;
            }

            // Allocate data buffers in native memory
            _dataBuf1 = Marshal.AllocHGlobal(BUFFER_SIZE);
            _dataBuf2 = Marshal.AllocHGlobal(BUFFER_SIZE);

            // Allocate WaveHdr structs in NATIVE MEMORY
            _hdr1Ptr = Marshal.AllocHGlobal(HDR_SIZE);
            _hdr2Ptr = Marshal.AllocHGlobal(HDR_SIZE);

            // Zero them out
            for (int i = 0; i < HDR_SIZE; i++)
            {
                Marshal.WriteByte(_hdr1Ptr, i, 0);
                Marshal.WriteByte(_hdr2Ptr, i, 0);
            }

            // Set lpData and dwBufferLength
            SetHeaderData(_hdr1Ptr, _dataBuf1);
            SetHeaderData(_hdr2Ptr, _dataBuf2);

            // Prepare headers
            waveInPrepareHeader(_hWaveIn, _hdr1Ptr, HDR_SIZE);
            waveInPrepareHeader(_hWaveIn, _hdr2Ptr, HDR_SIZE);

            // Add buffers to queue
            waveInAddBuffer(_hWaveIn, _hdr1Ptr, HDR_SIZE);
            waveInAddBuffer(_hWaveIn, _hdr2Ptr, HDR_SIZE);

            // Start recording
            result = waveInStart(_hWaveIn);
            if (result != 0)
            {
                Core.Logger.Log($"[VoiceCapture] waveInStart failed: {result}");
                return;
            }

            _recording = true;
            _pollThread = new Thread(PollBuffers) { IsBackground = true, Name = "VoiceCapturePoll" };
            _pollThread.Start();
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

        private uint ReadBytesRecorded(IntPtr hdrPtr)
        {
            return (uint)Marshal.ReadInt32(hdrPtr, BYTES_RECORDED_OFFSET);
        }

        private IntPtr ReadDataPtr(IntPtr hdrPtr)
        {
            return Marshal.ReadIntPtr(hdrPtr, 0);
        }

        private void PollBuffers()
        {
            while (_recording)
            {
                try
                {
                    if ((ReadFlags(_hdr1Ptr) & 0x00000001) != 0) // WHDR_DONE
                        ProcessBuffer(_hdr1Ptr, _dataBuf1);

                    if ((ReadFlags(_hdr2Ptr) & 0x00000001) != 0) // WHDR_DONE
                        ProcessBuffer(_hdr2Ptr, _dataBuf2);
                }
                catch (Exception ex)
                {
                    Core.Logger.Log($"[VoiceCapture] Poll error: {ex.Message}");
                }

                Thread.Sleep(2);
            }
        }

        private void ProcessBuffer(IntPtr hdrPtr, IntPtr dataPtr)
        {
            uint recorded = ReadBytesRecorded(hdrPtr);
            if (recorded > 0)
            {
                byte[] data = new byte[recorded];
                Marshal.Copy(dataPtr, data, 0, (int)recorded);

                DataAvailable?.Invoke(this, new WaveInEventArgs(data, (int)recorded));
            }

            // Unprepare, reset, re-prepare, re-add
            waveInUnprepareHeader(_hWaveIn, hdrPtr, HDR_SIZE);
            
            // Reset dwBytesRecorded and dwFlags
            Marshal.WriteInt32(hdrPtr, BYTES_RECORDED_OFFSET, 0);
            Marshal.WriteInt32(hdrPtr, FLAGS_OFFSET, 0);
            
            waveInPrepareHeader(_hWaveIn, hdrPtr, HDR_SIZE);
            waveInAddBuffer(_hWaveIn, hdrPtr, HDR_SIZE);
        }

        public void Stop()
        {
            _recording = false;
            _pollThread?.Join(500);

            if (_hWaveIn != IntPtr.Zero)
            {
                waveInStop(_hWaveIn);
                waveInReset(_hWaveIn);
                waveInUnprepareHeader(_hWaveIn, _hdr1Ptr, HDR_SIZE);
                waveInUnprepareHeader(_hWaveIn, _hdr2Ptr, HDR_SIZE);
                waveInClose(_hWaveIn);
                _hWaveIn = IntPtr.Zero;
            }

            if (_dataBuf1 != IntPtr.Zero) { Marshal.FreeHGlobal(_dataBuf1); _dataBuf1 = IntPtr.Zero; }
            if (_dataBuf2 != IntPtr.Zero) { Marshal.FreeHGlobal(_dataBuf2); _dataBuf2 = IntPtr.Zero; }
            if (_hdr1Ptr != IntPtr.Zero) { Marshal.FreeHGlobal(_hdr1Ptr); _hdr1Ptr = IntPtr.Zero; }
            if (_hdr2Ptr != IntPtr.Zero) { Marshal.FreeHGlobal(_hdr2Ptr); _hdr2Ptr = IntPtr.Zero; }
        }

        public void Dispose() => Stop();

        // ─── Native Structures ────────────────────────────────────────
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

        // ─── winmm.dll P/Invoke (using IntPtr for headers) ───────────
        [DllImport("winmm.dll")]
        private static extern int waveInOpen(ref IntPtr hWaveIn, uint deviceId, ref WaveFormatEx lpFormat, IntPtr dwCallback, IntPtr dwInstance, uint dwFlags);

        [DllImport("winmm.dll")]
        private static extern int waveInClose(IntPtr hWaveIn);

        [DllImport("winmm.dll")]
        private static extern int waveInStart(IntPtr hWaveIn);

        [DllImport("winmm.dll")]
        private static extern int waveInStop(IntPtr hWaveIn);

        [DllImport("winmm.dll")]
        private static extern int waveInReset(IntPtr hWaveIn);

        [DllImport("winmm.dll")]
        private static extern int waveInPrepareHeader(IntPtr hWaveIn, IntPtr lpWaveInHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveInUnprepareHeader(IntPtr hWaveIn, IntPtr lpWaveInHdr, int uSize);

        [DllImport("winmm.dll")]
        private static extern int waveInAddBuffer(IntPtr hWaveIn, IntPtr lpWaveInHdr, int uSize);
    }
}
