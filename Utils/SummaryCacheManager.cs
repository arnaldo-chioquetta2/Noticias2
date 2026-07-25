using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NewsImpactRanker.WinForms.Utils
{
    public static class SummaryCacheManager
    {
        public static string CacheFilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NewsRanking_SummaryCache_v2.json"); }
        }

        private static string LegacyBaseDirectoryPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SummaryCache.json"); }
        }

        public static List<SummaryCacheItem> LoadCache()
        {
            string path = CacheFilePath;
            try
            {
                LogService.Info($"[DEDUP] Arquivo histórico: {path}");
                if (!File.Exists(path))
                {
                    LogService.Info("[DEDUP] Arquivo histórico não encontrado; iniciando vazio");
                    return LoadLegacyCacheIfAvailable();
                }

                string json = File.ReadAllText(path);
                var cache = JsonConvert.DeserializeObject<List<SummaryCacheItem>>(json) ?? new List<SummaryCacheItem>();
                LogCacheStats(path, cache);
                return cache;
            }
            catch (Exception ex)
            {
                LogService.Error($"[DEDUP] Erro ao carregar histórico: {ex.GetType().Name}: {ex.Message}", ex);
                return LoadLegacyCacheIfAvailable();
            }
        }

        private static List<SummaryCacheItem> LoadLegacyCacheIfAvailable()
        {
            string path = LegacyBaseDirectoryPath;
            if (!File.Exists(path))
            {
                LogService.Info("[DEDUP] Nenhum arquivo legado de resumo encontrado");
                return new List<SummaryCacheItem>();
            }

            try
            {
                string json = File.ReadAllText(path);
                var cache = JsonConvert.DeserializeObject<List<SummaryCacheItem>>(json) ?? new List<SummaryCacheItem>();
                LogService.Info($"[DEDUP] Histórico legado carregado: {path}");
                LogCacheStats(path, cache);
                return cache;
            }
            catch (Exception ex)
            {
                LogService.Error($"[DEDUP] Erro ao carregar histórico legado: {ex.GetType().Name}: {ex.Message}", ex);
                return new List<SummaryCacheItem>();
            }
        }

        private static void LogCacheStats(string path, List<SummaryCacheItem> cache)
        {
            int canonical = cache.Count(x => x != null && x.IsCanonical && !string.IsNullOrWhiteSpace(x.Summary));
            int ignored = cache.Count - canonical;
            LogService.Info($"[DEDUP] Arquivo histórico: {path}");
            LogService.Info($"[DEDUP] Entradas totais: {cache.Count}");
            LogService.Info($"[DEDUP] Entradas canônicas válidas: {canonical}");
            LogService.Info($"[DEDUP] Entradas ignoradas: {ignored}");
            LogService.Info($"[DEDUP] Tamanho: {new FileInfo(path).Length} bytes; modificado: {File.GetLastWriteTime(path):o}");
        }

        public static void SaveCache(List<SummaryCacheItem> cache)
        {
            try
            {
                string path = CacheFilePath;
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                var snapshot = (cache ?? new List<SummaryCacheItem>()).ToList();
                string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
                File.WriteAllText(path, json);
                LogService.Info($"[DEDUP] Resumo persistido com sucesso");
                LogService.Info($"[DEDUP] Arquivo: {path}");
                LogService.Info($"[DEDUP] Total após gravação: {snapshot.Count}");
            }
            catch (Exception ex)
            {
                LogService.Error($"[DEDUP] Erro ao salvar histórico: {ex.GetType().Name}: {ex.Message}", ex);
            }
        }

        public static void ClearCache()
        {
            try
            {
                File.WriteAllText(CacheFilePath, "[]");
                LogService.Info("[DEDUP] Histórico de resumos limpo");
            }
            catch (Exception ex)
            {
                LogService.Error("[DEDUP] Erro ao limpar histórico de resumos", ex);
                throw;
            }
        }
    }
}
