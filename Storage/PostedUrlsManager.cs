using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Services;

namespace NewsImpactRanker.WinForms.Storage
{
    public static class PostedUrlsManager
    {
        private static readonly object FileLock = new object();

        public static string GetFilePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NewsRanking_PostedUrls_v1.json");
        }

        public static IReadOnlyList<PostedUrlItem> Load()
        {
            lock (FileLock)
            {
                string path = GetFilePath();
                if (!File.Exists(path))
                {
                    LogService.Info("[POSTED_URLS] Arquivo ainda não criado; lista vazia");
                    return new List<PostedUrlItem>();
                }

                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var items = JsonConvert.DeserializeObject<List<PostedUrlItem>>(json) ?? new List<PostedUrlItem>();
                    LogService.Info($"[POSTED_URLS] Arquivo: {path}");
                    LogService.Info($"[POSTED_URLS] Registros carregados: {items.Count}");
                    return items;
                }
                catch (Exception ex)
                {
                    LogService.Error($"[POSTED_URLS] ERRO ao carregar: {ex.GetType().Name}: {ex.Message}", ex);
                    return new List<PostedUrlItem>();
                }
            }
        }

        public static bool Contains(string url)
        {
            string normalized = NormalizeUrl(url);
            return Load().Any(x => string.Equals(x.NormalizedUrl ?? NormalizeUrl(x.Url), normalized, StringComparison.OrdinalIgnoreCase));
        }

        public static bool Add(string url, string summary, string provider, out string error)
        {
            error = null;
            string normalized = NormalizeUrl(url);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                error = "URL inválida.";
                return false;
            }

            lock (FileLock)
            {
                string path = GetFilePath();
                try
                {
                    var items = File.Exists(path)
                        ? JsonConvert.DeserializeObject<List<PostedUrlItem>>(File.ReadAllText(path, Encoding.UTF8)) ?? new List<PostedUrlItem>()
                        : new List<PostedUrlItem>();

                    if (items.Any(x => string.Equals(x.NormalizedUrl ?? NormalizeUrl(x.Url), normalized, StringComparison.OrdinalIgnoreCase)))
                    {
                        error = "Esta URL já estava marcada como postada.";
                        LogService.Info($"[POSTED_URLS] URL já registrada: {url}");
                        return false;
                    }

                    items.Add(new PostedUrlItem
                    {
                        Url = url.Trim(),
                        NormalizedUrl = normalized,
                        MarkedAt = DateTime.Now,
                        Summary = summary ?? string.Empty,
                        Provider = provider ?? string.Empty,
                        ApplicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString()
                    });

                    string directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                    string temp = path + ".tmp";
                    string json = JsonConvert.SerializeObject(items, Formatting.Indented);
                    File.WriteAllText(temp, json, new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);

                    LogService.Info($"[POSTED_URLS] URL registrada com sucesso: {url}");
                    LogService.Info($"[POSTED_URLS] Marcada em: {items[items.Count - 1].MarkedAt:O}");
                    LogService.Info($"[POSTED_URLS] Total de registros: {items.Count}");
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    LogService.Error($"[POSTED_URLS] ERRO ao registrar: {ex.GetType().Name}: {ex.Message}", ex);
                    return false;
                }
            }
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            string value = url.Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri)) return value;
            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty,
                Scheme = uri.Scheme.ToLowerInvariant(),
                Host = uri.Host.ToLowerInvariant()
            };
            string normalized = builder.Uri.AbsoluteUri;
            if (normalized.EndsWith("/") && builder.Path != "/") normalized = normalized.TrimEnd('/');
            return normalized;
        }
    }
}