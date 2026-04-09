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

        // METHOD v4: ClassifyNewsAsync (GroqService)
        public async Task<ServiceResult<TopicScoresResponse>> ClassifyNewsAsync(string text)
        {
            LogService.Info("METHOD v4: ClassifyNewsAsync (Groq)");

            // 1. Validações Iniciais
            if (string.IsNullOrWhiteSpace(_config.AiApiKey))
                return ServiceResult<TopicScoresResponse>.Fail("API Key da Groq não configurada.");

            if (string.IsNullOrWhiteSpace(_config.PromptFilePath) || !File.Exists(_config.PromptFilePath))
                return ServiceResult<TopicScoresResponse>.Fail("Arquivo de prompt não encontrado.");

            // 2. Preparação do Texto
            text = (text ?? "").Trim();

            // Truncamento para segurança de tokens (ajustado para 2000 conforme conversamos no scraping)
            if (text.Length > 2000)
                text = text.Substring(0, 2000);

            LogService.Info($"🧾 Texto p/ IA - len={text.Length}");

            // 3. Montagem do Payload
            string systemPrompt = File.ReadAllText(_config.PromptFilePath);
            string url = "https://api.groq.com/openai/v1/chat/completions";

            var payload = new
            {
                // Ajustado para usar SelectedModel conforme o novo AppConfig
                model = (string.IsNullOrWhiteSpace(_config.SelectedModel) || _config.SelectedModel.Contains("gemini"))
            ? "llama-3.1-8b-instant"
            : _config.SelectedModel,

                messages = new[]
                {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = "Analyze the following news article and return ONLY the JSON result.\n\n" + text }
        },

                temperature = 0.1, // Reduzido para ser mais determinístico
                max_tokens = 1500,
                response_format = new { type = "json_object" } // Groq suporta modo JSON
            };

            // 4. Envio da Requisição
            // Nota: SendRequestForTopicsAsync deve ser o método interno que faz o PostAsync
            var apiResponseResult = await SendRequestForTopicsAsync(url, JsonConvert.SerializeObject(payload));

            if (!apiResponseResult.Success)
                return ServiceResult<TopicScoresResponse>.Fail(apiResponseResult.ErrorMessage);

            // 5. Tratamento da Resposta
            string content = (apiResponseResult.Data ?? "").Trim();
            if (string.IsNullOrWhiteSpace(content))
                return ServiceResult<TopicScoresResponse>.Fail("IA retornou conteúdo vazio.");

            // Limpeza de possíveis tags de Markdown que a IA pode inserir
            content = content.Replace("```json", "").Replace("```", "").Trim();

            TopicScoresResponse parsed = null;

            try
            {
                // Tentativa de conversão direta
                parsed = JsonConvert.DeserializeObject<TopicScoresResponse>(content);
            }
            catch (Exception ex)
            {
                LogService.Warn($"Falha na desserialização direta: {ex.Message}. Tentando extração manual...");

                // Backup: Extração manual de JSON caso a IA envie texto extra
                int start = content.IndexOf("{");
                int end = content.LastIndexOf("}");

                if (start >= 0 && end > start)
                {
                    try
                    {
                        string jsonOnly = content.Substring(start, end - start + 1);
                        parsed = JsonConvert.DeserializeObject<TopicScoresResponse>(jsonOnly);
                    }
                    catch
                    {
                        return ServiceResult<TopicScoresResponse>.Fail($"JSON Corrompido pelo Groq: {ex.Message}. Texto: {content.Substring(0, Math.Min(content.Length, 150))}...");
                    }
                }
            }

            // 6. Finalização e Normalização
            if (parsed == null)
                return ServiceResult<TopicScoresResponse>.Fail("Falha ao processar resposta da IA.");

            if (parsed.Scores == null)
                parsed.Scores = new Dictionary<string, int>();

            // Garante que todos os 26 tópicos existam no dicionário
            EnsureAllTopics(parsed.Scores);

            // Normaliza os scores para o intervalo 0-100
            foreach (var key in parsed.Scores.Keys.ToList())
            {
                parsed.Scores[key] = Math.Max(0, Math.Min(100, parsed.Scores[key]));
            }

            return ServiceResult<TopicScoresResponse>.Ok(parsed);
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

        private async Task<ServiceResult<string>> SendRequestForTopicsAsync(string url, string jsonPayload)
        {
            // 👉 A MÁGICA PARA O GROQ ACONTECE AQUI:
            // Substituímos o valor fixo pelo configurado na tela de Settings.
            // Como o 'Replace' de string troca TODAS as ocorrências, se aparecer 2 vezes, mudará as 2.
            jsonPayload = jsonPayload.Replace("10 words", $"{_config.SummaryWordCount} words");

            try
            {
                // Agora o StringContent já vai com o JSON "turbinado" com a contagem certa
                using (var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json"))
                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = content;
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AiApiKey);

                    var response = await _httpClient.SendAsync(request);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        // ... (mantenha sua lógica de Rate Limit 429 aqui igual) ...
                        if ((int)response.StatusCode == 429)
                        {
                            // [Seu código de captura de tempo de espera...]
                        }

                        return ServiceResult<string>.Fail($"Erro Groq API: {response.StatusCode} - {responseBody}");
                    }

                    var result = JObject.Parse(responseBody);
                    string textResult = result["choices"]?[0]?["message"]?["content"]?.ToString();

                    if (string.IsNullOrWhiteSpace(textResult))
                        return ServiceResult<string>.Fail("Groq retornou conteúdo vazio.");

                    // Limpeza de blocos de código se a IA os retornar
                    int start = textResult.IndexOf('{');
                    int end = textResult.LastIndexOf('}');

                    if (start >= 0 && end > start)
                        textResult = textResult.Substring(start, end - start + 1);

                    return ServiceResult<string>.Ok(textResult);
                }
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<string>.Fail("Tempo limite de espera excedido. Tente novamente mais tarde.");
            }
        }

    }
}