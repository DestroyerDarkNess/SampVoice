using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;

namespace UniversalVoiceChat.Core
{
    public static class Config
    {
        // [Relay]
        public static string RelayUrl { get; private set; } = "ws://localhost:8000";

        // [Audio]
        public static float MaxDistance { get; private set; } = 40.0f;
        public static float InputGain { get; private set; } = 1.0f;
        public static float OutputGain { get; private set; } = 1.0f;

        // [VoiceActivation]
        public static int PushToTalkKey { get; private set; } = 0x42; // B key

        // [Channels] — Future feature
        public static Dictionary<string, (int Id, string Password)> Channels { get; private set; } = new();

        private static string DefaultIniContent = @"; ============================================================
; Universal Voice Chat Configuration
; Place this file next to gta_sa.exe
; ============================================================

[Relay]
; WebSocket URL of the Voice Relay Server
Url=ws://localhost:8000

[Audio]
; Maximum hearing distance in game units (meters)
MaxDistance=40.0
InputGain=1.0
OutputGain=1.0

[VoiceActivation]
; Push-to-Talk key (0x42 = B key). Set to 0 for always-on.
PushToTalkKey=0x42
";

        public static void Load()
        {
            string iniPath = GetIniPath();

            if (!File.Exists(iniPath))
            {
                Logger.Log($"[Config] No INI found at: {iniPath} — creating default one.");
                try
                {
                    File.WriteAllText(iniPath, DefaultIniContent);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Config] Failed to create INI: {ex.Message}");
                }
                return;
            }

            Logger.Log($"[Config] Loading: {iniPath}");

            string currentSection = "";
            foreach (string rawLine in File.ReadAllLines(iniPath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(';')) continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    currentSection = line.Substring(1, line.Length - 2).ToLowerInvariant();
                    continue;
                }

                int eqIndex = line.IndexOf('=');
                if (eqIndex < 0) continue;

                string key = line.Substring(0, eqIndex).Trim().ToLowerInvariant();
                string value = line.Substring(eqIndex + 1).Trim();

                switch (currentSection)
                {
                    case "relay":
                        if (key == "url") RelayUrl = value;
                        break;

                    case "audio":
                        if (key == "maxdistance") MaxDistance = ParseFloat(value, 40.0f);
                        if (key == "inputgain") InputGain = ParseFloat(value, 1.0f);
                        if (key == "outputgain") OutputGain = ParseFloat(value, 1.0f);
                        break;

                    case "voiceactivation":
                        if (key == "pushtotalkkey") PushToTalkKey = ParseHexOrInt(value, 0x42);
                        break;

                    case "channels":
                        // Format: ChannelName=ID,Password
                        string channelName = line.Substring(0, eqIndex).Trim();
                        string[] parts = value.Split(',');
                        if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int chId))
                        {
                            Channels[channelName] = (chId, parts[1].Trim());
                        }
                        break;
                }
            }

            Logger.Log($"[Config] RelayUrl={RelayUrl}");
            Logger.Log($"[Config] MaxDistance={MaxDistance}, InputGain={InputGain}, OutputGain={OutputGain}");
            Logger.Log($"[Config] PushToTalkKey=0x{PushToTalkKey:X2}");
            if (Channels.Count > 0)
                Logger.Log($"[Config] {Channels.Count} radio channel(s) loaded.");
        }

        private static string GetIniPath()
        {
            // The INI lives next to gta_sa.exe (same directory where the .asi is placed)
            string? exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            return Path.Combine(exeDir ?? ".", "UniversalVoiceChat.ini");
        }

        private static float ParseFloat(string s, float fallback)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        private static int ParseHexOrInt(string s, int fallback)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex) ? hex : fallback;
            return int.TryParse(s, out int dec) ? dec : fallback;
        }
    }
}
