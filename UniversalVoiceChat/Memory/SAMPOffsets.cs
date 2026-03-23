using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace UniversalVoiceChat.Memory
{
    internal static unsafe class SAMPOffsets
    {
        private static IntPtr _sampBase = IntPtr.Zero;
        private static int _activeSAMPInfoOffset = 0;

        // Base Offsets for SAMP_INFO pointer in different SA:MP versions
        private static readonly int[] KnownSAMPInfoOffsets = new int[]
        {
            0x21A0F8, // 0.3.7 R1
            0x21A100, // 0.3.7 R2 (Approx)
            0x26E8DC, // 0.3.7 R3
            0x26EA0C, // 0.3.7 R4
            0x26EB94, // 0.3.7 R5
            0x2ACA24  // 0.3.DL
        };

        public const int IP_OFFSET = 0x20;
        public const int PORT_OFFSET = 0x225;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, [Out] byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        public static bool Initialize()
        {
            if (_sampBase != IntPtr.Zero && _activeSAMPInfoOffset != 0) return true;

            try
            {
                foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
                {
                    if (module.ModuleName != null && module.ModuleName.Equals("samp.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        _sampBase = module.BaseAddress;
                        DetectActiveVersion();
                        return _activeSAMPInfoOffset != 0;
                    }
                }
            }
            catch { }

            return false;
        }

        private static void DetectActiveVersion()
        {
            IntPtr hProcess = Process.GetCurrentProcess().Handle;
            byte[] ipBuffer = new byte[257];

            foreach (var offset in KnownSAMPInfoOffsets)
            {
                try
                {
                    uint pSAMPInfo = *(uint*)(_sampBase + offset);
                    if (pSAMPInfo == 0) continue;

                    IntPtr targetAddress = (IntPtr)(pSAMPInfo + IP_OFFSET);
                    
                    // Safely read memory without crashing if pointer is invalid
                    if (ReadProcessMemory(hProcess, targetAddress, ipBuffer, ipBuffer.Length, out IntPtr bytesRead))
                    {
                        string ipString = Encoding.ASCII.GetString(ipBuffer);
                        int nullIndex = ipString.IndexOf('\0');
                        if (nullIndex > 0)
                        {
                            ipString = ipString.Substring(0, nullIndex);
                            
                            if (IsValidIpOrDomain(ipString))
                            {
                                _activeSAMPInfoOffset = offset;
                                Console.WriteLine($"[UniversalVoiceChat] Detected SA:MP Version Offset: 0x{offset:X}");
                                return;
                            }
                        }
                    }
                }
                catch { } // Ignore memory read exceptions
            }
        }

        private static bool IsValidIpOrDomain(string str)
        {
            if (str.Length < 3 || str.Length > 255) return false;
            foreach (char c in str)
            {
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-') return false;
            }
            return true;
        }

        public static bool TryGetServerInfo(out string ip, out int port)
        {
            ip = string.Empty;
            port = 0;

            if (!Initialize()) return false;

            try
            {
                uint pSAMPInfo = *(uint*)(_sampBase + _activeSAMPInfoOffset);
                if (pSAMPInfo == 0) return false;

                byte* pIP = (byte*)(pSAMPInfo + IP_OFFSET);
                string readIp = Marshal.PtrToStringAnsi((IntPtr)pIP);
                
                if (string.IsNullOrEmpty(readIp) || !IsValidIpOrDomain(readIp)) return false;

                ip = readIp;
                port = *(int*)(pSAMPInfo + PORT_OFFSET);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
