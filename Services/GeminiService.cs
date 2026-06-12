using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Services;
using NewsImpactRanker.WinForms.Storage;

namespace NewsImpactRanker.WinForms.Services
{
    public class GeminiService : IAiProvider
    {
        private readonly HttpClient _httpClient;

        public string Name => "GEMINI";

        public GeminiService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public async Task<(bool Success, dynamic Data, string ErrorMessage)> ClassifyNewsAsync(string text)
        {
            var result = await ClassifyAsync(text, null);
            return (result.Success, result.Data, result.ErrorMessage);
        }

        public async Task<ServiceResult<TopicScoresResponse>> ClassifyAsync(string text, string prompt)
        {
            LogService.Info("METHOD v1: ClassifyAsync");
            var config = StorageManager.LoadConfig();

            if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
                return ServiceResult<TopicScoresResponse>.Fail("Chave API do Gemini não configurada.");

            try
            {
                string promptSystem = prompt;
                if (string.IsNullOrWhiteSpace(promptSystem))
                {
                    if (string.IsNullOrWhiteSpace(config.PromptFilePath) || !File.Exists(config.PromptFilePath))
                        return ServiceResult<TopicScoresResponse>.Fail("Arquivo de prompt não encontrado.");

                    promptSystem = File.ReadAllText(config.PromptFilePath);
                }

                promptSystem = promptSystem.Replace("10 words", $"{config.SummaryWordCount} words");
                promptSystem = promptSystem.Replace("10 palavras", $"{config.SummaryWordCount} palavras");

                string model = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-2.0-flash" : config.GeminiModel;
                string url = $"https://generativelanguage.googleapis.com/v1/models/{model}:generateContent?key={config.GeminiApiKey}";

                var requestBody = new
                {
                    contents = new[] {
                        new {
                            parts = new[] {
                                new { text = $"{promptSystem}\n\nARTICLE:\n{text}" }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.1,
                        maxOutputTokens = 3072
                    }
                };

                string jsonPayload = JsonConvert.SerializeObject(requestBody);
                var response = await _httpClient.PostAsync(url, new StringContent(jsonPayload, Encoding.UTF8, "application/json"));
                string jsonResponse = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return ServiceResult<TopicScoresResponse>.Fail($"Erro HTTP Gemini: {(int)response.StatusCode} {response.StatusCode} - {jsonResponse}");
                }

                dynamic resp = JsonConvert.DeserializeObject(jsonResponse);
                if (resp.candidates == null || resp.candidates.Count == 0)
                    return ServiceResult<TopicScoresResponse>.Fail("Gemini não retornou candidatos de resposta.");

                string rawText = resp.candidates[0].content.parts[0].text;
                try
                {
                    int promptTokens = 0;
                    int completionTokens = 0;

                    if (resp.usageMetadata != null)
                    {
                        promptTokens = resp.usageMetadata.promptTokenCount != null ? (int)resp.usageMetadata.promptTokenCount : 0;
                        completionTokens = resp.usageMetadata.candidatesTokenCount != null ? (int)resp.usageMetadata.candidatesTokenCount : 0;
                        CostManager.AddGeminiUsage(promptTokens, completionTokens);
                        LogService.Info($"[GEMINI] usage prompt={promptTokens} completion={completionTokens}");
                        LogService.Info(CostManager.GetSingleLineCostSummary());
                    }
                }
                catch (Exception ex)
                {
                    LogService.Warn($"[GEMINI] Falha ao ler usageMetadata: {ex.Message}");
                }

                return AiResponseParser.ParseAndNormalize(rawText, Name);
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<TopicScoresResponse>.Fail("Timeout ao chamar Gemini.");
            }
            catch (Exception ex)
            {
                return ServiceResult<TopicScoresResponse>.Fail($"Exceção GeminiService: {ex.Message}");
            }
        }

    }
}
