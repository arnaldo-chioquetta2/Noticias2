using System;
using System.IO;
using System.Collections.Generic;
using NewsImpactRanker.WinForms.Storage;

namespace NewsImpactRanker.WinForms.Services
{
    public static class LogService
    {
        private static readonly object _lock = new object();
        private static bool _initialized = false;
        private static string _logFilePath;
        public static Dictionary<string, string> FalhasProcessamento { get; } = new Dictionary<string, string>();

        static LogService()
        {
            try
            {
                Directory.CreateDirectory(StorageManager.LogsPath);

                _logFilePath = Path.Combine(StorageManager.LogsPath, "execution.log");

                _initialized = true;
            }
            catch (Exception ex)
            {
                _initialized = false;
                Console.WriteLine($"[LOG FALHOU] {ex.Message}");
            }
        }

        public static void AddFalha(string url, string motivo)
        {
            lock (FalhasProcessamento)
            {
                FalhasProcessamento[url] = motivo;
            }
        }

        public static void Log(string message, string level = "INFO")
        {
            if (!_initialized)
            {
                Console.WriteLine($"[{level}] {message}");
                return;
            }

            try
            {
                string entry =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";

                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, entry);
                    Console.WriteLine(entry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOG ERROR] {ex.Message} - {message}");
            }
        }

        public static void Info(string message) => Log(message, "INFO");
        public static void Warn(string message) => Log(message, "WARN");

        public static void Error(string message, Exception ex = null)
        {
            string msg = ex != null
                ? $"{message} - {ex.Message}{Environment.NewLine}{ex.StackTrace}"
                : message;

            Log(msg, "ERROR");
        }

        public static void Debug(string message) => Log(message, "DEBUG");

        // 🔥 Novo: permitir acesso ao caminho do log
        public static string GetLogPath()
        {
            return _logFilePath;
        }

        public static void ResetLog()
        {
            try
            {
                if (File.Exists(_logFilePath))
                    File.Delete(_logFilePath);
            }
            catch
            {
                // evitar crash por log
            }
        }

    }
}