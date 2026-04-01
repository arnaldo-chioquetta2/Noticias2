using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using NewsImpactRanker.WinForms.Models;

namespace NewsImpactRanker.WinForms.Services
{
    public class ScrapingService
    {
        private readonly HttpClient _httpClient;

        private readonly List<string> _userAgents = new List<string>
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:122.0) Gecko/20100101 Firefox/122.0",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36"
        };

        public ScrapingService()
        {
            // 🛡️ Configuração de Segurança de Protocolo (Resolve muitos erros 400/403)
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = true,
                AllowAutoRedirect = true,
                // Ignora erros de certificado SSL (comum em sites de notícias antigos)
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            // 🕵️‍♂️ Configuração Inicial dos Headers de Disfarce
            ResetHeaders();
        }

        private void ResetHeaders()
        {
            _httpClient.DefaultRequestHeaders.Clear();

            // 1. User-Agent inicial (será rotacionado no ScrapeAsync)
            _httpClient.DefaultRequestHeaders.Add("User-Agent", _userAgents[0]);

            // 2. Aceita idiomas (Essencial para sites como Phys.org)
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7");

            // 3. Simula um pedido de página HTML real
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");

            // 4. Cabeçalhos de segurança que navegadores modernos enviam
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "max-age=0");
        }


        public async Task<NewsItem> ScrapeAsync(string url)
        {
            LogService.Info($"Iniciando scraping: {url}");

            string html = await GetHtmlWithRetry(url);

            // ✅ PRIMEIRO: validar null ou vazio
            if (string.IsNullOrWhiteSpace(html))
            {
                LogService.Warn($"HTML vazio ou nulo para {url}");

                return new NewsItem
                {
                    Url = url,
                    Status = "Sem Conteúdo",
                    ProcessedAt = DateTime.Now
                };
            }

            // ✅ SÓ DEPOIS logar início do HTML
            LogService.Info("🌐 HTML início: " + html.Substring(0, Math.Min(120, html.Length)));

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Verificar bloqueio
            if (IsBlocked(html))
            {
                LogService.Warn($"Bloqueio detectado para {url}");

                return new NewsItem
                {
                    Url = url,
                    Status = "Bloqueado",
                    ProcessedAt = DateTime.Now
                };
            }

            string title = ExtractTitle(doc);
            string text = ExtractText(doc);

            if (string.IsNullOrWhiteSpace(text))
            {
                LogService.Warn($"Texto não extraído para {url}");

                return new NewsItem
                {
                    Url = url,
                    Status = "Sem Conteúdo",
                    ProcessedAt = DateTime.Now
                };
            }

            // Normalização
            text = NormalizeText(text);

            // ✅ OTIMIZAÇÃO DE CUSTO/TOKENS: Reduzido de 12000 para 2000 caracteres (aprox. 500 tokens)
            int maxChars = 2000;
            if (text.Length > maxChars)
            {
                text = text.Substring(0, maxChars);

                // Evita cortar o texto no meio de uma palavra para não confundir a IA
                int lastSpace = text.LastIndexOf(' ');
                if (lastSpace > 0)
                {
                    text = text.Substring(0, lastSpace) + "...";
                }
            }

            return new NewsItem
            {
                Url = url,
                Title = title,
                RawText = text,
                TextHash = ComputeHash(text),
                Status = "Sucesso",
                ProcessedAt = DateTime.Now
            };
        }

        /// <summary>
        /// METHOD v4: GetHtmlWithRetry
        /// Versão adaptada para ignorar erros de ContentType/Charset inválido (ex: Nature.com)
        /// </summary>
        private async Task<string> GetHtmlWithRetry(string url)
        {
            int maxRetries = 3;
            var random = new Random();

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    // 🔄 ROTAÇÃO: Escolhe um User-Agent aleatório da lista antes de cada requisição
                    string currentAgent = _userAgents[random.Next(_userAgents.Count)];
                    _httpClient.DefaultRequestHeaders.Remove("User-Agent");
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", currentAgent);

                    // ⏱️ POLITE SCRAPER: Atraso aleatório entre 3 e 7 segundos para evitar o Erro 429
                    int delayMs = random.Next(3000, 7001);
                    LogService.Info($"⏳ Aguardando {delayMs / 1000.0}s para evitar bloqueio...");
                    await Task.Delay(delayMs);

                    // Faz a requisição de fato
                    var response = await _httpClient.GetAsync(url);

                    // Trata erros HTTP (como 404, 403, 429)
                    if (!response.IsSuccessStatusCode)
                    {
                        LogService.Warn($"HTTP {(int)response.StatusCode} para {url}");

                        // Se for 404 (Not Found) ou 403 (Forbidden pesado), não adianta tentar de novo
                        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden)
                            return null;

                        // Se for 429 ou 500, a gente joga um erro pra forçar o Retry no bloco Catch
                        throw new HttpRequestException($"Erro {response.StatusCode}");
                    }

                    return await response.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    if (i == maxRetries - 1)
                    {
                        LogService.Error($"Falha definitiva após {maxRetries} tentativas para {url}", ex);
                        return null;
                    }

                    // Espera ainda mais tempo no Retry caso o site esteja engasgando
                    LogService.Warn($"Tentativa {i + 1}/{maxRetries} falhou para {url}: {ex.Message}. Aguardando {(i + 1) * 2}s...");
                    await Task.Delay((i + 1) * 2000);
                }
            }
            return null;
        }

        private bool IsBlocked(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return true;

            string lowerHtml = html.ToLowerInvariant();

            // 1) HTML muito curto (erro/redirecionamento/bloqueio)
            if (html.Length < 300)
            {
                LogService.Warn($"HTML suspeitamente curto: {html.Length} chars");
                return true;
            }

            // 2) Página parece ser erro HTTP
            if (Regex.IsMatch(lowerHtml, @"<title>\s*(404|403|500|503|error|not found)", RegexOptions.IgnoreCase) ||
                (lowerHtml.Contains("page not found") && lowerHtml.Contains("<h1>404")))
            {
                LogService.Warn("Página parece ser um erro HTTP");
                return true;
            }

            return false;
        }
        private string ExtractTitle(HtmlDocument doc)
        {
            var h1 = doc.DocumentNode.SelectSingleNode("//h1");
            if (h1 != null) return WebUtility.HtmlDecode(h1.InnerText.Trim());

            var title = doc.DocumentNode.SelectSingleNode("//title");
            if (title != null) return WebUtility.HtmlDecode(title.InnerText.Trim());

            return "Sem Título";
        }

        private string ExtractText(HtmlDocument doc)
        {
            // Remover elementos indesejados
            var nodesToRemove = doc.DocumentNode.SelectNodes("//script|//style|//nav|//header|//footer|//aside|//iframe|//form|//noscript");
            if (nodesToRemove != null)
            {
                foreach (var node in nodesToRemove) node.Remove();
            }

            // ✅ EXPANDIR seletores para estruturas modernas
            var articleSelectors = new[]
            {
        "//article",
        "//main",
        "//*[contains(@class, 'content') or contains(@class, 'post') or contains(@class, 'entry') or contains(@class, 'article') or contains(@class, 'body') or contains(@data-testid, 'article')]",
        "//div[@role='main']",
        "//section[contains(@class, 'article')]"
    };

            HtmlNode target = null;
            foreach (var selector in articleSelectors)
            {
                var node = doc.DocumentNode.SelectSingleNode(selector);
                if (node != null)
                {
                    target = node;
                    break;
                }
            }

            target = target ?? doc.DocumentNode; // Fallback para body

            // ✅ Extrair parágrafos de forma mais flexível
            var paragraphs = target.SelectNodes(".//p[not(ancestor::script) and not(ancestor::style) and normalize-space(text()) != '']");

            if (paragraphs == null || paragraphs.Count == 0)
            {
                // ✅ Último recurso: pegar todo o texto visível do target
                string allText = WebUtility.HtmlDecode(target.InnerText);
                if (!string.IsNullOrWhiteSpace(allText) && allText.Length > 100)
                {
                    return allText;
                }
                return null;
            }

            var sb = new StringBuilder();
            foreach (var p in paragraphs)
            {
                string pText = WebUtility.HtmlDecode(p.InnerText.Trim());
                if (pText.Length > 30) // Reduzido de 20 para 30 para filtrar melhor
                    sb.AppendLine(pText);
            }

            return sb.ToString();
        }

        private string NormalizeText(string text)
        {
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        private string ComputeHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(bytes).Replace("-", "").ToLower();
            }
        }
    }
}
