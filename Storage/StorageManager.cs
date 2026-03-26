using System;
using System.IO;
using Newtonsoft.Json;
using NewsImpactRanker.WinForms.Models;

namespace NewsImpactRanker.WinForms.Storage
{
    public static class StorageManager
    {
        // ?? 1. MUDANÇA AQUI: Criamos uma pasta exclusiva adicionando "_Science" no final
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NewsImpactRanker_Science"
        );

        // ?? 2. MUDANÇA AQUI: Renomeamos os arquivos por precaução
        public static readonly string ConfigPath = Path.Combine(AppDataPath, "config_science.json");
        public static readonly string CachePath = Path.Combine(AppDataPath, "cache_science.json");
        // public static readonly string LogsPath = Path.Combine(AppDataPath, "logs");
        public static readonly string LogsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        static StorageManager()
        {
            if (!Directory.Exists(AppDataPath)) Directory.CreateDirectory(AppDataPath);
            if (!Directory.Exists(LogsPath)) Directory.CreateDirectory(LogsPath);
        }

        public static AppConfig LoadConfig()
        {
            if (!File.Exists(ConfigPath)) return new AppConfig();
            try
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public static void SaveConfig(AppConfig config)
        {
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
        }
    }
}