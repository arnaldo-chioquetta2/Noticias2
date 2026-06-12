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
    public class MistralService : IAiProvider
    {
        private readonly HttpClient _httpClient;

        public string Name => "MISTRAL";

        public MistralService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NewsImpactRanker/2.1");
        }

        public async Task<ServiceResult<TopicScoresResponse>> ClassifyAsync(string text, string prompt)
        {
            var config = StorageManager.LoadConfig();

            if (string.IsNullOrWhiteSpace(config.MistralApiKey))
            {
                return ServiceResult<TopicScoresResponse>.Fail("API Key da Mistral nao configurada.");
            }

            string model = string.IsNullOrWhiteSpace(config.MistralModel)
                ? "open-mixtral-8x7b"
                : config.MistralModel.Trim();

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
                LogService.Info("[MISTRAL] Tentando classificar noticia...");

                using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.mistral.ai/v1/chat/completions"))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.MistralApiKey);
                    request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        LogService.Error($"[MISTRAL] Erro HTTP: {(int)response.StatusCode} {response.StatusCode} - {responseBody}");
                        return ServiceResult<TopicScoresResponse>.Fail($"Erro Mistral API: {(int)response.StatusCode} {response.StatusCode} - {responseBody}");
                    }

                    var json = JObject.Parse(responseBody);
                    string content = json["choices"]?[0]?["message"]?["content"]?.ToString();

                    if (string.IsNullOrWhiteSpace(content))
                    {
                        LogService.Error("[MISTRAL] Conteudo vazio retornado pela API.");
                        return ServiceResult<TopicScoresResponse>.Fail("Mistral retornou conteudo vazio.");
                    }

                    var parsed = AiResponseParser.ParseAndNormalize(content, Name);
                    if (parsed.Success)
                    {
                        LogService.Info("[MISTRAL] Resposta recebida com sucesso.");
                    }
                    else
                    {
                        LogService.Error($"[MISTRAL] Falha ao parsear JSON. Conteudo bruto: {content}");
                    }

                    return parsed;
                }
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<TopicScoresResponse>.Fail("Timeout ao chamar Mistral.");
            }
            catch (Exception ex)
            {
                return ServiceResult<TopicScoresResponse>.Fail($"Excecao Mistral: {ex.Message}");
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
