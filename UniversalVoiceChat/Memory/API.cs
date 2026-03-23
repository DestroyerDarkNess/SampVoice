using System;
using System.Numerics;

namespace UniversalVoiceChat.Memory
{
    public static class API
    {
        // Player position -------------------------------------------------------
        public static unsafe bool TryGetPlayerPosition(out Vector3 pos)
        {
            try
            {
                uint ped = *(uint*)Offsets.R(Offsets.PED_PTR_ADDR);
                if (ped == 0) { pos = default; return false; }

                uint matrix = *(uint*)(ped + Offsets.MAT_OFFSET);
                if (matrix == 0) { pos = default; return false; }

                float x = *(float*)(matrix + Offsets.X_OFFSET);
                float y = *(float*)(matrix + Offsets.Y_OFFSET);
                float z = *(float*)(matrix + Offsets.Z_OFFSET);

                pos = new Vector3(x, y, z);
                return true;
            }
            catch
            {
                pos = default;
                return false;
            }
        }
    }
}
