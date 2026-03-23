using System;
using System.Numerics;
using UniversalVoiceChat.Core;

namespace UniversalVoiceChat.Audio
{
    public static class SpatialMath
    {
        /// <summary>
        /// Calculates the required Volume (0.0 to 1.0) and Panning (-1.0 to 1.0) 
        /// given the exact coordinates of the local player and remote player.
        /// Uses Config.MaxDistance from the INI file.
        /// </summary>
        public static void Calculate3DAudio(Vector3 localPos, Vector3 remotePos, float cameraAngleRad, out float volume, out float pan)
        {
            float maxDist = Config.MaxDistance;

            float dx = remotePos.X - localPos.X;
            float dy = remotePos.Y - localPos.Y;
            float dz = remotePos.Z - localPos.Z;

            float distance = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            
            if (distance >= maxDist)
            {
                volume = 0f;
                pan = 0f;
                return;
            }

            // Linear volume attenuation, multiplied by OutputGain from config
            volume = (1.0f - (distance / maxDist)) * Config.OutputGain;
            volume = Math.Clamp(volume, 0.0f, 1.0f);

            // TODO: Proper panning requires extracting the camera Z angle from memory.
            pan = 0.0f; 
        }
    }
}
