using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using System.Net.Http;
using System.Threading.Tasks;
using NewsImpactRanker.WinForms.Storage;

public class GeminiService
{
    private readonly HttpClient _httpClient;

    public GeminiService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<(bool Success, dynamic Data, string ErrorMessage)> ClassifyNewsAsync(string text)
    {
        // METHOD v8: ClassifyNewsAsync (Gemini)
        var config = StorageManager.LoadConfig();

        if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
            return (false, null, "Chave API do Gemini não configurada.");

        // 1. Lê o prompt original do arquivo
        string promptSystem = File.ReadAllText(config.PromptFilePath);

        // 👉 2. A MÁGICA DA INJEÇÃO: Substitui o limite fixo pelo limite dinâmico da tela
        promptSystem = promptSystem.Replace("10 words", $"{config.SummaryWordCount} words");
        // Dica extra: Se você tiver escrito "10 palavras" em português no prompt, mude o replace para:
        // promptSystem = promptSystem.Replace("10 palavras", $"{config.SummaryWordCount} palavras");

        // Tentaremos usar a v1 (estável) para evitar erros de versão beta
        string url = $"https://generativelanguage.googleapis.com/v1/models/gemini-2.5-flash:generateContent?key={config.GeminiApiKey}";

        // Payload simplificado e robusto
        var requestBody = new
        {
            contents = new[] {
        new {
            parts = new[] {
                // Aqui o promptSystem já vai com o "9 words", "15 words", etc.
                new { text = $"{promptSystem}\n\nARTICLE:\n{text}" }
            }
        }
    },
            generationConfig = new
            {
                temperature = 0.1,
                maxOutputTokens = 2048
            }
        };

        try
        {
            string jsonPayload = JsonConvert.SerializeObject(requestBody);

            var response = await _httpClient.PostAsync(url,
                new StringContent(jsonPayload, Encoding.UTF8, "application/json"));

            string jsonResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return (false, null, $"Erro Gemini (v8): {jsonResponse}");
            }

            dynamic resp = JsonConvert.DeserializeObject(jsonResponse);

            if (resp.candidates == null || resp.candidates.Count == 0)
                return (false, null, "IA não retornou candidatos de resposta.");

            string rawText = resp.candidates[0].content.parts[0].text;

            // Limpa as marcações de código markdown do JSON
            string cleanJson = rawText.Replace("```json", "").Replace("```", "").Trim();

            var scores = JsonConvert.DeserializeObject<dynamic>(cleanJson);

            return (true, scores, null);
        }
        catch (Exception ex)
        {
            return (false, null, $"Exceção GeminiService (v8): {ex.Message}");
        }
    }

}