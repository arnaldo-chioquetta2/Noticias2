using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Storage;
using System.IO;
using System.Collections.Generic;

namespace NewsImpactRanker.WinForms.Services
{
    public class GroqService
    {
        private readonly HttpClient _httpClient;
        private readonly AppConfig _config;
        private static int _totalTokensToday = 0;
        private static int _totalRequestsToday = 0;
        private static int _totalTokensExecution = 0;
        private static int _totalRequestsExecution = 0;
        private string _lastRawResponse;

        private readonly object _newsScoresLock = new object();
        private readonly List<NewsScoresItem> _allNewsScores = new List<NewsScoresItem>();

        private static readonly SemaphoreSlim _rateLimiter = new SemaphoreSlim(2, 2);

        private const int MAX_TEXT_LENGTH = 3000;
        private const int MAX_TOKENS = 300;

        public GroqService()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NewsImpactRanker/1.0");
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            _config = StorageManager.LoadConfig();
        }

        public async Task<TopicScoresResponse> ClassifyNewsAsync(string text)
        {
            LogService.Info("METHOD v3: ClassifyNewsAsync");

            LogService.Info($"🧾 Texto p/ IA - len={text?.Length ?? 0}");
            LogService.Info(" ");
            LogService.Info(text ?? "");
            LogService.Info(" ");

            if (string.IsNullOrWhiteSpace(_config.AiApiKey))
                throw new Exception("API Key da Groq não configurada.");

            text = (text ?? "").Trim();

            if (text.Length > 4000)
                text = text.Substring(0, 4000);

            if (string.IsNullOrWhiteSpace(_config.PromptFilePath) || !File.Exists(_config.PromptFilePath))
                throw new Exception("Arquivo de prompt não configurado.");

            string systemPrompt = File.ReadAllText(_config.PromptFilePath);

            string url = "https://api.groq.com/openai/v1/chat/completions";

            var payload = new
            {
                model = string.IsNullOrWhiteSpace(_config.AiModel)
                    ? "llama-3.1-8b-instant"
                    : _config.AiModel,

                messages = new[]
                {
            new
            {
                role = "system",
                content = systemPrompt
            },
            new
            {
                role = "user",
                content = "Analyze the following news article and return ONLY the JSON result.\n\n" + text
            }
        },

                temperature = 0.2,
                max_tokens = 1800,
                response_format = new { type = "json_object" }
            };

            string apiResponse = await SendRequestForTopicsAsync(
                url,
                JsonConvert.SerializeObject(payload)
            );

            LogService.Info("🌐 IA RAW RESPONSE:");
            LogService.Info(apiResponse ?? "");

            // A resposta já vem como JSON final
            string content = (apiResponse ?? "").Trim();

            if (string.IsNullOrWhiteSpace(content))
                throw new Exception("IA retornou conteúdo vazio.");

            LogService.Info("📦 Conteúdo retornado pela IA:");
            LogService.Info(content);

            // remover code fences caso existam
            content = content
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();

            TopicScoresResponse parsed = null;

            try
            {
                parsed = JsonConvert.DeserializeObject<TopicScoresResponse>(content);
            }
            catch
            {
                LogService.Warn("Falha ao desserializar JSON direto. Tentando extração manual...");

                int start = content.IndexOf("{");
                int end = content.LastIndexOf("}");

                if (start >= 0 && end > start)
                {
                    string jsonOnly = content.Substring(start, end - start + 1);

                    LogService.Info("🔧 JSON extraído:");
                    LogService.Info(jsonOnly);

                    parsed = JsonConvert.DeserializeObject<TopicScoresResponse>(jsonOnly);
                }
            }

            if (parsed == null)
                throw new Exception("Falha ao desserializar resposta da IA.");

            // novo prompt usa "scores"
            if (parsed.scores == null)
                parsed.scores = new Dictionary<string, int>();

            // garantir todas as chaves esperadas
            EnsureAllTopics(parsed.scores);

            // clamp de segurança 0..100
            foreach (var key in parsed.scores.Keys.ToList())
            {
                parsed.scores[key] = Math.Max(0, Math.Min(100, parsed.scores[key]));
            }

            return parsed;
        }

