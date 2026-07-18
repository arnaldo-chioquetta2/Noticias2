using System;
using System.IO;
using System.Data;
using System.Linq;
using System.Drawing;
using Newtonsoft.Json;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using NewsImpactRanker.WinForms.Utils;
using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Storage;
using NewsImpactRanker.WinForms.Services;

namespace NewsImpactRanker.WinForms.Forms
{
    public partial class MainForm : Form
    {
        private const string TopicUrlColumnName = "colTopicUrl";
        private const string CopyScrapColumnName = "colCopyScrap";
        private const string MainCaptionBase = "NewsImpactRanker - Classificador de Impacto de Notícias";

#if DEBUG
        private int _registroLimite = 10;
#else
        private int _registroLimite = 0;
#endif

        //private string _lastResultsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NewsRanking_LastResults.json"); 
        //private string _cachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NewsRanking_EvaluatedCache.json");
        private List<TopicResult> _currentTopicResults = new List<TopicResult>();
        private Dictionary<string, NewsScoresItem> _evaluatedCache = new Dictionary<string, NewsScoresItem>();
        private readonly object _evaluatedCacheFileLock = new object();

        private sealed class CacheUrlComparison
        {
            public string StoredUrl { get; set; }
            public string NormalizedStoredUrl { get; set; }
            public bool SameHost { get; set; }
            public bool SamePath { get; set; }
            public bool SameQuery { get; set; }
            public string DifferenceReason { get; set; }
        }

        private readonly ScrapingService _scrapingService;
        private readonly GroqService _groqService;
        private readonly DeepSeekService _deepSeekService;

        private readonly MistralService _mistralService;
        private readonly KimiService _kimiService;
        private readonly CanonicalSummaryService _canonicalSummaryService;
        private CancellationTokenSource _cts;        
        private bool _currentExecutionUsesFile = true;
        private readonly List<string> _failedDomains = new List<string>();
        private readonly object _failedDomainsLock = new object();
        private string _lastReportPath;
        private readonly object _newsScoresLock = new object();
        private readonly object _scoresLock = new object();
        private List<NewsScoresItem> _allNewsScores = new List<NewsScoresItem>();
        private static readonly object _tokenLock = new object();
        private int _tokensCurrentMinute = 0;
        private DateTime _minuteStartTime = DateTime.Now;
        private CancellationTokenSource _cancellationTokenSource;
        private const int TPM_LIMIT = 5500; // Margem de segurança de 500 tokens abaixo dos 6000
        private string _lastIaError = "Nenhum";
        private Stopwatch _executionTimer = new Stopwatch();
        private string _folderPath;
        private readonly Queue<long> _lastProcessingTimes = new Queue<long>();
        private readonly GeminiService _geminiService; // Adicione esta linha

        // Configuração do Filtro Anti-Duplicidade
        private readonly int _summaryWordCount = 10;
        private int _duplicateCount = 0;
        private int _resumosCanonicosGerados = 0;
        private int _duplicatasPorResumo = 0;
        private int _avaliacoesCompletasEvitadas = 0;
        private int _avaliacoesCompletasExecutadas = 0;

        // 👉 VARIÁVEIS DO FILTRO ANTI-DUPLICIDADE
        private List<SummaryCacheItem> _summaryCache = new List<SummaryCacheItem>();

        private sealed class SummaryDuplicateMatch
        {
            public string ExistingSummary { get; set; }
            public string NormalizedExistingSummary { get; set; }
            public string Reason { get; set; }
            public double Similarity { get; set; }
        }


        private int _processedCount = 0;
        private int _successCount = 0;
        private int _iaErrorCount = 0;
        private int _scrapErrorCount = 0;
        private int _cacheHitCount = 0;
        // Cronômetro para saber até que horas o Groq deve ficar "de castigo"
        private DateTime _groqCooldownUntil = DateTime.MinValue;

        private int _groqSuccessCount = 0;
        private int _geminiSuccessCount = 0;
        private int _deepSeekSuccessCount = 0;
        private int _mistralSuccessCount = 0;
        private bool _ipBlockWarningShown = false;

        private string _lastResultsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NewsRanking_LastResults_v2.json");
        private string _cachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NewsRanking_EvaluatedCache_v2.json");
        private string _summaryCachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NewsRanking_SummaryCache_v2.json");

        public MainForm()
        {
            InitializeComponent();
            _scrapingService = new ScrapingService();
            _deepSeekService = new DeepSeekService();
            _groqService = new GroqService();
            _geminiService = new GeminiService();

            _mistralService = new MistralService();
            _kimiService = new KimiService();
            _canonicalSummaryService = new CanonicalSummaryService();
            dgvResults.SortCompare += DgvResults_SortCompare;
            ApplyVersionToCaption();
            LogService.WriteApplicationHeader();
            LogApplicationIdentity();
            LoadLastResults();
            LoadEvaluatedCache();
            _summaryCache = SummaryCacheManager.LoadCache();

        }

        private void LoadEvaluatedCache()
        {
            try
            {
                LogService.Info($"[CACHE] Arquivo: {_cachePath}");
                if (File.Exists(_cachePath))
                {
                    string json = File.ReadAllText(_cachePath);
                    _evaluatedCache = JsonConvert.DeserializeObject<Dictionary<string, NewsScoresItem>>(json)
                                      ?? new Dictionary<string, NewsScoresItem>();

                    LogService.Info($"[CACHE] Entradas carregadas: {_evaluatedCache.Count}");
                    LogService.Info($"[CACHE] Data de modificação: {File.GetLastWriteTime(_cachePath):o}");
                    LogService.Info($"[CACHE] Tamanho: {new FileInfo(_cachePath).Length} bytes");
                }
                else
                {
                    _evaluatedCache = new Dictionary<string, NewsScoresItem>();
                    LogService.Info("[CACHE] Arquivo não encontrado; iniciando cache vazio");
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"[CACHE] Erro ao carregar cache: {ex.Message}", ex);
                _evaluatedCache = new Dictionary<string, NewsScoresItem>();
            }
        }

        private static string NormalizeCacheUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;

