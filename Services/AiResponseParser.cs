using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NewsImpactRanker.WinForms.Models;

namespace NewsImpactRanker.WinForms.Services
{
    public static class AiResponseParser
    {
        public static ServiceResult<TopicScoresResponse> ParseAndNormalize(string content, string providerName)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return ServiceResult<TopicScoresResponse>.Fail($"{providerName} retornou conteúdo vazio.");
            }

            string cleanJson = ExtractJson(content);
            TopicScoresResponse parsed;

            try
            {
                parsed = JsonConvert.DeserializeObject<TopicScoresResponse>(cleanJson);
            }
            catch (Exception ex)
            {
                return ServiceResult<TopicScoresResponse>.Fail($"JSON inválido do {providerName}: {ex.Message}. Texto: {TrimForLog(content)}");
            }

            if (parsed == null)
            {
                return ServiceResult<TopicScoresResponse>.Fail($"{providerName} retornou JSON vazio.");
            }

            if (parsed.Scores == null)
            {
                return ServiceResult<TopicScoresResponse>.Fail($"{providerName} retornou JSON sem o objeto 'scores'.");
            }

            EnsureAllTopics(parsed.Scores);
            return ServiceResult<TopicScoresResponse>.Ok(parsed);
        }

        public static string ExtractJson(string content)
        {
            string clean = (content ?? "")
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();

            int start = clean.IndexOf("{", StringComparison.Ordinal);
            int end = clean.LastIndexOf("}", StringComparison.Ordinal);

            if (start >= 0 && end > start)
            {
                return clean.Substring(start, end - start + 1);
            }

            return clean;
        }

        private static void EnsureAllTopics(Dictionary<string, int> scores)
        {
            foreach (var code in TopicCatalog.Codes)
            {
                if (!scores.ContainsKey(code))
                {
                    scores[code] = 0;
                }
            }

            foreach (var key in scores.Keys.ToList())
            {
                scores[key] = Math.Max(0, Math.Min(100, scores[key]));
            }
        }

        private static string TrimForLog(string text)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Substring(0, Math.Min(text.Length, 180));
        }
    }
}