        private void EnsureAllTopics(Dictionary<string, int> scores)
        {
            LogService.Info("METHOD v1: EnsureAllTopics");

            foreach (var code in NewsImpactRanker.WinForms.Models.TopicCatalog.Codes)
            {
                if (!scores.ContainsKey(code))
                    scores[code] = 0;
            }

            // clamp 0..100
            var keys = new List<string>(scores.Keys);
            foreach (var k in keys)
            {
                if (scores[k] < 0) scores[k] = 0;
                if (scores[k] > 100) scores[k] = 100;
            }
        }

        private async Task<string> SendRequestForTopicsAsync(string url, string jsonPayload)
        {
            using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = content;

                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _config.AiApiKey);

                var response = await _httpClient.SendAsync(request);

                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Erro Groq API: {response.StatusCode} - {responseBody}");

                var result = JObject.Parse(responseBody);

                string textResult = result["choices"]?[0]?["message"]?["content"]?.ToString();

                if (string.IsNullOrWhiteSpace(textResult))
                    throw new Exception("Groq retornou conteúdo vazio.");

                int start = textResult.IndexOf('{');
                int end = textResult.LastIndexOf('}');

                if (start >= 0 && end > start)
                    textResult = textResult.Substring(start, end - start + 1);

                return textResult;
            }
        }

        private List<TopicResult> SelectBestNewsPerTopic()
        {
            LogService.Info("METHOD v1: SelectBestNewsPerTopic");
            LogService.Info("DEBUG: Entrou em SelectBestNewsPerTopic");

            var results = new List<TopicResult>();

            // cópia do que já foi classificado
            List<NewsScoresItem> availableNews;
            lock (_newsScoresLock)
            {
                availableNews = new List<NewsScoresItem>(_allNewsScores);
            }

            LogService.Info($"DEBUG: availableNews inicial = {availableNews.Count}");

            if (availableNews.Count == 0)
                return results;

            // Se nenhuma notícia tem qualquer score > 0, não seleciona nada.
            int maxAny = 0;
            foreach (var n in availableNews)
            {
                if (n?.Scores == null) continue;
                foreach (var v in n.Scores.Values)
                    if (v > maxAny) maxAny = v;
            }

            if (maxAny <= 0)
            {
                LogService.Info("DEBUG: Nenhuma notícia possui score > 0 em qualquer tópico.");
                return results;
            }

            foreach (var code in NewsImpactRanker.WinForms.Models.TopicCatalog.Codes)
            {
                if (availableNews.Count == 0)
                {
                    LogService.Info("DEBUG: Não há mais notícias disponíveis.");
                    break;
                }

                var topicName = NewsImpactRanker.WinForms.Models.TopicCatalog.CodeToName.ContainsKey(code)
                    ? NewsImpactRanker.WinForms.Models.TopicCatalog.CodeToName[code]
                    : code;

                var ranked = availableNews
                    .Select(n => new
                    {
                        News = n,
                        Score = (n.Scores != null && n.Scores.ContainsKey(code)) ? n.Scores[code] : 0,
                        Total = (n.Scores != null) ? n.Scores.Values.Sum() : 0,
                        Order = n.SourceOrder
                    })
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Total)
                    .ThenBy(x => x.Order)
                    .FirstOrDefault();

                if (ranked == null)
                    continue;

                // Se o melhor score deste tópico é 0, NÃO consome URL.
                if (ranked.Score <= 0)
                {
                    LogService.Info($"DEBUG: tópico {topicName} ignorado (bestScore=0)");
                    continue;
                }

                results.Add(new TopicResult
                {
                    Topic = topicName,
                    Url = ranked.News.Url,
                    Score = ranked.Score
                });

                LogService.Info($"DEBUG: selecionada {ranked.News.Url} para {topicName} (score={ranked.Score})");

                // remove a notícia vencedora (não pode ganhar outro tópico)
                availableNews.Remove(ranked.News);

                LogService.Info($"DEBUG: remainingNews = {availableNews.Count}");
            }

            LogService.Info($"DEBUG: topicResults.Count = {results.Count}");
            return results;
        }

        public static int GetTotalTokensToday()
        {
            return _totalTokensToday;
        }

    }
}