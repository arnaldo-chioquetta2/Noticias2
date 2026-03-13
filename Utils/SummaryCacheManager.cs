using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Services;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace NewsImpactRanker.WinForms.Utils
{
    public static class SummaryCacheManager
    {
        // Define o caminho do arquivo de resumos em um único lugar
        private static readonly string CacheFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SummaryCache.json");

        // Método para carregar a lista do disco
        public static List<SummaryCacheItem> LoadCache()
        {
            if (!File.Exists(CacheFilePath))
                return new List<SummaryCacheItem>();

            try
            {
                string json = File.ReadAllText(CacheFilePath);
                return JsonConvert.DeserializeObject<List<SummaryCacheItem>>(json) ?? new List<SummaryCacheItem>();
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao ler SummaryCache.json", ex);
                return new List<SummaryCacheItem>();
            }
        }

        // Método para salvar a lista no disco
        public static void SaveCache(List<SummaryCacheItem> cache)
        {
            try
            {
                string json = JsonConvert.SerializeObject(cache, Formatting.Indented);
                File.WriteAllText(CacheFilePath, json);
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao salvar cache de resumos", ex);
            }
        }

        // 👉 O Astro da vez: Método centralizado para limpar tudo
        public static void ClearCache()
        {
            try
            {
                // Sobrescreve o arquivo com uma lista vazia
                File.WriteAllText(CacheFilePath, "[]");
                LogService.Info("[🧹 LIMPEZA] Cache de resumos foi apagado fisicamente.");
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao limpar cache de resumos", ex);
                throw; // Repassa o erro para a tela mostrar a mensagem
            }
        }
    }
}