            string trimmed = url.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri)) return trimmed;

            var builder = new UriBuilder(uri)
            {
                Fragment = string.Empty,
                Scheme = uri.Scheme.ToLowerInvariant(),
                Host = uri.Host.ToLowerInvariant()
            };

            string normalized = builder.Uri.AbsoluteUri;
            if (normalized.EndsWith("/") && builder.Path != "/")
                normalized = normalized.TrimEnd('/');

            return normalized;
        }

        private void LogApplicationIdentity()
        {
            string executablePath = Application.ExecutablePath;
            var executableInfo = File.Exists(executablePath) ? new FileInfo(executablePath) : null;
            var assembly = Assembly.GetExecutingAssembly();

            LogService.Info($"[APP] Executável: {executablePath}");
            LogService.Info($"[APP] BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}");
            LogService.Info($"[APP] AssemblyLocation: {assembly.Location}");
            LogService.Info($"[APP] Versão: {assembly.GetName().Version}");
            if (executableInfo != null)
                LogService.Info($"[APP] Modificado em: {executableInfo.LastWriteTime:o}");
        }

        private static bool IsClearlyContaminatedUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true;

            string value = url.Trim();
            return value.Contains("<") || value.Contains(">") ||
                   value.IndexOf("target=\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("http://", StringComparison.OrdinalIgnoreCase) !=
                   value.LastIndexOf("http://", StringComparison.OrdinalIgnoreCase) ||
                   value.IndexOf("https://", StringComparison.OrdinalIgnoreCase) !=
                   value.LastIndexOf("https://", StringComparison.OrdinalIgnoreCase);
        }

        private IEnumerable<CacheUrlComparison> FindSimilarCacheUrls(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri requestedUri))
                return Enumerable.Empty<CacheUrlComparison>();

            var candidates = new List<CacheUrlComparison>();
            foreach (var pair in _evaluatedCache)
            {
                string storedUrl = pair.Value?.Url ?? pair.Key;
                if (!Uri.TryCreate(storedUrl, UriKind.Absolute, out Uri storedUri))
                    continue;

                bool sameHost = string.Equals(requestedUri.Host, storedUri.Host, StringComparison.OrdinalIgnoreCase);
                bool samePath = string.Equals(requestedUri.AbsolutePath, storedUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
                bool sameQuery = string.Equals(requestedUri.Query, storedUri.Query, StringComparison.Ordinal);
                if (!sameHost && !samePath) continue;

                string reason;
                if (sameHost && samePath && !sameQuery)
                    reason = "query string";
                else if (sameHost && samePath && !string.Equals(requestedUri.Scheme, storedUri.Scheme, StringComparison.OrdinalIgnoreCase))
                    reason = "esquema http/https";
                else if (sameHost && samePath && !string.Equals(requestedUri.Fragment, storedUri.Fragment, StringComparison.Ordinal))
                    reason = "fragmento";
                else if (samePath && !sameHost)
                    reason = "host/www/subdomínio";
                else
                    reason = "caminho diferente";

                candidates.Add(new CacheUrlComparison
                {
                    StoredUrl = storedUrl,
                    NormalizedStoredUrl = NormalizeCacheUrl(storedUrl),
                    SameHost = sameHost,
                    SamePath = samePath,
                    SameQuery = sameQuery,
                    DifferenceReason = reason
                });
            }

            return candidates.Take(5).ToList();
        }

        private bool TryGetEvaluatedCache(string url, out NewsScoresItem cachedItem, out bool legacy)
        {
            cachedItem = null;
            legacy = false;
            string normalizedUrl = NormalizeCacheUrl(url);

            LogService.Info($"[CACHE] Diagnóstico lookup: original={url}");
            LogService.Info($"[CACHE] Diagnóstico lookup: normalizada={normalizedUrl}");

            if (IsClearlyContaminatedUrl(url) || string.IsNullOrWhiteSpace(normalizedUrl))
            {
                LogService.Info($"[CACHE] URL inválida para lookup: {url}");
                return false;
            }

            foreach (var pair in _evaluatedCache)
            {
                string cachedUrl = NormalizeCacheUrl(pair.Key);
                string itemUrl = NormalizeCacheUrl(pair.Value?.Url);
                if (!string.Equals(cachedUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(itemUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!IsValidEvaluatedCacheItem(pair.Value))
                {
                    LogService.Info("[CACHE] MISS diagnóstico");
                    LogService.Info("[CACHE] Motivo: entrada encontrada, porém inválida ou incompleta");
                    return false;
                }

                cachedItem = pair.Value;
                string provider = (cachedItem.AiProvider ?? string.Empty).Trim();
                legacy = string.IsNullOrWhiteSpace(provider) ||
                         string.Equals(provider, "IA", StringComparison.OrdinalIgnoreCase) ||
                         !new[] { "DEEPSEEK", "GROQ", "GEMINI", "MISTRAL", "KIMI" }
                             .Contains(provider.ToUpperInvariant());
                return true;
            }

            var similar = FindSimilarCacheUrls(url).ToList();
            LogService.Info("[CACHE] MISS diagnóstico");
            LogService.Info($"[CACHE] URL original: {url}");
            LogService.Info($"[CACHE] URL normalizada para lookup: {normalizedUrl}");
            LogService.Info($"[CACHE] Total de entradas em memória: {_evaluatedCache.Count}");
            LogService.Info(similar.Count == 0
                ? "[CACHE] Motivo: nenhuma entrada com mesmo host/path"
                : $"[CACHE] Motivo: existe entrada semelhante ({similar.Count})");

            int candidateNumber = 1;
            foreach (var candidate in similar)
            {
                LogService.Info($"[CACHE] Candidata {candidateNumber}: {candidate.StoredUrl}");
                LogService.Info($"[CACHE] Candidata {candidateNumber} normalizada: {candidate.NormalizedStoredUrl}");
                LogService.Info($"[CACHE] Diferença: {candidate.DifferenceReason}");
                candidateNumber++;
            }
            return false;
        }

        private static bool IsValidEvaluatedCacheItem(NewsScoresItem item)
        {
            return item != null &&
                   !string.IsNullOrWhiteSpace(item.Url) &&
                   !string.IsNullOrWhiteSpace(item.Summary) &&
                   item.Scores != null &&
                   item.Scores.Count > 0;
        }

        private void UpsertEvaluatedCache(string url, NewsScoresItem item)
        {
            lock (_evaluatedCacheFileLock)
            {
                string normalizedUrl = NormalizeCacheUrl(url);
                string existingKey = _evaluatedCache.Keys.FirstOrDefault(key =>
                    string.Equals(NormalizeCacheUrl(key), normalizedUrl, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(existingKey) && !string.Equals(existingKey, normalizedUrl, StringComparison.Ordinal))
                    _evaluatedCache.Remove(existingKey);

                _evaluatedCache[normalizedUrl] = item;
                LogService.Info($"[CACHE] Entrada atualizada: {normalizedUrl}");
                LogService.Info($"[CACHE] Entradas em memória: {_evaluatedCache.Count}");
                SaveEvaluatedCache();
            }
        }

        private void DgvResults_SortCompare(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (e.Column.Name == "colImpact")
            {
                double v1 = double.Parse(e.CellValue1?.ToString() ?? "0");
                double v2 = double.Parse(e.CellValue2?.ToString() ?? "0");
                e.SortResult = v1.CompareTo(v2);
                e.Handled = true;
            }
        }

        private void btnConfig_Click(object sender, EventArgs e)
        {
            using (var configForm = new ConfigForm())
            {
                configForm.ShowDialog();
            }
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            // Carrega configuração atualizada
            var config = StorageManager.LoadConfig();
            string startupMissingKeyMessage = GetMissingProviderKeyMessage(config);
            if (startupMissingKeyMessage != null)
            {
                MessageBox.Show(startupMissingKeyMessage, "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnConfig_Click(null, null);
                return;
            }

            // Reseta logs e contadores globais
            LogService.ResetLog();
            CostManager.Reset();
            _successCount = 0;
            _iaErrorCount = 0;
            _scrapErrorCount = 0;
            _cacheHitCount = 0;
            _deepSeekSuccessCount = 0;
            _groqSuccessCount = 0;
            _geminiSuccessCount = 0;
            _mistralSuccessCount = 0;
            _resumosCanonicosGerados = 0;
            _duplicatasPorResumo = 0;
            _avaliacoesCompletasEvitadas = 0;
            _avaliacoesCompletasExecutadas = 0;
            //_falhasProcessamento.Clear();

            LogService.Info("=== Processamento iniciado ===");

            // 1. Validação Inteligente de API Key conforme o provedor selecionado
            bool keyConfigurada = true;

            if (!keyConfigurada)
            {
                string provedor = config.SelectedProvider.ToString();
                MessageBox.Show(
                    $"A chave API do {provedor} não foi configurada.",
                    "Aviso",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                btnConfig_Click(null, null); // Abre a tela de configuração
                return;
            }

            List<string> validUrls;
            bool usingFile = false;

            try
            {
                // 🔹 Carrega URLs da fonte (campo de texto ou arquivo)
                validUrls = LoadUrlsFromSource(config);

                // Verifica se o campo de texto estava vazio para definir se estamos usando arquivo
                var typedUrls = txtUrls.Lines
                    .Select(l => l.Trim())
                    .Where(l => UrlValidator.IsValid(l))
                    .Distinct()
                    .ToList();

                usingFile = !typedUrls.Any();

                // 2. NOVA LÓGICA DE LIMITE: 0 = Infinito | n = Limite de registros
                if (_registroLimite > 0)
                {
                    validUrls = validUrls.Take(_registroLimite).ToList();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar URLs: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (validUrls.Count == 0)
            {
                MessageBox.Show("Nenhuma URL válida encontrada para processar.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Configura o Dashboard e Logs de Início
            _currentExecutionUsesFile = usingFile;
            string infoLimite = _registroLimite > 0 ? $"(Limite de {_registroLimite} registros ativo)" : "(Modo lote completo)";
            LogService.Info($"URLs carregadas: {validUrls.Count} {infoLimite}");

            if (usingFile)
                LogService.Info($"Arquivo utilizado: {config.NewsFilePath}");
            else
                LogService.Info("Modo: URLs digitadas manualmente no campo de texto.");

            // Limpa estados de execuções anteriores
            _allNewsScores.Clear();
            lock (_failedDomainsLock) { _failedDomains.Clear(); }

            // Bloqueia a interface e limpa as grids
            ToggleUI(false);

            //dgvResults.Rows.Clear();
            //dgvTopicResults.Rows.Clear();
            // --- Troque isto: ---
            // dgvResults.Rows.Clear();
            // dgvTopicResults.Rows.Clear();

            // --- Por isto: ---
            if (dgvResults.DataSource != null) dgvResults.DataSource = null;
            else dgvResults.Rows.Clear(); // Se não estiver vinculada, limpa normal

            dgvTopicResults.DataSource = null; // Como usamos DataSource aqui, isso já limpa tudo
            dgvTopicResults.Columns.Clear();   // Opcional: Limpa as colunas para garantir o novo layout

            // Configura Barra de Progresso
            progressBar.Maximum = validUrls.Count;
            progressBar.Value = 0;
            lblProgress.Text = $"0/{validUrls.Count}";

            _cts = new CancellationTokenSource();

            try
            {
                // 🔹 INÍCIO DO PROCESSAMENTO
                // Agora o ProcessUrlsAsync cuidará internamente de alternar entre Gemini/Groq
                await ProcessUrlsAsync(validUrls);

                // 🔹 FINALIZAÇÃO E RELATÓRIOS
                LogService.Info($"Processamento concluído. Sucessos: {_allNewsScores.Count}");

                // Seleciona as melhores notícias por tópico (Ranking Final)
                var topicResults = SelectBestNewsPerTopic(9);

                // ---> INTEGRAÇÃO COM LAST RESULTS (JSON) <---
                // 1. Atualiza a variável de memória global para o controle de cliques
                MergePendingTopicResults(topicResults);

                // 3. Atualiza a grid de resultados por assunto
                DisplayTopicResults(_currentTopicResults);

                // 4. Salva o arquivo de texto com o ranking
                SaveFinalRankingToFile(topicResults);

                LogService.Info($"Total de tópicos preenchidos: {topicResults.Count}");
                UpdateCostLabel();
            }
            catch (OperationCanceledException)
            {
                LogService.Info("Operação interrompida pelo usuário.");
                MessageBox.Show("Processamento cancelado.", "Informação", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogService.Error("Erro fatal no loop de processamento", ex);

                if (ex.Message.Contains("Limite de uso atingido"))
                {
                    MessageBox.Show(ex.Message, "Limite da API", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    Application.Exit();
                }
                else
                {
                    MessageBox.Show($"Ocorreu um erro inesperado: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                SaveEvaluatedCache();
                // Libera a interface novamente
                ToggleUI(true);
                UpdateCostLabel();
            }
        }

        private async Task ProcessUrlsAsync(List<string> urls)
        {
            _executionTimer.Restart();
            _lastIaError = "Nenhum";
            _lastProcessingTimes.Clear();

            foreach (var url in urls)
            {
                // Verifica cancelamento
                if (_cts != null && _cts.IsCancellationRequested) break;

                Stopwatch itemTimer = Stopwatch.StartNew();

                try
                {
                    // O segredo do Fallback está aqui dentro
                    await ProcessSingleUrlAsync(url);
                }
                catch (Exception ex)
                {
                    LogService.Error($"Erro crítico ao processar {url}: {ex.Message}");
                    _lastIaError = ex.Message;
                }
                finally
                {
                    itemTimer.Stop();
                    lock (_lastProcessingTimes)
                    {
                        _lastProcessingTimes.Enqueue(itemTimer.ElapsedMilliseconds);
                        if (_lastProcessingTimes.Count > 10) _lastProcessingTimes.Dequeue();
                    }

                    UpdateProgress();
                    UpdateStatusLabel();
                }

                // Delay entre URLs para o Polite Scraping (ajustado para ser seguro)
                await Task.Delay(1000);
            }

            _executionTimer.Stop();
            UpdateInfoLabel(GetFormattedStatus("Processamento finalizado!"));
        }

        private async Task ProcessSingleUrlAsync(string url)
        {
            string normalizedUrl = NormalizeCacheUrl(url);
            LogService.Info($"[CACHE] Consultando: {normalizedUrl}");

            if (TryGetEvaluatedCache(url, out var cachedItem, out bool legacy))
            {
                _cacheHitCount++;
                _successCount++;

                LogService.Info("[CACHE] HIT");
                LogService.Info($"[CACHE] Consultada: {normalizedUrl}");
                LogService.Info($"[CACHE] Armazenada: {NormalizeCacheUrl(cachedItem.Url)}");
                LogService.Info(legacy
                    ? $"[CACHE] HIT legado: {normalizedUrl}"
                    : $"[CACHE] HIT: {normalizedUrl}");

                lock (_allNewsScores)
                {
                    _allNewsScores.Add(new NewsScoresItem
                    {
                        Url = normalizedUrl,
                        Title = cachedItem.Title,
                        Summary = cachedItem.Summary,
                        Scores = new Dictionary<string, int>(cachedItem.Scores),
                        AiProvider = cachedItem.AiProvider,
                        RawText = cachedItem.RawText,
                        SourceOrder = _successCount
                    });
                }

                LogService.Info("[CACHE] Resultado restaurado sem scraping e sem IA");
                return;
            }

            // 1. Scraping
            var newsItem = await _scrapingService.ScrapeAsync(url);

            if (newsItem == null || newsItem.Status != "Sucesso" || string.IsNullOrWhiteSpace(newsItem.RawText))
            {
                LogService.Warn($"Scraping falhou ou retornou vazio para: {url}");
                _scrapErrorCount++;
                return;
            }

            var config = StorageManager.LoadConfig();

            LogService.Info("[DEDUP] Gerando resumo canônico antes da avaliação");
            LogService.Info("[DEDUP] Idioma canônico: português");
            LogService.Info($"[DEDUP] Palavras configuradas: {config.SummaryWordCount}");
            var canonicalResult = await _canonicalSummaryService.GenerateAsync(newsItem.RawText, config);
            if (!canonicalResult.Success || string.IsNullOrWhiteSpace(canonicalResult.Data))
            {
                LogService.Error($"[DEDUP] Falha ao gerar resumo canônico: {canonicalResult.ErrorMessage}");
                _iaErrorCount++;
                return;
            }

            string canonicalSummary = canonicalResult.Data.Trim();
            string normalizedCanonicalSummary = NormalizeCanonicalSummary(canonicalSummary);
            int receivedWords = CountWords(normalizedCanonicalSummary);
            _resumosCanonicosGerados++;
            LogService.Info($"[DEDUP] Resumo recebido: {canonicalSummary}");
            LogService.Info($"[DEDUP] Resumo normalizado: {normalizedCanonicalSummary}");
            LogService.Info($"[DEDUP] Quantidade recebida: {receivedWords}");

            if (receivedWords != config.SummaryWordCount)
            {
                LogService.Error($"[DEDUP] Resumo canônico inválido: esperado {config.SummaryWordCount} palavras, recebido {receivedWords}");
                _iaErrorCount++;
                return;
            }

            if (IsDuplicateByCanonicalSummary(canonicalSummary, out var duplicateMatch))
            {
                _duplicateCount++;
                _duplicatasPorResumo++;
                _avaliacoesCompletasEvitadas++;
                LogService.Info("[DEDUP] DUPLICATA PRÉ-AVALIAÇÃO");
                LogService.Info($"[DEDUP] Resumo atual: {canonicalSummary}");
                LogService.Info($"[DEDUP] Resumo existente: {duplicateMatch.ExistingSummary}");
                LogService.Info($"[DEDUP] Similaridade: {duplicateMatch.Similarity:0.00} ({duplicateMatch.Reason})");
                LogService.Info("[DEDUP] Avaliação completa evitada");
                return;
            }

            LogService.Info("[DEDUP] Resumo não encontrado no histórico");

            string prompt = LoadPrompt(config);
            var provider = GetSelectedProviderService(config.SelectedProvider);
            LogService.Info("[AI] Iniciando avaliação completa");
            LogService.Info($"[{provider.Name}] Processando URL: {url}");
            var aiResult = await provider.ClassifyAsync(newsItem.RawText, prompt);
            UpdateCostLabel();

            // Verifica se o usuário cancelou durante o aviso de IP
            if (_cts != null && _cts.IsCancellationRequested) return;

            // 3. FINALIZAÇÃO
            if (aiResult.Success && aiResult.Data != null)
            {
                var resultScore = new NewsScoresItem
                {
                    Url = url,
                    Title = newsItem.Title,
                    Summary = aiResult.Data.Summary,
                    Scores = aiResult.Data.Scores,
                    AiProvider = provider.Name,
                    SourceOrder = _successCount + 1
                };

                lock (_allNewsScores)
                {
                    _allNewsScores.Add(resultScore);
                }
                _successCount++;
                _avaliacoesCompletasExecutadas++;
                IncrementProviderCounter(provider.Name);
                UpsertEvaluatedCache(normalizedUrl, resultScore);
                SaveCanonicalSummary(canonicalSummary);
            }
            else
            {
                if (string.Equals(provider.Name, "KIMI", StringComparison.OrdinalIgnoreCase))
                    LogService.Error($"[KIMI] Erro final: {aiResult.ErrorMessage}");

                LogService.Error($"❌ Falha definitiva: Nenhuma IA processou {url}");
                _iaErrorCount++;
            }
        }

        private string LoadPrompt(AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.PromptFilePath) || !File.Exists(config.PromptFilePath))
            {
                throw new FileNotFoundException("Arquivo de prompt não encontrado.", config.PromptFilePath);
            }

            return File.ReadAllText(config.PromptFilePath);
        }

        private bool IsDuplicateByCanonicalSummary(string generatedSummary, out SummaryDuplicateMatch match)
        {
            match = null;
            string normalized = NormalizeCanonicalSummary(generatedSummary);
            int expectedWords = StorageManager.LoadConfig().SummaryWordCount > 0
                ? StorageManager.LoadConfig().SummaryWordCount
                : 5;

            if (CountWords(normalized) != expectedWords)
            {
                LogService.Warn($"[DEDUP] Resumo inválido: esperado {expectedWords} palavras, recebido {CountWords(normalized)}");
                return false;
            }

            LogService.Info("[DEDUP] Comparando exclusivamente pelo resumo");
            foreach (var item in _summaryCache.Where(x => x.IsCanonical && !string.IsNullOrWhiteSpace(x.Summary)))
            {
                string existingNormalized = NormalizeCanonicalSummary(item.Summary);
                if (string.Equals(normalized, existingNormalized, StringComparison.Ordinal))
                {
                    match = new SummaryDuplicateMatch
                    {
                        ExistingSummary = item.Summary,
                        NormalizedExistingSummary = existingNormalized,
                        Reason = "igualdade exata do resumo normalizado",
                        Similarity = 1.0
                    };
                    return true;
                }

                double similarity = CalculateCanonicalTokenSimilarity(normalized, existingNormalized);
                if (similarity >= 0.80 && CountWords(normalized) == CountWords(existingNormalized))
                {
                    match = new SummaryDuplicateMatch
                    {
                        ExistingSummary = item.Summary,
                        NormalizedExistingSummary = existingNormalized,
                        Reason = "tokens canônicos semelhantes",
                        Similarity = similarity
                    };
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeCanonicalSummary(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) return string.Empty;

            var chars = summary.ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
                .ToArray();
            return string.Join(" ", new string(chars)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeCanonicalToken));
        }

        private static string NormalizeCanonicalToken(string token)
        {
            if (token.Length > 4 && token.EndsWith("s", StringComparison.Ordinal))
                return token.Substring(0, token.Length - 1);
            return token;
        }

        private static int CountWords(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? 0 : value.Split(' ').Length;
        }

        private static double CalculateCanonicalTokenSimilarity(string left, string right)
        {
            var leftTokens = new HashSet<string>(left.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            var rightTokens = new HashSet<string>(right.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (leftTokens.Count == 0 || rightTokens.Count == 0) return 0;

            int intersection = leftTokens.Intersect(rightTokens).Count();
            int union = leftTokens.Union(rightTokens).Count();
            return union == 0 ? 0 : (double)intersection / union;
        }

        private void SaveCanonicalSummary(string canonicalSummary)
        {
            if (string.IsNullOrWhiteSpace(canonicalSummary)) return;

            _summaryCache.Add(new SummaryCacheItem
            {
                Summary = canonicalSummary.Trim(),
                DateAdded = DateTime.Now,
                IsCanonical = true
            });
            SummaryCacheManager.SaveCache(_summaryCache);
        }

        private void IncrementProviderCounter(string providerName)
        {
            switch ((providerName ?? "").ToUpperInvariant())
            {
                case "DEEPSEEK":
                    _deepSeekSuccessCount++;
                    break;
                case "GROQ":
                    _groqSuccessCount++;
                    break;
                case "GEMINI":
                    _geminiSuccessCount++;
                    break;
                case "MISTRAL":
                    _mistralSuccessCount++;
                    break;
            }
        }

        private IAiProvider GetSelectedProviderService(AiProvider provider)
        {
            switch (provider)
            {
                case AiProvider.DeepSeek:
                    return _deepSeekService;
                case AiProvider.Groq:
                    return _groqService;
                case AiProvider.Gemini:
                    return _geminiService;
                case AiProvider.Mistral:
                    return _mistralService;
                case AiProvider.Kimi:
                    return _kimiService;
                default:
                    throw new InvalidOperationException("Provedor de IA invalido.");
            }
        }

        private string GetMissingProviderKeyMessage(AppConfig config)
        {
            switch (config.SelectedProvider)
            {
                case AiProvider.DeepSeek:
                    return string.IsNullOrWhiteSpace(config.DeepSeekApiKey) ? "A chave API do DeepSeek nao foi configurada." : null;
                case AiProvider.Groq:
                    return string.IsNullOrWhiteSpace(config.AiApiKey) ? "A chave API da Groq nao foi configurada." : null;
                case AiProvider.Gemini:
                    return string.IsNullOrWhiteSpace(config.GeminiApiKey) ? "A chave API do Gemini nao foi configurada." : null;
                case AiProvider.Mistral:
                    return string.IsNullOrWhiteSpace(config.MistralApiKey) ? "A chave API da Mistral nao foi configurada." : null;
                case AiProvider.Kimi:
                    return string.IsNullOrWhiteSpace(config.KimiApiKey) ? "A chave API da Kimi nao foi configurada." : null;
                default:
                    return "Provedor de IA invalido.";
            }
        }

        private async Task<(bool Success, TopicScoresResponse Scores, AiProvider WinningProvider)> ProcessWithAiFallbackAsync(string rawText, string url, AiProvider primaryProvider)
        {
            // --- TENTATIVA 1: Provedor Principal ---
            var result = await CallAiProviderAsync(primaryProvider, rawText, url);

            // Checa bloqueio de IP no Gemini (se ele for o principal)
            if (!result.Success && primaryProvider == AiProvider.Gemini && IsIpBlockError(result.ErrorMessage))
            {
                // Se o usuário decidir encerrar na caixa de diálogo, retornamos falha imediatamente
                if (!HandleIpBlockWarning()) return (false, null, primaryProvider);
            }

            // Se o provedor principal teve sucesso, retornamos os dados
            if (result.Success && result.Data != null)
            {
                return (true, result.Data, primaryProvider);
            }

            // --- TENTATIVA 2: Fallback (Reserva) ---
            // Se o principal era Gemini, reserva é Groq (e vice-versa)
            var fallbackProvider = (primaryProvider == AiProvider.Gemini) ? AiProvider.Groq : AiProvider.Gemini;

            LogService.Warn($"⚠️ {primaryProvider} falhou para {url}. Acionando reserva {fallbackProvider}...");

            var fallbackResult = await CallAiProviderAsync(fallbackProvider, rawText, url);

            // Checa bloqueio de IP também no fallback (caso o reserva seja o Gemini)
            if (!fallbackResult.Success && fallbackProvider == AiProvider.Gemini && IsIpBlockError(fallbackResult.ErrorMessage))
            {
                if (!HandleIpBlockWarning()) return (false, null, fallbackProvider);
            }

            // Verificação de sucesso do reserva
            if (fallbackResult.Success && fallbackResult.Data != null)
            {
                LogService.Info($"✅ Fallback bem-sucedido! {fallbackProvider} processou {url}.");
                return (true, fallbackResult.Data, fallbackProvider);
            }
            else
            {
                // 🚨 CRÍTICO: Registra o motivo real da falha da segunda IA no log 🚨
                // Sem essa linha, não saberíamos se a Groq falhou por API Key, Limite de Uso ou Erro de JSON
                LogService.Error($"🚨 O Reserva ({fallbackProvider}) também falhou para {url}: {fallbackResult.ErrorMessage}");
            }

            // Se as duas falharem, retornamos o pacote informando a falha definitiva
            return (false, null, primaryProvider);
        }

        private bool IsIpBlockError(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage)) return false;
            return errorMessage.Contains("ExpectationFailed") ||
                   errorMessage.Contains("automated queries") ||
                   errorMessage.Contains("Sorry...");
        }

        private bool HandleIpBlockWarning()
        {
            if (_ipBlockWarningShown) return true;

            bool continuar = false;
            this.Invoke((MethodInvoker)delegate {
                var dr = MessageBox.Show(
                    "O Google detectou tráfego automatizado e bloqueou seu IP temporariamente.\n\n" +
                    "Recomendamos reiniciar seu modem para obter um novo IP.\n\n" +
                    "Deseja CONTINUAR tentando processar as notícias restantes (usando a Groq se o Gemini falhar)?",
                    "IP Bloqueado pelo Gemini",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                continuar = (dr == DialogResult.Yes);
            });

            _ipBlockWarningShown = true;

            if (!continuar)
            {
                LogService.Warn("🛑 Processamento cancelado pelo usuário após bloqueio de IP.");
                _cts?.Cancel();
            }
            return continuar;
        }



        private async Task<ServiceResult<TopicScoresResponse>> CallAiProviderAsync(AiProvider provider, string text, string url)
        {
            try
            {
                if (provider == AiProvider.Gemini)
                {
                    // 1. Recebe a Tupla do Gemini
                    var result = await _geminiService.ClassifyNewsAsync(text);

                    // 2. Converte manualmente para ServiceResult
                    if (result.Success)
                    {
                        return ServiceResult<TopicScoresResponse>.Ok((TopicScoresResponse)result.Data);
                    }
                    else
                    {
                        return ServiceResult<TopicScoresResponse>.Fail(result.ErrorMessage);
                    }
                }
                else
                {
                    // O Groq já retorna ServiceResult direto, então aqui não dá erro
                    return await _groqService.ClassifyNewsAsync(text);
                }
            }
            catch (Exception ex)
            {
                return ServiceResult<TopicScoresResponse>.Fail($"Exceção na IA ({provider}): {ex.Message}");
            }
        }

        private async Task ExecuteAiAndFilterAsync(string url, string rawText, string title)
        {
            var config = StorageManager.LoadConfig();

            bool success = false;
            dynamic iaData = null;
            string errorMsg = "";

            // 1. Decisão inicial de qual provedor usar (considerando o Cooldown do Groq)
            bool useGemini = config.SelectedProvider == AiProvider.Gemini;

            if (config.SelectedProvider == AiProvider.Groq)
            {
                if (DateTime.Now < _groqCooldownUntil)
                {
                    LogService.Warn($"[⏳ COOLDOWN] Groq em descanso até {_groqCooldownUntil:HH:mm:ss}. Usando Gemini para {url}");
                    useGemini = true;
                }
            }

            // 2. Primeira tentativa de chamada da IA
            if (useGemini)
            {
                var res = await _geminiService.ClassifyNewsAsync(rawText);
                success = res.Success; iaData = res.Data; errorMsg = res.ErrorMessage;
            }
            else
            {
                var res = await _groqService.ClassifyNewsAsync(rawText);
                success = res.Success; iaData = res.Data; errorMsg = res.ErrorMessage;
            }

            // 3. Fallback: Se tentou o Groq e deu Rate Limit (429), pula pro Gemini na mesma hora
            if (!success && !useGemini && errorMsg != null &&
               (errorMsg.Contains("429") || errorMsg.Contains("Limite") || errorMsg.ToLower().Contains("too many requests")))
            {
                LogService.Error($"[GROQ LIMIT] Groq sobrecarregado! Acionando Gemini de emergência...");

                // Põe o Groq de castigo
                _groqCooldownUntil = DateTime.Now.AddSeconds(40);

                // Tenta novamente agora com o Gemini
                var fallbackRes = await _geminiService.ClassifyNewsAsync(rawText);
                success = fallbackRes.Success;
                iaData = fallbackRes.Data;
                errorMsg = fallbackRes.ErrorMessage;

                // 👉 IMPORTANTE: Atualiza a flag para que os logs e contadores abaixo saibam que foi o Gemini quem resolveu
                useGemini = true;
            }

            // 4. Processamento dos dados em caso de sucesso
            if (success && iaData != null)
            {
                Dictionary<string, int> scores = new Dictionary<string, int>();
                string summary = "";

                try
                {
                    // Normalização: Transforma qualquer retorno em JSON universal para evitar crash de maiúsculas/minúsculas
                    string jsonUnificado = iaData is string ? (string)iaData : Newtonsoft.Json.JsonConvert.SerializeObject(iaData);
                    var parsedData = Newtonsoft.Json.Linq.JObject.Parse(jsonUnificado);

                    // Puxa as notas (Case-Insensitive)
                    var tokenScores = parsedData.GetValue("scores", StringComparison.OrdinalIgnoreCase);
                    if (tokenScores != null)
                        scores = tokenScores.ToObject<Dictionary<string, int>>();

                    // Puxa o resumo (Case-Insensitive)
                    var tokenSummary = parsedData.GetValue("summary", StringComparison.OrdinalIgnoreCase);
                    if (tokenSummary != null)
                        summary = tokenSummary.ToString().Trim().ToLower();
                }
                catch (Exception ex)
                {
                    LogService.Error($"[PARSER ERRO] Falha ao processar JSON da IA para {url}: {ex.Message}");
                    _iaErrorCount++;
                    return;
                }

                // 5. Categoria Vencedora e Filtro Anti-Duplicidade
                var topCategory = scores.OrderByDescending(x => x.Value).FirstOrDefault();

                if (topCategory.Value > 0 && !string.IsNullOrWhiteSpace(summary))
                {
                    bool isDuplicate = _summaryCache.Any(s =>
                        string.Equals(NormalizeCanonicalSummary(s.Summary), NormalizeCanonicalSummary(summary), StringComparison.Ordinal));

                    if (isDuplicate)
                    {
                        LogService.Info($"[♻️ DEDUPLICAÇÃO] Notícia repetida ignorada por resumo: '{summary}'");
                        _duplicateCount++;
                        if (_currentExecutionUsesFile) RemoveUrlFromConfiguredFile(url);
                        return;
                    }
                }

                // 6. Finalização e Contabilização
                // Definimos o nome correto para o log baseado em quem terminou a tarefa
                string provedorFinal = useGemini ? "Gemini" : "Groq";
                LogService.Info($"IA utilizada: {provedorFinal}");

                if (useGemini) _geminiSuccessCount++; else _groqSuccessCount++;

                HandleClassificationSuccess(url, title, scores, summary, rawText, false);
            }
            else
            {
                // Se após todas as tentativas falhou
                _lastIaError = errorMsg ?? "Resposta vazia ou erro desconhecido";
                LogService.Warn($"IA falhou definitivamente para {url}: {_lastIaError}");
                _iaErrorCount++;
            }
        }
        /// <summary>
        /// Processa o sucesso de uma classificação, seja vinda da IA ou do Cache.
        /// </summary>
        private void HandleClassificationSuccess(string url, string title, Dictionary<string, int> scores, string summary, string rawText, bool fromCache = false, string providerName = "IA")
        {
            NewsScoresItem item;

            // 1. Registro na lista global de pontuações
            lock (_scoresLock)
            {
                item = new NewsScoresItem
                {
                    Url = url,
                    Title = title,
                    Scores = scores,
                    Summary = summary,
                    RawText = rawText,
                    SourceOrder = _allNewsScores.Count,
                    AiProvider = providerName // Agora o compilador vai aceitar!
                };

                if (!_allNewsScores.Any(n => n.Url == url))
                {
                    _allNewsScores.Add(item);
                    if (!fromCache) _successCount++;
                }
                else
                {
                    // Atualiza caso já exista (útil para recarregar do cache com novos dados)
                    var existing = _allNewsScores.First(n => n.Url == url);
                    existing.Scores = scores;
                    existing.Summary = summary;
                    existing.RawText = rawText;
                    existing.AiProvider = providerName;
                }
            }

            // 2. Persistência no Cache de IA
            if (!fromCache)
            {
                UpsertEvaluatedCache(url, item);
            }

            UpdateStatusLabel();

            // 3. RECONSTRUÇÃO DO RANKING (Lógica Multi-Categoria)
            // Aqui garantimos que a notícia se espalhe por todos os tópicos que pontuou
            List<TopicResult> partialResults = new List<TopicResult>();

            foreach (var news in _allNewsScores)
            {
                foreach (var s in news.Scores)
                {
                    if (s.Value > 0)
                    {
                        partialResults.Add(new TopicResult
                        {
                            Topic = s.Key,
                            Url = news.Url,
                            Score = s.Value,
                            Summary = news.Summary,
                            AiProvider = news.AiProvider
                        });
                    }
                }
            }

            // Ordenação: Alfabética por Tópico e depois maior Score no topo
            partialResults = partialResults
                .OrderBy(r => r.Topic)
                .ThenByDescending(r => r.Score)
                .ToList();

            // 4. Preservação do estado de "Lido" (IsClicked)
            foreach (var novoResultado in partialResults)
            {
                var antigo = _currentTopicResults.FirstOrDefault(r => r.Url == novoResultado.Url && r.Topic == novoResultado.Topic);
                if (antigo != null)
                {
                    novoResultado.IsClicked = antigo.IsClicked;
                }
            }

            // 5. Atualiza a lista global e salva o estado
            _currentTopicResults = partialResults;
            SaveLastResults();

            // 6. Atualiza a Grid (apenas o que não foi lido)
            var itensParaMostrar = _currentTopicResults.Where(r => !r.IsClicked).ToList();
            DisplayTopicResults(itensParaMostrar);

            if (!fromCache)
            {
                LogService.Info($"[OK] ({providerName}) Notícia classificada e distribuída: {url}");

                // Se estiver processando lote de arquivo, limpa a linha do TXT
                if (_currentExecutionUsesFile)
                {
                    RemoveUrlFromConfiguredFile(url);
                }
            }
        }

        private void SaveEvaluatedCache()
        {
            try
            {
                LogService.Info($"[CACHE] Salvando arquivo: {_cachePath}");
                LogService.Info($"[CACHE] Entradas a persistir: {_evaluatedCache.Count}");
                lock (_evaluatedCacheFileLock)
                {
                    var snapshot = new Dictionary<string, NewsScoresItem>(_evaluatedCache);
                    LogService.Info($"[CACHE] Entradas a persistir: {snapshot.Count}");
                    string json = JsonConvert.SerializeObject(snapshot, Newtonsoft.Json.Formatting.Indented);
                    LogService.Info($"[CACHE] JSON serializado: {System.Text.Encoding.UTF8.GetByteCount(json)} bytes");
                    string tempPath = _cachePath + ".tmp";
                    File.WriteAllText(tempPath, json, System.Text.Encoding.UTF8);
                    if (File.Exists(_cachePath))
                        File.Replace(tempPath, _cachePath, null, true);
                    else
                        File.Move(tempPath, _cachePath);
                }
                var fileInfo = new FileInfo(_cachePath);
                LogService.Info("[CACHE] Arquivo salvo com sucesso");
                LogService.Info($"[CACHE] Tamanho final: {fileInfo.Length} bytes");
                LogService.Info($"[CACHE] Data de modificação: {fileInfo.LastWriteTime:o}");
            }
            catch (Exception ex)
            {
                LogService.Error("[CACHE] Erro ao salvar memória de avaliações.", ex);
            }
        }

        private void UpdateStatusLabel()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(UpdateStatusLabel));
                return;
            }
            // Dashboard simplificado para o rodapé (lblInfo)
            lblInfo.Text = $"✅ Sucesso: {_successCount} | ♻️ Duplicadas: {_duplicateCount} | ⚡ Cache: {_cacheHitCount} | 🤖 Erros IA: {_iaErrorCount}";
        }

        // 

        private void SaveFinalRankingToFile(List<TopicResult> results)
        {
            try
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                _lastReportPath = Path.Combine(folder, $"NewsRanking_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                var lines = new List<string>();

                // 1. Constrói cada seção do relatório
                AddReportHeader(lines);
                AddMonitoredTopicsSection(lines);
                AddRankingSection(lines, results);
                AddExecutionSummarySection(lines);
                AddFailedDomainsSection(lines);
                AddFailuresSection(lines);
                AddCostSummarySection(lines);

                // 2. Salva o arquivo final
                File.WriteAllLines(_lastReportPath, lines);
                LogService.Info($"Relatório completo salvo em: {_lastReportPath}");
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao gerar relatório final", ex);
            }
        }

        // ==============================================
        // SUB-MÉTODOS DE GERAÇÃO DO RELATÓRIO
        // ==============================================

        private void AddReportHeader(List<string> lines)
        {
            lines.Add("================================================");
            lines.Add("         NEWS TOPIC RANKING REPORT              ");
            lines.Add("================================================");
            lines.Add($"Data: {DateTime.Now}");
            lines.Add($"IA Utilizada: {StorageManager.LoadConfig().SelectedProvider}");
            lines.Add("");
        }

        private void AddMonitoredTopicsSection(List<string> lines)
        {
            lines.Add("===== TÓPICOS MONITORADOS =====");
            if (_allNewsScores.Any())
            {
                var allTopics = _allNewsScores.First().Scores.Keys.OrderBy(t => t).ToList();
                foreach (var topicName in allTopics)
                {
                    int count = _allNewsScores.Count(n => n.Scores.ContainsKey(topicName) && n.Scores[topicName] > 0);
                    lines.Add($"- {topicName.PadRight(30)} ({count} notícias encontradas)");
                }
            }
            else
            {
                lines.Add("Nenhum tópico processado.");
            }
            lines.Add("");
        }

        private void AddRankingSection(List<string> lines, List<TopicResult> results)
        {
            lines.Add("===== MELHORES POR ASSUNTO (RANKING) =====");
            lines.Add("");

            if (results.Count == 0)
            {
                lines.Add("Nenhuma notícia atingiu os critérios mínimos para o ranking.");
            }
            else
            {
                foreach (var r in results)
                {
                    lines.Add($"📌 TÓPICO: {r.Topic.ToUpper()}");
                    lines.Add($"⭐ SCORE : {r.Score}");
                    lines.Add($"🔗 URL   : {r.Url}");
                    lines.Add($"📄 TÍTULO: {r.Title}");
                    lines.Add(new string('-', 40));
                }
            }
            lines.Add("");
        }

        private void AddExecutionSummarySection(List<string> lines)
        {
            lines.Add("===== RESUMO DA EXECUÇÃO =====");
            lines.Add($"Total de URLs Processadas       : {progressBar.Value}");
            lines.Add($"Sucessos de Ranking (Inéditas)  : {_successCount - _cacheHitCount}");
            lines.Add($"Reaproveitadas via Cache (⚡)   : {_cacheHitCount}");
            lines.Add($"Descartadas por Duplicidade (♻️) : {_duplicateCount}");
            lines.Add($"Falhas de IA (🤖)               : {_iaErrorCount}");
            lines.Add($"Falhas de Scraping (🌐)         : {_scrapErrorCount}");
            lines.Add($"- Processados pelo DeepSeek     : {_deepSeekSuccessCount}");
            lines.Add($"- Processados pelo Groq         : {_groqSuccessCount}");
            lines.Add($"- Processados pelo Gemini       : {_geminiSuccessCount}");
            lines.Add($"- Processados pelo Mistral      : {_mistralSuccessCount}");
            lines.Add($"Resumos canônicos gerados       : {_resumosCanonicosGerados}");
            lines.Add($"Duplicatas detectadas           : {_duplicatasPorResumo}");
            lines.Add($"Avaliações completas evitadas   : {_avaliacoesCompletasEvitadas}");
            lines.Add($"Avaliações completas executadas : {_avaliacoesCompletasExecutadas}");
            lines.Add($"Tempo Total de Execução         : {_executionTimer.Elapsed:hh\\:mm\\:ss}");
            lines.Add("");
        }

        private void AddCostSummarySection(List<string> lines)
        {
            lines.Add("--------------------------------------------------");
            lines.Add("RESUMO DE CUSTOS DE IA:");
            lines.Add($"- Gemini: prompt={CostManager.GetGeminiPromptTokens()}, completion={CostManager.GetGeminiCompletionTokens()}, total={CostManager.GetGeminiTokens()} tokens (Custo: ${CostManager.GetGeminiCost():0.000000})");
            lines.Add($"- Groq: prompt={CostManager.GetGroqPromptTokens()}, completion={CostManager.GetGroqCompletionTokens()}, total={CostManager.GetGroqTokens()} tokens (Custo: ${CostManager.GetGroqCost():0.000000})");
            lines.Add($"- CUSTO TOTAL DA OPERAÇÃO: ${(CostManager.GetGeminiCost() + CostManager.GetGroqCost()):0.000000}");
            lines.Add("--------------------------------------------------");
            lines.Add("");
        }

        private void AddFailedDomainsSection(List<string> lines)
        {
            lock (_failedDomainsLock)
            {
                if (_failedDomains.Any())
                {
                    lines.Add("===== DOMÍNIOS COM PROBLEMAS (HTTP 403/404/Timeout) =====");
                    foreach (var d in _failedDomains.Distinct())
                    {
                        lines.Add($"❌ {d}");
                    }
                    lines.Add("");
                }
            }
        }

        private void AddFailuresSection(List<string> lines)
        {
            lines.Add("=======================================================");
            lines.Add("📋 NOTÍCIAS NÃO CATEGORIZADAS / FALHAS DE PROCESSAMENTO");
            lines.Add("=======================================================\n");

            if (LogService.FalhasProcessamento.Count == 0)
            {
                lines.Add("Nenhuma falha registrada! Todas as URLs funcionaram.");
            }
            else
            {
                foreach (var falha in LogService.FalhasProcessamento)
                {
                    lines.Add($"URL: {falha.Key}");
                    lines.Add($"Motivo: {falha.Value}\n");
                }
                lines.Add($"Total de falhas registradas: {LogService.FalhasProcessamento.Count}");
            }
        }

        //private void SaveFinalRankingToFile(List<TopicResult> results)
        //{
        //    // METHOD v2: SaveFinalRankingToFile
        //    // Alteração: Inclusão da seção de Tópicos Monitorados e estatísticas por categoria.
        //    try
        //    {
        //        string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        //        _lastReportPath = Path.Combine(
        //            folder,
        //            $"NewsRanking_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        //        );

        //        var lines = new List<string>();

        //        lines.Add("================================================");
        //        lines.Add("         NEWS TOPIC RANKING REPORT              ");
        //        lines.Add("================================================");
        //        lines.Add($"Data: {DateTime.Now}");
        //        lines.Add($"IA Utilizada: {StorageManager.LoadConfig().SelectedProvider}");
        //        lines.Add("");

        //        // --- NOVA SEÇÃO: TÓPICOS MONITORADOS ---
        //        lines.Add("===== TÓPICOS MONITORADOS =====");
        //        if (_allNewsScores.Any())
        //        {
        //            // Pegamos as chaves (nomes dos tópicos) da primeira notícia com sucesso
        //            var allTopics = _allNewsScores.First().Scores.Keys.OrderBy(t => t).ToList();
        //            foreach (var topicName in allTopics)
        //            {
        //                // Conta quantas notícias bateram nesse tópico (score > 0)
        //                int count = _allNewsScores.Count(n => n.Scores.ContainsKey(topicName) && n.Scores[topicName] > 0);
        //                lines.Add($"- {topicName.PadRight(30)} ({count} notícias encontradas)");
        //            }
        //        }
        //        else
        //        {
        //            lines.Add("Nenhum tópico processado.");
        //        }
        //        lines.Add("");

        //        lines.Add("===== MELHORES POR ASSUNTO (RANKING) =====");
        //        lines.Add("");

        //        if (results.Count == 0)
        //        {
        //            lines.Add("Nenhuma notícia atingiu os critérios mínimos para o ranking.");
        //        }
        //        else
        //        {
        //            foreach (var r in results)
        //            {
        //                lines.Add($"📌 TÓPICO: {r.Topic.ToUpper()}");
        //                lines.Add($"⭐ SCORE : {r.Score}");
        //                lines.Add($"🔗 URL   : {r.Url}");
        //                lines.Add($"📄 TÍTULO: {r.Title}");
        //                lines.Add(new string('-', 40));
        //            }
        //        }

        //        lines.Add("");
        //        lines.Add("===== RESUMO DA EXECUÇÃO =====");
        //        lines.Add($"Total de URLs analisadas      : {_allNewsScores.Count + _iaErrorCount + _scrapErrorCount}");
        //        lines.Add($"Sucessos de Classificação     : {_allNewsScores.Count}");
        //        lines.Add($"Falhas de IA (🤖)            : {_iaErrorCount}");
        //        lines.Add($"Falhas de Scraping (🌐)       : {_scrapErrorCount}");
        //        lines.Add($"Tópicos com match no Ranking  : {results.Count}");
        //        lines.Add("");

        //        lines.Add("===== RESUMO DA EXECUÇÃO =====");
        //        lines.Add($"Total de URLs processadas     : {_allNewsScores.Count + _iaErrorCount + _scrapErrorCount}");
        //        lines.Add($"Sucessos Totais (IA + Cache)  : {_successCount}");
        //        lines.Add($"   -> Desse total, via Cache  : {_cacheHitCount} ⚡"); // 👉 NOVO NO TXT
        //        lines.Add($"Falhas de IA (🤖)             : {_iaErrorCount}");
        //        lines.Add($"Falhas de Scraping (🌐)       : {_scrapErrorCount}");
        //        lines.Add($"Tópicos com match no Ranking  : {results.Count}");
        //        lines.Add("");

        //        // ... dentro do método SaveFinalRankingToFile ...
        //        lines.Add("");
        //        lines.Add("===== RESUMO DA EXECUÇÃO =====");
        //        lines.Add($"Total de URLs Processadas       : {progressBar.Value}");
        //        lines.Add($"Sucessos de Ranking (Inéditas)  : {_successCount - _cacheHitCount}");
        //        lines.Add($"Reaproveitadas via Cache (⚡)   : {_cacheHitCount}");
        //        lines.Add($"Descartadas por Duplicidade (♻️) : {_duplicateCount}"); // 👉 NOVO
        //        lines.Add($"Falhas de IA (🤖)               : {_iaErrorCount}");
        //        lines.Add($"Falhas de Scraping (🌐)         : {_scrapErrorCount}");
        //        lines.Add($"Tempo Total de Execução         : {_executionTimer.Elapsed:hh\\:mm\\:ss}");
        //        lines.Add("");

        //        // Exemplo de como adicionar no seu StringBuilder (sb) ou texto do relatório:
        //        lines.Add("=== RESUMO DE PROCESSAMENTO ===");
        //        lines.Add($"Total de Sucessos: {_successCount}");
        //        lines.Add($"- Processados pelo Groq: {_groqSuccessCount}");
        //        lines.Add($"- Processados pelo Gemini: {_geminiSuccessCount}");
        //        lines.Add($"- Recuperados da Memória (Cache): {_cacheHitCount}");
        //        lines.Add($"Falhas de IA: {_iaErrorCount}");
        //        lines.Add($"Falhas de Scraping: {_scrapErrorCount}");
        //        lines.Add("");

        //        // --- DOMÍNIOS COM FALHA ---
        //        lock (_failedDomainsLock)
        //        {
        //            if (_failedDomains.Any())
        //            {
        //                lines.Add("===== DOMÍNIOS COM PROBLEMAS (HTTP 403/404/Timeout) =====");
        //                foreach (var d in _failedDomains.Distinct())
        //                    lines.Add($"❌ {d}");
        //            }
        //        }

        //        // Na hora de montar o relatório final (seu StringBuilder sb):
        //        lines.Add("\n=======================================================");
        //        lines.Add("📋 NOTÍCIAS NÃO CATEGORIZADAS / FALHAS DE PROCESSAMENTO");
        //        lines.Add("=======================================================\n");

        //        if (LogService.FalhasProcessamento.Count == 0)
        //        {
        //            lines.Add("Nenhuma falha registrada! Todas as URLs funcionaram.");
        //        }
        //        else
        //        {
        //            foreach (var falha in LogService.FalhasProcessamento)
        //            {
        //                lines.Add($"URL: {falha.Key}");
        //                lines.Add($"Motivo: {falha.Value}\n");
        //            }
        //            lines.Add($"Total de falhas registradas: {LogService.FalhasProcessamento.Count}");
        //        }

        //        File.WriteAllLines(_lastReportPath, lines);
        //        LogService.Info($"Relatório completo salvo em: {_lastReportPath}");
        //    }
        //    catch (Exception ex)
        //    {
        //        LogService.Error("Erro ao gerar relatório final", ex);
        //    }
        //}

        // ✅ Adicionar este método na classe MainForm se ainda não existir
        private string ExtractDomain(string url)
        {
            try
            {
                var uri = new Uri(url);
                return uri.Host.ToLower();
            }
            catch
            {
                // Fallback: extrair domínio manualmente se a URL for malformada
                try
                {
                    var uri = new UriBuilder(url).Host.ToLower();
                    return uri;
                }
                catch
                {
                    return url;
                }
            }
        }

        private void ShowFailedDomainsReport()
        {
            List<string> failedDomainsCopy;
            int successCount, failCount;

            lock (_failedDomainsLock)
            {
                failedDomainsCopy = new List<string>(_failedDomains);
            }

            // ✅ Contar sucessos e falhas no DataGridView
            successCount = dgvResults.Rows.Cast<DataGridViewRow>()
                .Count(r => r.Cells["colStatus"].Value?.ToString() == "Sucesso");
            failCount = dgvResults.Rows.Count - successCount;

            // ✅ Log de estatísticas
            double successRate = dgvResults.Rows.Count > 0
                ? (successCount * 100.0 / dgvResults.Rows.Count)
                : 0;

            LogService.Info($"=== Relatório Final de Processamento ===");
            LogService.Info($"Total de URLs: {dgvResults.Rows.Count}");
            LogService.Info($"✅ Sucesso: {successCount}");
            LogService.Info($"❌ Falha: {failCount}");
            LogService.Info($"📊 Taxa de sucesso: {successRate:F1}%");

            if (failedDomainsCopy.Count > 0)
            {
                LogService.Warn($"=== Domínios com Falha de Leitura ({failedDomainsCopy.Count}) ===");

                string reportMessage = $"O processo foi concluído, mas {failedDomainsCopy.Count} domínio(s) não puderam ser lidos:\n\n";
                reportMessage += string.Join("\n", failedDomainsCopy.Select((d, i) => $"{i + 1}. {d}"));
                reportMessage += $"\n\n📊 Resumo: {successCount} sucesso(s) / {failCount} falha(s) / {successRate:F1}% taxa de sucesso";

                LogService.Warn("Domínios falhos: " + string.Join(", ", failedDomainsCopy));

                MessageBox.Show(reportMessage, "Domínios com Falha de Leitura", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                LogService.Info("🎉 Todos os domínios foram processados com sucesso!");
                MessageBox.Show($"Processamento concluído com sucesso!\n\n{successCount} URL(s) processada(s) com {successRate:F1}% de taxa de sucesso.",
                    "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void AddOrUpdateGrid(NewsItem item)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => AddOrUpdateGrid(item)));
                return;
            }

            dgvResults.Rows.Add(
                item.ImpactScore,
                item.Title,
                item.Url,
                item.Category,
                item.ImpactReason,
                item.Status,
                item.ProcessedAt.ToString("g")
            );

            // 🔥 Ordenar imediatamente após inserir
            dgvResults.Sort(dgvResults.Columns["colImpact"],
                System.ComponentModel.ListSortDirection.Descending);
        }

        private void UpdateProgress()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(UpdateProgress));
                return;
            }

            if (progressBar.Value < progressBar.Maximum)
            {
                progressBar.Value++;
                lblProgress.Text = $"{progressBar.Value}/{progressBar.Maximum}";
            }
        }

        private void SortByImpact()
        {
            dgvResults.Sort(dgvResults.Columns["colImpact"], System.ComponentModel.ListSortDirection.Descending);
        }

        private void ToggleUI(bool enabled)
        {
            btnStart.Enabled = enabled;
            btnConfig.Enabled = enabled;
            txtUrls.Enabled = enabled;
            //nudParallelism.Enabled = enabled;
        }

        private void UpdateCostLabel()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateCostLabel));
                return;
            }

            lblTotalCost.Text = CostManager.GetFormattedTotalCost();
        }

        private void btnCopyCost_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(CostManager.GetFormattedTotalCost());
                var original = btnCopyCost.Text;
                btnCopyCost.Text = "Copiado!";

                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 1200;
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    if (!IsDisposed)
                        btnCopyCost.Text = original;
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao copiar custo", ex);
            }
        }

        private void dgvResults_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            // ✅ Verificar se o clique foi na coluna de URL e em uma linha válida
            if (e.RowIndex >= 0 && e.ColumnIndex == dgvResults.Columns["colUrl"].Index)
            {
                string url = dgvResults.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString();

                if (!string.IsNullOrEmpty(url))
                {
                    try
                    {
                        // ✅ COPIAR URL PARA ÁREA DE TRANSFERÊNCIA
                        Clipboard.SetText(url);

                        // ✅ FEEDBACK: Log da ação
                        LogService.Info($"URL copiada para clipboard: {url}");

                        // ✅ Referências da linha/célula
                        var row = dgvResults.Rows[e.RowIndex];
                        var cell = row.Cells[e.ColumnIndex];

                        // ✅ Guardar estado original da célula (para restaurar texto e cor do texto)
                        var originalValue = cell.Value;
                        var originalForeColor = cell.Style.ForeColor;

                        // ✅ Guardar estado original da linha (opcional, caso queira restaurar no futuro)
                        // Aqui NÃO vamos restaurar a linha, porque você quer manter a cor laranja.
                        // var originalRowBackColor = row.DefaultCellStyle.BackColor;

                        // ✅ FEEDBACK VISUAL NA CÉLULA (temporário)
                        cell.Value = "✓ Copiado!";
                        cell.Style.ForeColor = Color.Green;

                        // ✅ REMOVER A URL DO ARQUIVO (mas NÃO remove do grid)
                        RemoveUrlFromConfiguredFile(url);

                        // ✅ MARCAR A LINHA COM LARANJA FRACO (permanente)
                        row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 200); // laranja fraco
                        row.DefaultCellStyle.ForeColor = Color.Black;

                        // ✅ Restaurar APENAS a célula (texto/cor do texto) após 1.5 segundos
                        var timer = new System.Windows.Forms.Timer();
                        timer.Interval = 1500;
                        timer.Tick += (s, args) =>
                        {
                            timer.Stop();
                            timer.Dispose();

                            if (this.IsDisposed) return;

                            // Thread-safe: garantir que a atualização da UI seja na thread principal
                            if (this.InvokeRequired)
                            {
                                this.Invoke(new Action(() =>
                                {
                                    cell.Value = originalValue;
                                    cell.Style.ForeColor = originalForeColor;
                                }));
                            }
                            else
                            {
                                cell.Value = originalValue;
                                cell.Style.ForeColor = originalForeColor;
                            }
                        };
                        timer.Start();
                    }
                    catch (Exception ex)
                    {
                        // ✅ Tratamento de erro caso o clipboard não esteja acessível
                        LogService.Error($"Erro ao copiar URL para clipboard: {url}", ex);
                        MessageBox.Show(
                            "Não foi possível copiar o link para a área de transferência.\n\n" +
                            "Dica: Verifique se outro aplicativo não está bloqueando o clipboard.",
                            "Erro ao Copiar",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                    }
                }
            }
        }

        private void RemoveUrlFromConfiguredFile(string url)
        {
            try
            {
                var config = StorageManager.LoadConfig();

                if (string.IsNullOrWhiteSpace(config.NewsFilePath) || !File.Exists(config.NewsFilePath))
                    return;

                var lines = File.ReadAllLines(config.NewsFilePath).ToList();

                int removed = lines.RemoveAll(l => l.Trim() == url);

                if (removed > 0)
                {
                    File.WriteAllLines(config.NewsFilePath, lines);
                    LogService.Info($"URL removida do arquivo de entrada: {url}");
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao remover URL do arquivo", ex);
            }
        }

        private void btnOpenReport_Click(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_lastReportPath) ||
                    !File.Exists(_lastReportPath))
                {
                    MessageBox.Show("Nenhum relatório foi gerado ainda.",
                        "Aviso",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = _lastReportPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao abrir relatório", ex);
                MessageBox.Show("Erro ao abrir o relatório.",
                    "Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }


        private void btnOpenLog_Click(object sender, EventArgs e)
        {
            try
            {
                string path = LogService.GetLogPath();

                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    MessageBox.Show("Log ainda não foi gerado.",
                        "Aviso",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao abrir log", ex);
            }
        }

        private List<string> LoadUrlsFromSource(AppConfig config)
        {
            List<string> urls = new List<string>();

            // 1️⃣ Verificar se há URLs digitadas na interface
            var textUrls = txtUrls.Lines
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Where(l => UrlValidator.IsValid(l))
                .Distinct()
                .ToList();

            if (textUrls.Count > 0)
            {
                LogService.Info($"Fonte de URLs: campo de texto ({textUrls.Count} URLs)");
                urls.AddRange(textUrls);
                return urls;
            }

            // 2️⃣ Caso contrário usar o arquivo configurado
            if (string.IsNullOrWhiteSpace(config.NewsFilePath) || !File.Exists(config.NewsFilePath))
            {
                throw new Exception("Arquivo de URLs não configurado ou não encontrado.");
            }

            var fileUrls = File.ReadAllLines(config.NewsFilePath)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Where(l => UrlValidator.IsValid(l))
                .Distinct()
                .ToList();

            LogService.Info($"Fonte de URLs: arquivo ({fileUrls.Count} URLs)");
            LogService.Info($"Arquivo utilizado: {config.NewsFilePath}");

            urls.AddRange(fileUrls);

            return urls;
        }

        private List<TopicResult> SelectBestNewsPerTopic(int minimumScore)
        {
            var topicResults = new List<TopicResult>();

            // Rastreador: Impede que a MESMA notícia ocupe duas linhas no Grid final
            var usedNewsUrls = new HashSet<string>();

            // 1. Navegamos pelos códigos oficiais do seu TopicCatalog
            foreach (var sigla in TopicCatalog.Codes)
            {
                // 2. Selecionamos as notícias que tiraram a nota mínima para esta sigla
                // E que ainda NÃO FORAM usadas em categorias anteriores
                var eligibleNews = _allNewsScores
                    .Where(n => n.Scores.ContainsKey(sigla) && n.Scores[sigla] >= minimumScore)
                    .Where(n => !usedNewsUrls.Contains(n.Url))
                    .ToList();

                if (eligibleNews.Any())
                {
                    // Pega a melhor notícia para este tópico específico
                    var winner = eligibleNews
                        .OrderByDescending(n => n.Scores[sigla])
                        .FirstOrDefault();

                    if (winner != null)
                    {
                        // Registra que a notícia ganhou uma vaga (não poderá entrar nas próximas categorias)
                        usedNewsUrls.Add(winner.Url);

                        // Busca o nome amigável (Ex: "CS" -> "Ciência Controversa")
                        string nomeCompleto = TopicCatalog.CodeToName.ContainsKey(sigla)
                                              ? TopicCatalog.CodeToName[sigla]
                                              : sigla;

                        topicResults.Add(new TopicResult
                        {
                            Topic = nomeCompleto,
                            Url = winner.Url,
                            Score = winner.Scores[sigla],
                            Summary = winner.Summary,
                            IsClicked = false
                        });
                    }
                }
            }

            // 4. Retorna a lista ordenada pelo Score (85 no topo, etc)
            return topicResults.OrderByDescending(r => r.Score).ToList();
        }

        private void DisplayTopicResults(List<TopicResult> results)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<List<TopicResult>>(DisplayTopicResults), results);
                return;
            }

            // 1. Configurações de Fonte e Estilo
            // Você pode alterar o "12" para o tamanho que desejar (ex: 11, 14, etc)
            Font fonteTexto = new Font("Segoe UI", 12f, FontStyle.Regular);
            Font fonteCabecalho = new Font("Segoe UI", 11f, FontStyle.Bold);

            dgvTopicResults.DataSource = null;
            dgvTopicResults.Columns.Clear();
            dgvTopicResults.AutoGenerateColumns = false;

            // 2. Aplica a fonte nas células e nos cabeçalhos
            dgvTopicResults.DefaultCellStyle.Font = fonteTexto;
            dgvTopicResults.ColumnHeadersDefaultCellStyle.Font = fonteCabecalho;

            // IMPORTANTE: Ajusta a altura da linha para caber a fonte maior
            dgvTopicResults.RowTemplate.Height = 35;

            // 3. Campo 1: Assunto
            dgvTopicResults.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colTopic",
                HeaderText = "Assunto",
                DataPropertyName = "Topic",
                Width = 180 // Aumentei um pouco para compensar a fonte maior
            });

            // 4. Campo 2: Score
            dgvTopicResults.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colScore",
                HeaderText = "Score",
                DataPropertyName = "Score",
                Width = 60,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Font = new Font(fonteTexto, FontStyle.Bold) // Score em negrito
                }
            });

            // 5. Campo 3: Resumo
            dgvTopicResults.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colSummary",
                HeaderText = "Resumo",
                DataPropertyName = "Summary",
                Width = 450
            });

            // 6. Campo 4: URL
            dgvTopicResults.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = TopicUrlColumnName,
                HeaderText = "URL",
                DataPropertyName = "Url",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });

            dgvTopicResults.Columns.Add(new DataGridViewButtonColumn
            {
                Name = CopyScrapColumnName,
                HeaderText = "",
                Text = "📋",
                Width = 44,
                UseColumnTextForButtonValue = true,
                ToolTipText = "Copiar scrap"
            });

            // Estética adicional
            dgvTopicResults.RowHeadersVisible = false;
            dgvTopicResults.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvTopicResults.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240); // Linhas alternadas para facilitar leitura

            dgvTopicResults.DataSource = results;
        }        

        private async void dgvTopicResults_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            try
            {
                var row = dgvTopicResults.Rows[e.RowIndex];
                string url = row.Cells[TopicUrlColumnName].Value?.ToString();
                string columnName = dgvTopicResults.Columns[e.ColumnIndex].Name;

                if (string.IsNullOrWhiteSpace(url)) return;

                if (columnName == TopicUrlColumnName)
                {
                    Clipboard.SetText(url);
                    MarkTopicResultAsHandled(url);
                    row.DefaultCellStyle.BackColor = Color.FromArgb(204, 120, 0);
                    row.DefaultCellStyle.ForeColor = Color.White;
                    ShowTopicGridFeedback(row, e.ColumnIndex, "✓ URL");
                    return;
                }

                if (columnName == CopyScrapColumnName)
                {
                    string scrapText = await GetOrFetchScrapTextAsync(url);

                    if (string.IsNullOrWhiteSpace(scrapText))
                    {
                        MessageBox.Show("Esta notícia não possui scrap salvo para cópia.", "Scrap indisponível", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    Clipboard.SetText(scrapText);
                    _evaluatedCache.TryGetValue(url, out var cachedNews);
                    SaveSummaryToMemory(url, cachedNews);
                    MarkTopicResultAsHandled(url);
                    row.DefaultCellStyle.BackColor = Color.FromArgb(204, 120, 0);
                    row.DefaultCellStyle.ForeColor = Color.White;
                    ShowTopicGridFeedback(row, e.ColumnIndex, "✓ Scrap");
                    return;
                }

                return;
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao processar clique na URL do ranking", ex);
            }
        }

        private async Task<string> GetOrFetchScrapTextAsync(string url)
        {
            if (_evaluatedCache.TryGetValue(url, out var cachedNews) && !string.IsNullOrWhiteSpace(cachedNews.RawText))
            {
                return cachedNews.RawText;
            }

            UpdateInfoLabel(GetFormattedStatus("Buscando scrap da notícia..."));

            var newsItem = await _scrapingService.ScrapeAsync(url);
            if (newsItem == null || newsItem.Status != "Sucesso" || string.IsNullOrWhiteSpace(newsItem.RawText))
            {
                LogService.Warn($"Scrap sob demanda indisponível para {url}");
                return null;
            }

            if (cachedNews == null)
            {
                var topicItem = _currentTopicResults.FirstOrDefault(r => r.Url == url);
                cachedNews = new NewsScoresItem
                {
                    Url = url,
                    Title = newsItem.Title,
                    Summary = topicItem?.Summary,
                    RawText = newsItem.RawText
                };
                _evaluatedCache[url] = cachedNews;
            }
            else
            {
                cachedNews.RawText = newsItem.RawText;
                if (string.IsNullOrWhiteSpace(cachedNews.Title))
                {
                    cachedNews.Title = newsItem.Title;
                }
            }

            SaveEvaluatedCache();
            LogService.Info($"[SCRAP] Conteúdo atualizado sob demanda para {url}");
            return newsItem.RawText;
        }

        private void SaveSummaryToMemory(string url, NewsScoresItem fullNewsItem)
        {
            if (fullNewsItem == null || string.IsNullOrWhiteSpace(fullNewsItem.Summary) || fullNewsItem.Scores == null)
            {
                return;
            }

            var topCategory = fullNewsItem.Scores.OrderByDescending(x => x.Value).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(topCategory.Key) || topCategory.Value <= 0)
            {
                return;
            }

            bool alreadyInCache = _summaryCache.Any(s =>
                string.Equals(NormalizeCanonicalSummary(s.Summary), NormalizeCanonicalSummary(fullNewsItem.Summary), StringComparison.Ordinal));

            if (alreadyInCache)
            {
                return;
            }

            _summaryCache.Add(new SummaryCacheItem
            {
                Summary = fullNewsItem.Summary,
                TopCategory = topCategory.Key,
                DateAdded = DateTime.Now
            });

            SummaryCacheManager.SaveCache(_summaryCache);
            LogService.Info($"[CACHE] Resumo memorizado manualmente para a notícia: {url}");
        }

        private void MarkTopicResultAsHandled(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            var persistedResults = ReadLastResults()
                .Where(r => !string.Equals(r.Url, url, StringComparison.OrdinalIgnoreCase))
                .ToList();

            SaveLastResults(persistedResults);

            if (_currentExecutionUsesFile)
            {
                RemoveUrlFromConfiguredFile(url);
            }
        }

        private void ShowTopicGridFeedback(DataGridViewRow row, int columnIndex, string feedbackText)
        {
            var cell = row.Cells[columnIndex];
            var originalValue = cell.Value;
            var originalColor = cell.Style.ForeColor;

            cell.Value = feedbackText;
            cell.Style.ForeColor = Color.Green;

            var timer = new System.Windows.Forms.Timer { Interval = 1500 };
            timer.Tick += (s, args) =>
            {
                timer.Stop();
                timer.Dispose();
                if (!this.IsDisposed)
                {
                    try
                    {
                        cell.Value = originalValue;
                        cell.Style.ForeColor = originalColor;
                    }
                    catch
                    {
                    }

                    UpdateInfoLabel(GetFormattedStatus($"Restam {_currentTopicResults.Count(r => !r.IsClicked)} pendentes"));
                }
            };
            timer.Start();
        }

        private void UpdateInfoLabel(string message)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => UpdateInfoLabel(message)));
                return;
            }

            lblInfo.Text = message;
        }

        private void ApplyVersionToCaption()
        {
            var fileVersion = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyFileVersionAttribute>()?
                .Version;

            Text = string.IsNullOrWhiteSpace(fileVersion)
                ? MainCaptionBase
                : $"{MainCaptionBase} v{fileVersion}";
        }

        // METHOD v8: DelayWithCountdownAsync
        private async Task DelayWithCountdownAsync(int waitTimeMs)
        {
            int secondsToWait = waitTimeMs / 1000;

            while (secondsToWait > 0)
            {
                // Mostra o dashboard completo + o cronômetro da pausa
                UpdateInfoLabel(GetFormattedStatus($"Pausa de segurança: {secondsToWait}s (Limite Groq)"));
                await Task.Delay(1000);
                secondsToWait--;
            }
        }

        private async Task CheckAndDelayForTokenLimitAsync(string textToProcess)
        {
            // Estima a quantidade de tokens: (caracteres / 4) + margem do prompt
            // int estimatedTokens = (textToProcess.Length / 4) + 200;
            // int estimatedTokens = (textToProcess.Length / 4) + 1000;
            // int estimatedTokens = (textToProcess.Length / 4) + 2000;
            int estimatedTokens = (textToProcess.Length / 3) + 3000;

            int waitTimeMs = 0;

            lock (_tokenLock)
            {
                TimeSpan elapsed = DateTime.Now - _minuteStartTime;

                // Se já passou mais de 1 minuto, reinicia a janela de contagem
                if (elapsed.TotalMinutes >= 1)
                {
                    _tokensCurrentMinute = 0;
                    _minuteStartTime = DateTime.Now;
                    elapsed = TimeSpan.Zero;
                }

                // Se a requisição atual ultrapassar o limite seguro de TPM (ex: 5500)
                if (_tokensCurrentMinute + estimatedTokens > TPM_LIMIT)
                {
                    // Calcula os milissegundos que faltam para completar a janela de 1 minuto
                    waitTimeMs = 60000 - (int)elapsed.TotalMilliseconds;
                    if (waitTimeMs < 0) waitTimeMs = 0;

                    // Projeta os valores para depois da espera
                    _tokensCurrentMinute = estimatedTokens;
                    _minuteStartTime = DateTime.Now.AddMilliseconds(waitTimeMs);
                }
                else
                {
                    // Acumula os tokens no minuto atual
                    _tokensCurrentMinute += estimatedTokens;
                }
            }

            // Se for necessário esperar, chama a rotina de contagem decrescente visual
            if (waitTimeMs > 0)
            {
                await DelayWithCountdownAsync(waitTimeMs);
            }
        }

        private string GetFormattedStatus(string currentAction = "")
        {
            int total = progressBar.Maximum;
            int processados = progressBar.Value;
            int restantes = total - processados;
            string eta = "Calculando...";

            if (processados > 0 && _executionTimer.IsRunning)
            {
                long msDecorridos = _executionTimer.ElapsedMilliseconds;
                long msPorNoticia = msDecorridos / processados;
                TimeSpan tempoRestante = TimeSpan.FromMilliseconds(msPorNoticia * restantes);
                eta = tempoRestante.TotalHours >= 1 ? tempoRestante.ToString(@"hh\:mm\:ss") : tempoRestante.ToString(@"mm\:ss");
            }

            // Montagem da string com ícones
            string dashboard = $"✅ {_successCount} | ♻️ {_duplicateCount} | ⚡ {_cacheHitCount} | 🤖 {_iaErrorCount} | 🌐 {_scrapErrorCount} | 📈 {processados}/{total} | ⏳ ETA: {eta}";

            if (!string.IsNullOrEmpty(currentAction))
                dashboard += $"\n→ {currentAction}";

            if (_iaErrorCount > 0)
                dashboard += $"\n⚠ Último Erro IA: {_lastIaError}";

            return dashboard;
        }

        // Método para Salvar
        private List<TopicResult> ReadLastResults()
        {
            if (!File.Exists(_lastResultsPath))
            {
                return new List<TopicResult>();
            }

            string json = File.ReadAllText(_lastResultsPath);
            return JsonConvert.DeserializeObject<List<TopicResult>>(json) ?? new List<TopicResult>();
        }

        private void SaveLastResults(List<TopicResult> results = null)
        {
            try
            {
                var resultsToSave = results ?? _currentTopicResults;
                string json = JsonConvert.SerializeObject(resultsToSave, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(_lastResultsPath, json);
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao salvar resultados pendentes.", ex);
            }
        }

        // Método para Carregar
        private void MergePendingTopicResults(List<TopicResult> newResults)
        {
            var mergedByUrl = new Dictionary<string, TopicResult>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in ReadLastResults().Where(r => !r.IsClicked && !string.IsNullOrWhiteSpace(r.Url)))
            {
                mergedByUrl[item.Url] = item;
            }

            foreach (var item in newResults.Where(r => !r.IsClicked && !string.IsNullOrWhiteSpace(r.Url)))
            {
                mergedByUrl[item.Url] = item;
            }

            _currentTopicResults = mergedByUrl.Values
                .OrderByDescending(r => r.Score)
                .ToList();

            SaveLastResults(_currentTopicResults);
        }

        private void LoadLastResults()
        {
            try
            {
                _currentTopicResults = ReadLastResults()
                    .Where(r => !r.IsClicked && !string.IsNullOrWhiteSpace(r.Url))
                    .ToList();

                if (_currentTopicResults.Any())
                {
                    DisplayTopicResults(_currentTopicResults);
                    UpdateInfoLabel($"Carregados {_currentTopicResults.Count} resultados nÃ£o lidos da Ãºltima sessÃ£o.");
                }

                if (File.Exists(_lastResultsPath))
                {
                    string json = File.ReadAllText(_lastResultsPath);
                    var allResults = JsonConvert.DeserializeObject<List<TopicResult>>(json) ?? new List<TopicResult>();

                    // Filtra pegando APENAS os que NÃO foram clicados
                    _currentTopicResults = allResults.Where(r => !r.IsClicked).ToList();

                    // Mostra na Grid se tiver algum sobrando
                    if (_currentTopicResults.Any())
                    {
                        DisplayTopicResults(_currentTopicResults);
                        UpdateInfoLabel($"Carregados {_currentTopicResults.Count} resultados não lidos da última sessão.");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao carregar resultados pendentes.", ex);
            }
        }        

        private void MainForm_Load(object sender, EventArgs e)
        {
            LogService.Info(">>> Aplicativo Iniciado. Carregando memórias...");
            ApplyVersionToCaption();
            LoadLastResults();
            LoadEvaluatedCache();
            _summaryCache = SummaryCacheManager.LoadCache();
            UpdateCostLabel();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Isso apagará TODO o histórico de resumos da IA e também limpará a lista de leitura atual da tela. Deseja continuar?",
                "Limpeza Total",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                try
                {
                    // 1. Limpa fisicamente usando a nova classe centralizada
                    SummaryCacheManager.ClearCache();

                    // 2. Limpa a lista na memória do MainForm para fazer efeito imediato
                    if (_summaryCache != null) _summaryCache.Clear();

                    // 3. Limpa a Grid e as pendências atuais
                    if (_currentTopicResults != null) _currentTopicResults.Clear();
                    SaveLastResults(); // Se você já tiver um LastResultsManager, melhor ainda!
                    dgvTopicResults.DataSource = null;

                    UpdateInfoLabel("Sistema e tela totalmente limpos!");
                    MessageBox.Show("Todos os dados e resumos foram apagados!", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erro ao limpar dados: " + ex.Message);
                }
            }
        }

        private void btnLimparCache_Click(object sender, EventArgs e)
        {
            var confirmacao = MessageBox.Show(
                "Isso removerá todos os resultados salvos anteriormente e forçará a IA a reprocessar tudo. Deseja continuar?",
                "Limpar Cache Completo",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (confirmacao == DialogResult.Yes)
            {
                try
                {
                    // 1. Limpa as listas na Memória RAM
                    _evaluatedCache.Clear();
                    _summaryCache.Clear();
                    _currentTopicResults.Clear();
                    _allNewsScores.Clear();

                    // 2. Apaga os arquivos físicos no Disco
                    if (File.Exists(_cachePath)) File.Delete(_cachePath);
                    if (File.Exists(_summaryCachePath)) File.Delete(_summaryCachePath);
                    if (File.Exists(_lastResultsPath)) File.Delete(_lastResultsPath);

                    // 3. Notifica o Log e o Usuário
                    LogService.Info("🧹 Faxina completa realizada! Caches de avaliação e sumários foram removidos.");

                    // Zera os contadores da tela para o próximo processamento parecer "limpo"
                    _processedCount = 0;
                    _successCount = 0;
                    _cacheHitCount = 0;
                    _iaErrorCount = 0;

                    MessageBox.Show("Caches apagados com sucesso! O próximo processamento será 100% novo.", "Sucesso");
                }
                catch (Exception ex)
                {
                    LogService.Error($"Erro ao limpar cache: {ex.Message}");
                    MessageBox.Show("Erro ao apagar arquivos de cache. Verifique se eles não estão abertos em outro programa.");
                }
            }
        }
    }

}
