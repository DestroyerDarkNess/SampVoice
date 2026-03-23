using System;
using System.IO;
using System.Diagnostics;

namespace UniversalVoiceChat.Core
{
    public static class Logger
    {
        private static string _logPath;
        private static object _lock = new object();

        static Logger()
        {
            try
            {
                string? exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                _logPath = Path.Combine(exeDir ?? ".", "UniversalVoiceChat.log");
                
                // Clear old log at startup
                if (File.Exists(_logPath))
                {
                    File.Delete(_logPath);
                }
            }
            catch { }
        }

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    string formatted = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                    File.AppendAllText(_logPath, formatted);
                }
            }
            catch { }
        }
    }
}
