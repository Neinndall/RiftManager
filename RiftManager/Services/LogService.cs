using System;
using System.IO;

namespace RiftManager.Services
{
    public class LogService : IDisposable
    {
        private readonly string _logFilePath;
        private StreamWriter _logFileWriter;

        public event Action<string, string> OnLogMessage; // Level, Message

        public LogService()
        {
            string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDirectory);
            _logFilePath = Path.Combine(logDirectory, "application.log");

            try
            {
                _logFileWriter = new StreamWriter(_logFilePath, append: true) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                _logFileWriter = null;
                OnLogMessage?.Invoke("ERROR", $"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} Could not initiate log file at '{_logFilePath}': {ex.Message}");
            }
        }

        private void WriteLogEntry(string level, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string formattedMessage = $"[{level}] {message}";

            OnLogMessage?.Invoke(level, message);

            if (_logFileWriter != null)
            {
                try
                {
                    _logFileWriter.WriteLine($"{timestamp} {formattedMessage}");
                }
                catch (Exception ex)
                {
                    OnLogMessage?.Invoke("ERROR", $"[ERROR] {timestamp} Failed to write to log file: {ex.Message}");
                }
            }
        }

        public void Log(string message) => WriteLogEntry("INFO", message);
        public void LogWarning(string message) => WriteLogEntry("WARNING", message);
        public void LogError(string message) => WriteLogEntry("ERROR", message);
        public void LogSuccess(string message) => WriteLogEntry("SUCCESS", message);
        public void LogDebug(string message) => WriteLogEntry("DEBUG", message);

        public void Dispose()
        {
            if (_logFileWriter != null)
            {
                _logFileWriter.Close();
                _logFileWriter.Dispose();
                _logFileWriter = null;
            }
        }
    }
}
