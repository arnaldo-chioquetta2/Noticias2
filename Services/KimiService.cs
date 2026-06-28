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
    public class KimiService : IAiProvider
    {
        private readonly HttpClient _httpClient;
        private AppConfig _config;

        public string Name => "KIMI";

        public KimiService()
        {
            LogService.Info("METHOD v1: KimiService");
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "NewsImpactRanker/2.1");
            _config = StorageManager.LoadConfig();
        }

        public async Task<ServiceResult<TopicScoresResponse>> ClassifyAsync(string text, string prompt)
        {
            LogService.Info("METHOD v2: KimiService.ClassifyAsync");
            _config = StorageManager.LoadConfig();

            if (string.IsNullOrWhiteSpace(_config.KimiApiKey))
                return ServiceResult<TopicScoresResponse>.Fail("API Key da Kimi nao configurada.");

            string model = string.IsNullOrWhiteSpace(_config.KimiModel) ? "kimi-k2" : _config.KimiModel.Trim();
            string url = BuildChatCompletionsUrl(_config.KimiBaseUrl);
            string preparedText = PrepareText(text);
            string preparedPrompt = ApplySummaryWordCount(prompt, _config.SummaryWordCount);

            LogService.Info($"[KIMI] model={model}");
            LogService.Info($"[KIMI] url={url}");
            LogService.Info($"[KIMI] text_len={preparedText.Length}");

            var firstAttempt = await SendRequestAsync(url, model, preparedPrompt, preparedText, includeResponseFormat: true, reinforcePrompt: false);
            if (firstAttempt.Success)
                return firstAttempt;

            if (LooksLikeStructuredOutputRejection(firstAttempt.ErrorMessage))
            {
                LogService.Warn("[KIMI] response_format rejeitado; reenviando sem response_format");
                var secondAttempt = await SendRequestAsync(url, model, preparedPrompt, preparedText, includeResponseFormat: false, reinforcePrompt: true);
                return secondAttempt;
            }

            return firstAttempt;
        }

        private async Task<ServiceResult<TopicScoresResponse>> SendRequestAsync(string url, string model, string prompt, string text, bool includeResponseFormat, bool reinforcePrompt)
        {
            try
            {
                LogService.Info(includeResponseFormat ? "[KIMI] Tentativa 1: response_format=json_object" : "[KIMI] Tentativa 2: sem response_format");

                string systemPrompt = prompt ?? string.Empty;
                if (reinforcePrompt)
                {
                    systemPrompt = (systemPrompt + "\n\nReturn ONLY valid JSON. Do not include markdown. Do not include explanations. Do not wrap the response in ```json.").Trim();
                }

                var payload = new
                {
                    model = model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = text }
                    },
                    temperature = 0.2,
                    max_tokens = 1200,
                    enable_search = _config.KimiEnableSearch,
                    enable_thinking = _config.KimiEnableThinking
                };

                var body = JsonConvert.SerializeObject(payload);
                if (includeResponseFormat)
                {
                    var withResponseFormat = JObject.Parse(body);
                    withResponseFormat["response_format"] = JObject.FromObject(new { type = "json_object" });
                    body = withResponseFormat.ToString(Formatting.None);
                }

                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    var apiKey = (_config.KimiApiKey ?? string.Empty).Trim();
                    if (apiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        apiKey = apiKey.Substring("Bearer ".Length).Trim();

                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                    var response = await _httpClient.SendAsync(request);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        var message = $"Erro Kimi API: {(int)response.StatusCode} {response.StatusCode} - {responseBody}";
                        LogService.Error($"[KIMI] Erro HTTP: {(int)response.StatusCode} {response.StatusCode} - {responseBody}");
                        return ServiceResult<TopicScoresResponse>.Fail(message);
                    }

                    LogService.Info("[KIMI] Resposta bruta recebida com sucesso.");
                    LogService.Info($"[KIMI] response_body={TrimForLog(responseBody, 24000)}");

                    var root = JObject.Parse(responseBody);
                    var contentToken = root["choices"]?[0]?["message"]?["content"];
                    string content = ExtractContentString(contentToken);
                    LogService.Info($"[KIMI] content={TrimForLog(content, 24000)}");

                    var parseResult = ParseResponse(content);
                    if (parseResult.Success)
                    {
                        LogService.Info("[KIMI] JSON validado com sucesso");
                        return parseResult;
                    }

                    LogService.Warn("[KIMI] JSON inv\u00e1lido detectado; iniciando reparo autom\u00e1tico");
                    LogService.Warn($"[KIMI] Erro original: {parseResult.ErrorMessage}");

                    try
                    {
                        string repairedContent = await RepairJsonAsync(content, parseResult.ErrorMessage);
                        var repairedResult = ParseResponse(repairedContent);
                        if (repairedResult.Success)
                        {
                            LogService.Info("[KIMI] JSON reparado validado com sucesso");
                            return repairedResult;
                        }

                        LogService.Error($"[KIMI] Falha tamb\u00e9m no reparo JSON: {repairedResult.ErrorMessage}");
                        return ServiceResult<TopicScoresResponse>.Fail(
                            $"JSON original inv\u00e1lido: {parseResult.ErrorMessage}. Reparo inv\u00e1lido: {repairedResult.ErrorMessage}");
                    }
                    catch (Exception repairException)
                    {
                        LogService.Error($"[KIMI] Falha tamb\u00e9m no reparo JSON: {repairException.Message}");
                        return ServiceResult<TopicScoresResponse>.Fail(
                            $"JSON original inv\u00e1lido: {parseResult.ErrorMessage}. Falha no reparo: {repairException.Message}");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                return ServiceResult<TopicScoresResponse>.Fail("Timeout ao chamar Kimi.");
            }
            catch (Exception ex)
            {
                return ServiceResult<TopicScoresResponse>.Fail($"Excecao KimiService: {ex.Message}");
            }
        }

        private static bool LooksLikeStructuredOutputRejection(string errorMessage)
        {
            var msg = (errorMessage ?? string.Empty).ToLowerInvariant();
            return msg.Contains("model incompatible") || msg.Contains("structured output") || msg.Contains("json_schema") || msg.Contains("response_format") || msg.Contains("unsupported");
        }

        private static ServiceResult<TopicScoresResponse> ParseResponse(string content)
        {
            try
            {
                string json = ExtractJson(content);
                return AiResponseParser.ParseAndNormalize(json, "Kimi");
            }
            catch (Exception ex)
            {
                return ServiceResult<TopicScoresResponse>.Fail($"JSON inv\u00e1lido da Kimi: {ex.Message}");
            }
        }

        private static string BuildRepairPrompt()
        {
            var template = new StringBuilder();
            template.AppendLine("{");
            template.AppendLine("  \"summary\": \"texto\",");
            template.AppendLine("  \"scores\": {");
            for (int i = 0; i < TopicCatalog.Codes.Length; i++)
            {
                string suffix = i < TopicCatalog.Codes.Length - 1 ? "," : string.Empty;
                template.AppendLine($"    \"{TopicCatalog.Codes[i]}\": 0{suffix}");
            }
            template.AppendLine("  }");
            template.AppendLine("}");

            return "Voc\u00ea \u00e9 um reparador de JSON. Corrija o conte\u00fado recebido para JSON v\u00e1lido.\n" +
                   "Retorne somente JSON puro.\nN\u00e3o use markdown.\nN\u00e3o explique.\n" +
                   "Preserve exatamente a estrutura:\n" + template +
                   "\nRegras:\n- Todos os scores devem ser inteiros de 0 a 100.\n" +
                   "- Se um campo estiver ausente ou imposs\u00edvel de recuperar, use 0.\n" +
                   "- Se houver texto quebrado como NT0SPSP, inferir os campos corretos como NT e SP.\n" +
                   "- N\u00e3o inventar categorias fora da lista.\n- N\u00e3o remover summary.";
        }

        private async Task<string> RepairJsonAsync(string invalidContent, string errorMessage)
        {
            LogService.Info("[KIMI] Tentativa de reparo JSON");

            string url = BuildChatCompletionsUrl(_config.KimiBaseUrl);
            string model = string.IsNullOrWhiteSpace(_config.KimiModel) ? "kimi-k2" : _config.KimiModel.Trim();
            string userPrompt = $"Erro de parse:\n{errorMessage}\n\nJSON inv\u00e1lido:\n{invalidContent}\n\nRetorne apenas o JSON corrigido.";
            var payload = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = BuildRepairPrompt() },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0,
                max_tokens = 1200,
                enable_search = false,
                enable_thinking = false
            };

            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                string apiKey = (_config.KimiApiKey ?? string.Empty).Trim();
                if (apiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    apiKey = apiKey.Substring("Bearer ".Length).Trim();

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        $"Erro HTTP no reparo Kimi: {(int)response.StatusCode} {response.StatusCode} - {TrimForLog(responseBody, 24000)}");
                }

                var root = JObject.Parse(responseBody);
                string repairedContent = ExtractContentString(root["choices"]?[0]?["message"]?["content"]);
                if (string.IsNullOrWhiteSpace(repairedContent))
                    throw new Exception("Resposta vazia no reparo JSON da Kimi.");

                LogService.Info("[KIMI] JSON reparado recebido");
                LogService.Info($"[KIMI] repair_content={TrimForLog(repairedContent, 24000)}");
                return repairedContent;
            }
        }

        private static string BuildChatCompletionsUrl(string baseUrl)
        {
            var url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (!url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                url += "/v1";
            return url + "/chat/completions";
        }

        private static string ExtractContentString(JToken contentToken)
        {
            if (contentToken == null || contentToken.Type == JTokenType.Null)
                return string.Empty;

            if (contentToken.Type == JTokenType.String)
                return contentToken.ToString();

            if (contentToken.Type == JTokenType.Array)
            {
                var sb = new StringBuilder();
                foreach (var part in contentToken)
                {
                    var type = part?["type"]?.ToString();
                    if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                    {
                        var text = part?["text"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                            sb.Append(text);
                    }
                    else if (!string.IsNullOrWhiteSpace(part?["text"]?.ToString()))
                    {
                        sb.Append(part?["text"]?.ToString());
                    }
                }
                return sb.ToString();
            }

            return contentToken.ToString();
        }

        private static string ExtractJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new Exception("Resposta vazia da Kimi.");

            var text = content.Trim();
            text = text.Replace("```json", string.Empty)
                       .Replace("```", string.Empty)
                       .Trim();

            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');

            if (start < 0 || end < start)
                throw new Exception("JSON nÃƒÂ£o encontrado na resposta da Kimi.");

            return text.Substring(start, end - start + 1);
        }

        private static string PrepareText(string text)
        {
            text = (text ?? string.Empty).Trim();
            return text.Length > 2000 ? text.Substring(0, 2000) : text;
        }

        private static string ApplySummaryWordCount(string prompt, int summaryWordCount)
        {
            int count = summaryWordCount > 0 ? summaryWordCount : 10;
            return (prompt ?? string.Empty)
                .Replace("10 words", $"{count} words")
                .Replace("10 palavras", $"{count} palavras");
        }

        private static void EnsureAllTopics(System.Collections.Generic.Dictionary<string, int> scores)
        {
            foreach (var code in TopicCatalog.Codes)
            {
                if (!scores.ContainsKey(code))
                    scores[code] = 0;
            }
        }

        private static string TrimForLog(string text, int maxLen)
        {
            text = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= maxLen ? text : text.Substring(0, maxLen);
        }
    }
}
