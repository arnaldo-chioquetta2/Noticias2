// Services/GeminiService.cs
using NewsImpactRanker.WinForms.Storage;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

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
        // Alteração: Remoção do campo response_mime_type para compatibilidade total com v1 e v1beta.

        var config = StorageManager.LoadConfig();

        if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
            return (false, null, "Chave API do Gemini não configurada.");

        string promptSystem = File.ReadAllText(config.PromptFilePath);

        // Tentaremos usar a v1 (estável) para evitar erros de versão beta
        string url = $"https://generativelanguage.googleapis.com/v1/models/gemini-2.5-flash:generateContent?key={config.GeminiApiKey}";

        // Payload simplificado e robusto
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
                maxOutputTokens = 2048
                // Removido response_mime_type para evitar o erro 400 "Unknown name"
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

            // Como removemos o 'MimeType', a IA pode ocasionalmente colocar ```json ... ```
            // Vamos limpar isso antes de converter
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