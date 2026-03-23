using System;
using System.Diagnostics;

namespace UniversalVoiceChat.Memory
{
    internal static unsafe class Offsets
    {
        // === STATIC OFFSETS (GTA SA 1.0 US)
        public const int PED_PTR_ADDR = 0xB6F5F0;
        public const int MAT_OFFSET = 0x14;
        public const int X_OFFSET = 0x30;
        public const int Y_OFFSET = 0x34;
        public const int Z_OFFSET = 0x38;

        // ---------------------------------------------------------------------
        private static readonly int _delta;   // signed 32-bit; realBase - preferredBase

        static Offsets()
        {
            try
            {
                IntPtr realBase = Process.GetCurrentProcess().MainModule.BaseAddress;

                // --- Read PE header in memory to get the preferred ImageBase -----
                byte* pBase = (byte*)realBase;
                int peOffset = *(int*)(pBase + 0x3C);          // DOS->e_lfanew
                bool pe32Plus = (*(ushort*)(pBase + peOffset + 0x18) == 0x20B);

                uint preferredBase =
                    pe32Plus
                        ? *(uint*)(pBase + peOffset + 0x18 + 0x08)  // PE32+: 8 bytes after Magic
                        : *(uint*)(pBase + peOffset + 0x34);        // PE32 : 0x34 after PE header

                _delta = (int)realBase.ToInt32() - (int)preferredBase;
            }
            catch
            {
                _delta = 0; // Fallback
            }
        }

        /// <summary>
        /// Translates an absolute GTA offset (based on preferred ImageBase)
        /// into the *actual* address for this run, compensating ASLR/rebases.
        /// </summary>
        public static IntPtr R(int absoluteOffset) => (IntPtr)(absoluteOffset + _delta);
    }
}
