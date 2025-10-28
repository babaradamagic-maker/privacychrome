using System;
using System.IO;

namespace PrivacyChrome
{
    internal static class DebugLogger
    {
        private static readonly object _lock = new object();
        private static readonly string _path = Path.Combine(Path.GetTempPath(), "privacychrome.log");

        public static void Log(string message)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_path, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Swallow logging errors to avoid interfering with app behavior
            }
        }

        public static void Log(string fmt, params object[] args)
        {
            try
            {
                Log(string.Format(fmt, args));
            }
            catch
            {
                // ignore
            }
        }
    }
}