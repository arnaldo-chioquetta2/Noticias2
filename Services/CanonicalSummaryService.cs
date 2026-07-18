using System;
using System.IO;
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
    public sealed class CanonicalSummaryService
    {
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<ServiceResult<string>> GenerateAsync(string text, AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(text))
                return ServiceResult<string>.Fail("Texto vazio para geração do resumo canônico.");

            int wordCount = config.SummaryWordCount > 0 ? config.SummaryWordCount : 5;
            string prompt = BuildPrompt(wordCount);
            string article = text.Trim();
            if (article.Length > 2500) article = article.Substring(0, 2500);

            switch (config.SelectedProvider)
            {
                case AiProvider.Gemini:
                    return await GenerateGeminiAsync(article, prompt, config);
                case AiProvider.DeepSeek:
                    return await GenerateOpenAiCompatibleAsync(article, prompt,
                        string.IsNullOrWhiteSpace(config.DeepSeekBaseUrl) ? "https://api.deepseek.com" : config.DeepSeekBaseUrl,
                        string.IsNullOrWhiteSpace(config.DeepSeekModel) ? "deepseek-chat" : config.DeepSeekModel,
                        config.DeepSeekApiKey);
                case AiProvider.Mistral:
                    return await GenerateOpenAiCompatibleAsync(article, prompt, "https://api.mistral.ai/v1",
                        string.IsNullOrWhiteSpace(config.MistralModel) ? "open-mixtral-8x7b" : config.MistralModel,
                        config.MistralApiKey);
                case AiProvider.Kimi:
                    return await GenerateOpenAiCompatibleAsync(article, prompt,
                        string.IsNullOrWhiteSpace(config.KimiBaseUrl) ? "https://servidorapi.duckdns.org/v1" : config.KimiBaseUrl,
                        string.IsNullOrWhiteSpace(config.KimiModel) ? "kimi-k2" : config.KimiModel,
                        config.KimiApiKey);
                default:
                    string groqModel = string.IsNullOrWhiteSpace(config.GroqModel) ? config.SelectedModel : config.GroqModel;
                    return await GenerateOpenAiCompatibleAsync(article, prompt, "https://api.groq.com/openai/v1",
                        string.IsNullOrWhiteSpace(groqModel) ? "llama-3.1-8b-instant" : groqModel,
                        config.AiApiKey);
            }
        }

        private async Task<ServiceResult<string>> GenerateOpenAiCompatibleAsync(string article, string prompt, string baseUrl, string model, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return ServiceResult<string>.Fail("Chave API não configurada para gerar resumo canônico.");

            string endpoint = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (!endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
                endpoint.IndexOf("/openai/v1", StringComparison.OrdinalIgnoreCase) < 0)
                endpoint += "/v1";
            endpoint += "/chat/completions";

            var payload = new
            {
                model = model.Trim(),
                messages = new[]
                {
                    new { role = "system", content = prompt },
                    new { role = "user", content = article }
                },
                temperature = 0,
                max_tokens = 80
            };

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    string key = apiKey.Trim();
                    if (key.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) key = key.Substring(7).Trim();
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
                    var response = await _httpClient.SendAsync(request);
                    string body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                        return ServiceResult<string>.Fail($"Resumo canônico: HTTP {(int)response.StatusCode}.");

                    string content = JObject.Parse(body)["choices"]?[0]?["message"]?["content"]?.ToString();
                    return string.IsNullOrWhiteSpace(content)
                        ? ServiceResult<string>.Fail("Resumo canônico vazio.")
                        : ServiceResult<string>.Ok(content.Trim());
                }
            }
            catch (Exception ex)
            {
                return ServiceResult<string>.Fail($"Falha ao gerar resumo canônico: {ex.Message}");
            }
        }

        private async Task<ServiceResult<string>> GenerateGeminiAsync(string article, string prompt, AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
                return ServiceResult<string>.Fail("Chave API do Gemini não configurada.");

            string model = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-2.0-flash" : config.GeminiModel;
            string endpoint = $"https://generativelanguage.googleapis.com/v1/models/{model}:generateContent?key={config.GeminiApiKey}";
            var payload = new
            {
                systemInstruction = new { parts = new[] { new { text = prompt } } },
                contents = new[] { new { parts = new[] { new { text = article } } } },
                generationConfig = new { temperature = 0, maxOutputTokens = 80 }
            };

            try
            {
                var response = await _httpClient.PostAsync(endpoint,
                    new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json"));
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return ServiceResult<string>.Fail($"Resumo canônico Gemini: HTTP {(int)response.StatusCode}.");

                string content = JObject.Parse(body)["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                return string.IsNullOrWhiteSpace(content)
                    ? ServiceResult<string>.Fail("Resumo canônico vazio.")
                    : ServiceResult<string>.Ok(content.Trim());
            }
            catch (Exception ex)
            {
                return ServiceResult<string>.Fail($"Falha ao gerar resumo canônico: {ex.Message}");
            }
        }

        private static string BuildPrompt(int wordCount)
        {
            return $"Gere uma chave canônica desta notícia em exatamente {wordCount} palavras. " +
                   "Escreva sempre em português, independentemente do idioma original. " +
                   "Represente somente o fato central. Use termos específicos, preserve nomes, números e conceitos técnicos. " +
                   "Evite artigos, preposições, adjetivos promocionais e palavras genéricas. " +
                   "Use formas linguísticas estáveis. Não use pontuação. Retorne somente as " + wordCount + " palavras.";
        }
    }
}
