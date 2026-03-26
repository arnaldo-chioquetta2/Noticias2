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
        // METHOD v9: ClassifyNewsAsync (Gemini) - Blindado contra JSON corrompido
        var config = StorageManager.LoadConfig();

        if (string.IsNullOrWhiteSpace(config.GeminiApiKey))
            return (false, null, "Chave API do Gemini não configurada.");

        try
        {
            // 1. Lê e ajusta o prompt dinamicamente
            string promptSystem = File.ReadAllText(config.PromptFilePath);
            promptSystem = promptSystem.Replace("10 words", $"{config.SummaryWordCount} words");
            promptSystem = promptSystem.Replace("10 palavras", $"{config.SummaryWordCount} palavras");

            string url = $"https://generativelanguage.googleapis.com/v1/models/gemini-2.0-flash:generateContent?key={config.GeminiApiKey}";

            // 2. Monta o Payload com limite de 2048 tokens (fôlego de sobra)
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
                    temperature = 0.1, // Mantém a resposta técnica e menos criativa
                    maxOutputTokens = 2048
                }
            };

            string jsonPayload = JsonConvert.SerializeObject(requestBody);
            var response = await _httpClient.PostAsync(url, new StringContent(jsonPayload, Encoding.UTF8, "application/json"));
            string jsonResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return (false, null, $"Erro HTTP Gemini: {response.StatusCode} - {jsonResponse}");
            }

            dynamic resp = JsonConvert.DeserializeObject(jsonResponse);
            if (resp.candidates == null || resp.candidates.Count == 0)
                return (false, null, "IA não retornou candidatos de resposta.");

            // 3. EXTRAÇÃO E LIMPEZA DO TEXTO (Onde os erros acontecem)
            string rawText = resp.candidates[0].content.parts[0].text;

            // Limpa marcações markdown e espaços em branco nas pontas
            string cleanJson = rawText.Replace("```json", "").Replace("```", "").Trim();

            try
            {
                // Tenta converter o texto limpo em um objeto dinâmico
                var scores = JsonConvert.DeserializeObject<dynamic>(cleanJson);
                return (true, scores, null);
            }
            catch (JsonReaderException jex)
            {
                // Se o JSON estiver quebrado (o erro de 'Unterminated string' cai aqui)
                return (false, null, $"JSON Corrompido pela IA: {jex.Message}. Texto recebido: {cleanJson.Substring(0, Math.Min(cleanJson.Length, 100))}...");
            }
        }
        catch (Exception ex)
        {
            return (false, null, $"Exceção GeminiService (v9): {ex.Message}");
        }
    }

}