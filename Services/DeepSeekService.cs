using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Storage;

namespace NewsImpactRanker.WinForms.Services
{
    public class DeepSeekService : IAiProvider
    {
        private readonly HttpClient _httpClient;

        public string Name => "DEEPSEEK";

        public DeepSeekService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NewsImpactRanker/2.1");
        }

        public async Task<ServiceResult<TopicScoresResponse>> ClassifyAsync(string text, string prompt)
        {
            var config = StorageManager.LoadConfig();

            if (string.IsNullOrWhiteSpace(config.DeepSeekApiKey))
            {
                return ServiceResult<TopicScoresResponse>.Fail("API Key da DeepSeek não configurada.");
            }

            string baseUrl = string.IsNullOrWhiteSpace(config.DeepSeekBaseUrl)
                ? "https://api.deepseek.com"
                : config.DeepSeekBaseUrl.Trim().TrimEnd('/');

            string model = string.IsNullOrWhiteSpace(config.DeepSeekModel)
                ? "deepseek-chat"
                : config.DeepSeekModel.Trim();

            string preparedText = PrepareText(text);
            string preparedPrompt = ApplySummaryWordCount(prompt, config.SummaryWordCount);

            var payload = new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = preparedPrompt },
                    new { role = "user", content = "Analyze the following news article and return ONLY the JSON result.\n\n" + preparedText }
                },
                temperature = 0.1,
                max_tokens = 1500,
                response_format = new { type = "json_object" }
            };

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions"))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.DeepSeekApiKey);
                    request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        return ServiceResult<TopicScoresResponse>.Fail($"Erro DeepSeek API: {(int)response.StatusCode} {response.StatusCode} - {responseBody}");
                    }

                    var json = JObject.Parse(responseBody);
                    string content = json["choices"]?[0]?["message"]?["content"]?.ToString();
                    return AiResponseParser.ParseAndNormalize(content, Name);
                }
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<TopicScoresResponse>.Fail("Timeout ao chamar DeepSeek.");
            }
            catch (Exception ex)
            {
                return ServiceResult<TopicScoresResponse>.Fail($"Exceção DeepSeek: {ex.Message}");
            }
        }

        private static string PrepareText(string text)
        {
            text = (text ?? "").Trim();
            return text.Length > 2000 ? text.Substring(0, 2000) : text;
        }

        private static string ApplySummaryWordCount(string prompt, int summaryWordCount)
        {
            int count = summaryWordCount > 0 ? summaryWordCount : 10;
            return (prompt ?? "")
                .Replace("10 words", $"{count} words")
                .Replace("10 palavras", $"{count} palavras");
        }
    }
}
