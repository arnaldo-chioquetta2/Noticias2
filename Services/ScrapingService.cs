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

            // 1. O "Crachá" de Googlebot
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)");

            // 2. O "Rastro": Dizemos que estamos vindo de uma pesquisa do Google
            // Isso ajuda muito a evitar bloqueios em portais de notícias
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.google.com/");

            // 3. Idiomas e Aceitação de Conteúdo
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9,pt-BR;q=0.8,pt;q=0.7");
            _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");

            // 4. Cabeçalhos de "Simulação de Humano" (Sec-Fetch)
            // Mesmo como robô, esses headers ajudam a passar por firewalls modernos
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
            _httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
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

        private async Task<string> GetHtmlWithRetry(string url)
        {
            int maxRetries = 3;
            var random = new Random();
            bool tentouCache = false;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    // 🔄 AJUSTE NO DISFARCE: 
                    // Se o objetivo é ser o Googlebot, não podemos rotacionar para Chrome/Firefox.
                    // Vamos manter o User-Agent fixo como Googlebot definido no ResetHeaders.
                    // Se quiser rotacionar, a lista '_userAgents' deve conter apenas variações de Googlebot.

                    // Opcional: Descomente se quiser voltar a rotacionar navegadores comuns:
                    // string currentAgent = _userAgents[random.Next(_userAgents.Count)];
                    // _httpClient.DefaultRequestHeaders.Remove("User-Agent");
                    // _httpClient.DefaultRequestHeaders.Add("User-Agent", currentAgent);

                    // ⏱️ POLITE SCRAPER
                    int delayMs = random.Next(3000, 7001);
                    LogService.Info($"⏳ Aguardando {delayMs / 1000.0}s para evitar bloqueio...");
                    await Task.Delay(delayMs);

                    var response = await _httpClient.GetAsync(url);

                    if (!response.IsSuccessStatusCode)
                    {
                        LogService.Warn($"HTTP {(int)response.StatusCode} para {url}");

                        // 🚨 O PULO DO GATO: Se der 403, tentamos o Cache do Google uma única vez
                        if (response.StatusCode == HttpStatusCode.Forbidden && !tentouCache)
                        {
                            LogService.Warn($"⛔ Bloqueio 403 detectado. Tentando via Google Web Cache...");
                            url = $"https://webcache.googleusercontent.com/search?q=cache:{url}";
                            tentouCache = true;
                            i = -1; // Reseta as tentativas para a nova URL de cache
                            continue;
                        }

                        if (response.StatusCode == HttpStatusCode.NotFound)
                            return null;

                        throw new HttpRequestException($"Erro {response.StatusCode}");
                    }

                    // 🛠️ FIX DE ENCODING: Resolve o erro de "conjunto de caracteres inválido" (ex: Nature.com)
                    // Em vez de ReadAsStringAsync direto, lemos os bytes e forçamos UTF-8 ou o que o site mandar
                    byte[] contentBytes = await response.Content.ReadAsByteArrayAsync();
                    return Encoding.UTF8.GetString(contentBytes);
                }
                catch (Exception ex)
                {
                    if (i == maxRetries - 1)
                    {
                        LogService.Error($"Falha definitiva após {maxRetries} tentativas para {url}: {ex.Message}");
                        return null;
                    }

                    int waitTime = (i + 1) * 3000;
                    LogService.Warn($"Tentativa {i + 1}/{maxRetries} falhou. Aguardando {waitTime / 1000}s...");
                    await Task.Delay(waitTime);
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
