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
using NewsImpactRanker.WinForms.Utils;
using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Storage;
using NewsImpactRanker.WinForms.Services;

namespace NewsImpactRanker.WinForms.Forms
{
    public partial class MainForm : Form
    {

#if DEBUG
        private int _registroLimite = 5;
#else
        private int _registroLimite = 0;
#endif

        private string _lastResultsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NewsRanking_LastResults.json"); 
        private string _cachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NewsRanking_EvaluatedCache.json");
        private List<TopicResult> _currentTopicResults = new List<TopicResult>();
        private Dictionary<string, NewsScoresItem> _evaluatedCache = new Dictionary<string, NewsScoresItem>();

        private readonly ScrapingService _scrapingService;
        private readonly GroqService _groqService;
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

        // 👉 VARIÁVEIS DO FILTRO ANTI-DUPLICIDADE
        private string _summaryCachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NewsRanking_SummaryCache.json");
        private List<SummaryCacheItem> _summaryCache = new List<SummaryCacheItem>();


        private int _processedCount = 0;
        private int _successCount = 0;
        private int _iaErrorCount = 0;
        private int _scrapErrorCount = 0;
        private int _cacheHitCount = 0;
        //private int _duplicateCount = 0;

        public MainForm()
        {
            InitializeComponent();
            _scrapingService = new ScrapingService();
            _groqService = new GroqService();
            _geminiService = new GeminiService();
            dgvResults.SortCompare += DgvResults_SortCompare;
            LoadLastResults();
            LoadEvaluatedCache();

        }

        private void LoadEvaluatedCache()
        {
            try
            {
                if (File.Exists(_cachePath))
                {
                    string json = File.ReadAllText(_cachePath);
                    _evaluatedCache = JsonConvert.DeserializeObject<Dictionary<string, NewsScoresItem>>(json)
                                      ?? new Dictionary<string, NewsScoresItem>();

                    LogService.Info($"[CACHE] Memória carregada: {_evaluatedCache.Count} avaliações anteriores prontas para reuso.");
                }
            }
            catch (Exception ex)
            {
                LogService.Error("[CACHE] Erro ao carregar memória de avaliações.", ex);
                _evaluatedCache = new Dictionary<string, NewsScoresItem>();
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

            // Reseta logs e contadores globais
            LogService.ResetLog();
            _successCount = 0;
            _iaErrorCount = 0;
            _scrapErrorCount = 0;
            _cacheHitCount = 0;

            LogService.Info("=== Processamento iniciado ===");

            // 1. Validação Inteligente de API Key conforme o provedor selecionado
            bool keyConfigurada = config.SelectedProvider == AiProvider.Gemini
                ? !string.IsNullOrEmpty(config.GeminiApiKey)
                : !string.IsNullOrEmpty(config.AiApiKey);

            if (!keyConfigurada)
            {
                string provedor = config.SelectedProvider == AiProvider.Gemini ? "Gemini" : "Groq";
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
            dgvResults.Rows.Clear();
            dgvTopicResults.Rows.Clear();

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
                var topicResults = SelectBestNewsPerTopic();

                // ---> INTEGRAÇÃO COM LAST RESULTS (JSON) <---
                // 1. Atualiza a variável de memória global para o controle de cliques
                _currentTopicResults = topicResults;

                // 2. Salva o status "IsClicked = false" no disco imediatamente
                SaveLastResults();

                // 3. Atualiza a grid de resultados por assunto
                DisplayTopicResults(_currentTopicResults);

                // 4. Salva o arquivo de texto com o ranking
                SaveFinalRankingToFile(topicResults);

                LogService.Info($"Total de tópicos preenchidos: {topicResults.Count}");
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
                // Libera a interface novamente
                ToggleUI(true);
            }
        }

        //////

        private async Task ProcessUrlsAsync(List<string> urls)
        {
            _executionTimer.Restart();
            _lastIaError = "Nenhum";
            _lastProcessingTimes.Clear();

            foreach (var url in urls)
            {
                if (_cts != null && _cts.IsCancellationRequested) break;

                Stopwatch itemTimer = Stopwatch.StartNew();

                try
                {
                    // Chama o trabalhador secundário para processar 1 URL por vez
                    await ProcessSingleUrlAsync(url);
                }
                catch (Exception ex)
                {
                    LogService.Error($"Erro crítico em {url}: {ex.Message}");
                    _lastIaError = ex.Message;
                    _iaErrorCount++;
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

                await Task.Delay(2000); // Respiro da interface
            }

            _executionTimer.Stop();
            UpdateInfoLabel(GetFormattedStatus("Processamento finalizado!"));
        }

        private async Task ProcessSingleUrlAsync(string url)
        {
            // 👉 ETAPA A: CACHE DE URL
//#if DEBUG
//            bool isCached = false;
//#else
            bool isCached = _evaluatedCache != null && _evaluatedCache.ContainsKey(url);
// #endif

            if (isCached)
            {
                UpdateInfoLabel(GetFormattedStatus($"Recuperando da Memória: {url}"));
                var cachedItem = _evaluatedCache[url];
                HandleClassificationSuccess(url, cachedItem.Title, cachedItem.Scores, cachedItem.Summary, true);
                LogService.Info($"[CACHE HIT] URL já conhecida: {url}");
                _cacheHitCount++;
                return; // Termina o processamento desta URL aqui
            }

            // 👉 ETAPA B: SCRAPING
            UpdateInfoLabel(GetFormattedStatus($"Scraping: {url}"));
            var scrapedNews = await _scrapingService.ScrapeAsync(url);

            if (scrapedNews == null || scrapedNews.Status != "Sucesso")
            {
                if (scrapedNews?.Status == "Bloqueado" || scrapedNews?.Status == "Sem Conteúdo")
                    _scrapErrorCount++;
                return;
            }

            // Se o scraping funcionou, envia o Texto e o Título para a IA (Método 3)
            await ExecuteAiAndFilterAsync(url, scrapedNews.RawText, scrapedNews.Title);
        }

        private async Task ExecuteAiAndFilterAsync(string url, string rawText, string title)
        {
            var config = StorageManager.LoadConfig();

            if (config.SelectedProvider == AiProvider.Groq)
                await CheckAndDelayForTokenLimitAsync(rawText);
            else
                UpdateInfoLabel(GetFormattedStatus("Gemini processando..."));

            bool success = false;
            dynamic iaData = null;
            string errorMsg = "";

            // 👉 ETAPA C: IA COM RETRY DE 3x
            for (int i = 1; i <= 3; i++)
            {
                if (config.SelectedProvider == AiProvider.Gemini)
                {
                    var res = await _geminiService.ClassifyNewsAsync(rawText);
                    success = res.Success; iaData = res.Data; errorMsg = res.ErrorMessage;
                }
                else
                {
                    var res = await _groqService.ClassifyNewsAsync(rawText);
                    success = res.Success; iaData = res.Data; errorMsg = res.ErrorMessage;
                }

                if (success && iaData != null) break; // Sai do loop se deu certo
                if (i < 3) await Task.Delay(2000); // Pausa antes de tentar de novo
            }

            // 👉 ETAPA D: FILTRO ANTI-DUPLICIDADE SEMÂNTICA
            if (success && iaData != null && iaData.scores != null)
            {
                Dictionary<string, int> scores = JsonConvert.DeserializeObject<Dictionary<string, int>>(iaData.scores.ToString());
                string summary = iaData.summary != null ? iaData.summary.ToString().Trim().ToLower() : "";
                var topCategory = scores.OrderByDescending(x => x.Value).FirstOrDefault();

                if (topCategory.Value > 0 && !string.IsNullOrWhiteSpace(summary))
                {
                    bool isDuplicate = _summaryCache.Any(s =>
                        s.Summary.Equals(summary, StringComparison.OrdinalIgnoreCase) &&
                        s.TopCategory.Equals(topCategory.Key, StringComparison.OrdinalIgnoreCase));

                    if (isDuplicate)
                    {
                        LogService.Info($"[♻️ DEDUPLICAÇÃO] Notícia repetida ignorada por resumo: '{summary}'");
                        _duplicateCount++;
                        if (_currentExecutionUsesFile) RemoveUrlFromConfiguredFile(url);
                        return; // Descarta e sai
                    }
                }

                // 👉 ETAPA E: SUCESSO FINAL
                HandleClassificationSuccess(url, title, scores, summary, false);
            }
            else
            {
                _lastIaError = errorMsg ?? "Resposta inválida (JSON)";
                LogService.Warn($"IA falhou para {url}: {_lastIaError}");
                _iaErrorCount++;
            }
        }

        /// <summary>
        /// Processa o sucesso de uma classificação, seja vinda da IA ou do Cache.
        /// </summary>
        private void HandleClassificationSuccess(string url, string title, Dictionary<string, int> scores, string summary, bool fromCache = false)
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
                    SourceOrder = _allNewsScores.Count
                };

                if (!_allNewsScores.Any(n => n.Url == url))
                {
                    _allNewsScores.Add(item);
                    _successCount++;
                }
            }

            // 2. Persistência no Cache de IA
            if (!fromCache)
            {
                _evaluatedCache[url] = item;
                SaveEvaluatedCache();
            }

            UpdateStatusLabel();

            // 4. Recálculo do Ranking e 👉 ORDENAÇÃO POR SCORE 👈
            // Aqui é onde garantimos que o 85 suba e o 55 desça
            var partialResults = SelectBestNewsPerTopic()
                                 .OrderByDescending(r => r.Score) // Ordena do maior para o menor
                                 .ToList();

            // 5. Preservação do estado de "Lido" (IsClicked)
            foreach (var novoResultado in partialResults)
            {
                var antigo = _currentTopicResults.FirstOrDefault(r => r.Url == novoResultado.Url && r.Topic == novoResultado.Topic);
                if (antigo != null)
                {
                    novoResultado.IsClicked = antigo.IsClicked;
                }
            }

            // 6. Atualiza a lista global e salva o estado
            _currentTopicResults = partialResults;
            SaveLastResults();

            // 7. Atualiza a Grid (apenas o que não foi lido)
            var itensParaMostrar = _currentTopicResults.Where(r => !r.IsClicked).ToList();
            DisplayTopicResults(itensParaMostrar);

            if (!fromCache)
            {
                LogService.Info($"[OK] Notícia classificada e reordenada: {url}");
            }
        }

        private void SaveEvaluatedCache()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_evaluatedCache, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(_cachePath, json);
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

        private void SaveFinalRankingToFile(List<TopicResult> results)
        {
            // METHOD v2: SaveFinalRankingToFile
            // Alteração: Inclusão da seção de Tópicos Monitorados e estatísticas por categoria.
            try
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                _lastReportPath = Path.Combine(
                    folder,
                    $"NewsRanking_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                );

                var lines = new List<string>();

                lines.Add("================================================");
                lines.Add("         NEWS TOPIC RANKING REPORT              ");
                lines.Add("================================================");
                lines.Add($"Data: {DateTime.Now}");
                lines.Add($"IA Utilizada: {StorageManager.LoadConfig().SelectedProvider}");
                lines.Add("");

                // --- NOVA SEÇÃO: TÓPICOS MONITORADOS ---
                lines.Add("===== TÓPICOS MONITORADOS =====");
                if (_allNewsScores.Any())
                {
                    // Pegamos as chaves (nomes dos tópicos) da primeira notícia com sucesso
                    var allTopics = _allNewsScores.First().Scores.Keys.OrderBy(t => t).ToList();
                    foreach (var topicName in allTopics)
                    {
                        // Conta quantas notícias bateram nesse tópico (score > 0)
                        int count = _allNewsScores.Count(n => n.Scores.ContainsKey(topicName) && n.Scores[topicName] > 0);
                        lines.Add($"- {topicName.PadRight(30)} ({count} notícias encontradas)");
                    }
                }
                else
                {
                    lines.Add("Nenhum tópico processado.");
                }
                lines.Add("");

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
                lines.Add("===== RESUMO DA EXECUÇÃO =====");
                lines.Add($"Total de URLs analisadas      : {_allNewsScores.Count + _iaErrorCount + _scrapErrorCount}");
                lines.Add($"Sucessos de Classificação     : {_allNewsScores.Count}");
                lines.Add($"Falhas de IA (🤖)            : {_iaErrorCount}");
                lines.Add($"Falhas de Scraping (🌐)       : {_scrapErrorCount}");
                lines.Add($"Tópicos com match no Ranking  : {results.Count}");
                lines.Add("");

                lines.Add("===== RESUMO DA EXECUÇÃO =====");
                lines.Add($"Total de URLs processadas     : {_allNewsScores.Count + _iaErrorCount + _scrapErrorCount}");
                lines.Add($"Sucessos Totais (IA + Cache)  : {_successCount}");
                lines.Add($"   -> Desse total, via Cache  : {_cacheHitCount} ⚡"); // 👉 NOVO NO TXT
                lines.Add($"Falhas de IA (🤖)             : {_iaErrorCount}");
                lines.Add($"Falhas de Scraping (🌐)       : {_scrapErrorCount}");
                lines.Add($"Tópicos com match no Ranking  : {results.Count}");
                lines.Add("");

                // ... dentro do método SaveFinalRankingToFile ...
                lines.Add("");
                lines.Add("===== RESUMO DA EXECUÇÃO =====");
                lines.Add($"Total de URLs Processadas       : {progressBar.Value}");
                lines.Add($"Sucessos de Ranking (Inéditas)  : {_successCount - _cacheHitCount}");
                lines.Add($"Reaproveitadas via Cache (⚡)   : {_cacheHitCount}");
                lines.Add($"Descartadas por Duplicidade (♻️) : {_duplicateCount}"); // 👉 NOVO
                lines.Add($"Falhas de IA (🤖)               : {_iaErrorCount}");
                lines.Add($"Falhas de Scraping (🌐)         : {_scrapErrorCount}");
                lines.Add($"Tempo Total de Execução         : {_executionTimer.Elapsed:hh\\:mm\\:ss}");
                lines.Add("");

                // --- DOMÍNIOS COM FALHA ---
                lock (_failedDomainsLock)
                {
                    if (_failedDomains.Any())
                    {
                        lines.Add("===== DOMÍNIOS COM PROBLEMAS (HTTP 403/404/Timeout) =====");
                        foreach (var d in _failedDomains.Distinct())
                            lines.Add($"❌ {d}");
                    }
                }

                File.WriteAllLines(_lastReportPath, lines);
                LogService.Info($"Relatório completo salvo em: {_lastReportPath}");
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao gerar relatório final", ex);
            }
        }

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

        private List<TopicResult> SelectBestNewsPerTopic()
        {
            LogService.Info("METHOD v2: SelectBestNewsPerTopic");
            LogService.Info("DEBUG: Entrou em SelectBestNewsPerTopic");

            var results = new List<TopicResult>();

            // cópia do que já foi classificado
            List<NewsScoresItem> availableNews;
            lock (_scoresLock)
            {
                availableNews = new List<NewsScoresItem>(_allNewsScores);
            }

            LogService.Info($"DEBUG: availableNews inicial = {availableNews.Count}");

            if (availableNews.Count == 0)
                return results;

            // Se nenhuma notícia tem qualquer score > 0, não faz sentido selecionar
            int maxAny = 0;
            foreach (var n in availableNews)
            {
                if (n?.Scores == null) continue;
                foreach (var v in n.Scores.Values)
                    if (v > maxAny) maxAny = v;
            }

            if (maxAny <= 0)
            {
                LogService.Info("DEBUG: Nenhuma notícia possui score > 0 em qualquer tópico.");
                return results;
            }

            foreach (var code in NewsImpactRanker.WinForms.Models.TopicCatalog.Codes)
            {
                if (availableNews.Count == 0)
                {
                    LogService.Info("DEBUG: Não há mais notícias disponíveis.");
                    break;
                }

                var topicName = NewsImpactRanker.WinForms.Models.TopicCatalog.CodeToName.ContainsKey(code)
                    ? NewsImpactRanker.WinForms.Models.TopicCatalog.CodeToName[code]
                    : code;

                // acha a melhor notícia para este tópico (por CÓDIGO)
                var ranked = availableNews
                    .Select(n => new
                    {
                        News = n,
                        Score = (n.Scores != null && n.Scores.ContainsKey(code)) ? n.Scores[code] : 0,
                        Total = (n.Scores != null) ? n.Scores.Values.Sum() : 0,
                        Order = n.SourceOrder
                    })
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Total)   // desempate: soma total
                    .ThenBy(x => x.Order)             // depois: ordem original
                    .FirstOrDefault();

                if (ranked == null)
                    continue;

                // REGRA NOVA: se o melhor score deste tópico é 0, NÃO seleciona e NÃO consome URL
                if (ranked.Score <= 0)
                {
                    // só loga e segue para o próximo tópico
                    LogService.Info($"DEBUG: tópico {topicName} ignorado (bestScore=0)");
                    continue;
                }

                results.Add(new TopicResult
                {
                    Topic = topicName,
                    Url = ranked.News.Url,
                    Score = ranked.Score
                });

                LogService.Info($"DEBUG: selecionada {ranked.News.Url} para {topicName} (score={ranked.Score})");

                // remove a notícia para não ser usada em outro assunto
                availableNews.Remove(ranked.News);

                LogService.Info($"DEBUG: remainingNews = {availableNews.Count}");
            }

            LogService.Info($"DEBUG: topicResults.Count = {results.Count}");

            // return results;
            return results.OrderByDescending(r => r.Score).ToList();

        }

        private void DisplayTopicResults(List<TopicResult> results)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => DisplayTopicResults(results)));
                return;
            }

            dgvTopicResults.Rows.Clear();

            foreach (var r in results)
            {
                dgvTopicResults.Rows.Add(
                    r.Topic,
                    r.Url,
                    r.Score
                );
            }
            LogService.Info($"DEBUG GRID NAME: {dgvTopicResults.Name}");
            LogService.Info($"DEBUG: Grid atualizada com {dgvTopicResults.Rows.Count} linhas");
        }

        private void dgvTopicResults_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            if (dgvTopicResults.Columns[e.ColumnIndex].Name != "colTopicUrl") return;

            try
            {
                var row = dgvTopicResults.Rows[e.RowIndex];
                string url = row.Cells[e.ColumnIndex].Value?.ToString();

                if (string.IsNullOrWhiteSpace(url)) return;

                // 1. Copiar para a área de transferência
                Clipboard.SetText(url);

                // 2. Marcar como lido e Salvar no Cache de Resumos
                var item = _currentTopicResults.FirstOrDefault(r => r.Url == url);
                if (item != null)
                {
                    item.IsClicked = true;
                    SaveLastResults();

                    // 👉 CORREÇÃO: Buscamos a notícia completa no Cache de Avaliações
                    // É lá que o 'Summary' e o dicionário 'Scores' completo residem
                    if (_evaluatedCache.TryGetValue(url, out var fullNewsItem))
                    {
                        if (!string.IsNullOrWhiteSpace(fullNewsItem.Summary) && fullNewsItem.Scores != null)
                        {
                            // Descobre a categoria principal para parear com o resumo
                            var topCategory = fullNewsItem.Scores.OrderByDescending(x => x.Value).FirstOrDefault();

                            // Checa se já não está no cache de resumos para não duplicar
                            bool alreadyInCache = _summaryCache.Any(s =>
                                s.Summary.Equals(fullNewsItem.Summary, StringComparison.OrdinalIgnoreCase) &&
                                s.TopCategory.Equals(topCategory.Key, StringComparison.OrdinalIgnoreCase));

                            if (!alreadyInCache && topCategory.Value > 0)
                            {
                                _summaryCache.Add(new SummaryCacheItem
                                {
                                    Summary = fullNewsItem.Summary,
                                    TopCategory = topCategory.Key,
                                    DateAdded = DateTime.Now
                                });

                                SaveSummaryCache(); // Salva o JSON do cache de resumos
                                LogService.Info($"[💾 CACHE SALVO] Resumo memorizado após clique: '{fullNewsItem.Summary}'");
                            }
                        }
                    }
                }

                if (_currentExecutionUsesFile)
                {
                    RemoveUrlFromConfiguredFile(url);
                }

                // 3. Feedback Visual (Pinta a linha de laranja claro)
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 205);

                // 4. Feedback Visual Temporário (Célula vira "Copiado!")
                var cell = row.Cells[e.ColumnIndex];
                var originalValue = cell.Value;
                var originalColor = cell.Style.ForeColor;

                cell.Value = "✓ Copiado!";
                cell.Style.ForeColor = Color.Green;

                var timer = new System.Windows.Forms.Timer { Interval = 1500 };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    if (!this.IsDisposed)
                    {
                        try { cell.Value = originalValue; cell.Style.ForeColor = originalColor; } catch { }
                        UpdateInfoLabel(GetFormattedStatus($"Restam {_currentTopicResults.Count(r => !r.IsClicked)} pendentes"));
                    }
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao processar clique na URL do ranking", ex);
            }
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
            int estimatedTokens = (textToProcess.Length / 4) + 2000;

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
        private void SaveLastResults()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_currentTopicResults, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(_lastResultsPath, json);
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao salvar resultados pendentes.", ex);
            }
        }

        // Método para Carregar
        private void LoadLastResults()
        {
            try
            {
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


        private void LoadSummaryCache()
        {
            try
            {
                if (File.Exists(_summaryCachePath))
                {
                    string json = File.ReadAllText(_summaryCachePath);
                    _summaryCache = JsonConvert.DeserializeObject<List<SummaryCacheItem>>(json) ?? new List<SummaryCacheItem>();

                    LogService.Info($"[ANTI-DUPLICIDADE] Memória carregada: {_summaryCache.Count} resumos históricos prontos para o filtro.");
                }
                else
                {
                    LogService.Info("[ANTI-DUPLICIDADE] Arquivo de cache de resumos não encontrado (será criado na primeira duplicata ou sucesso).");
                }
            }
            catch (Exception ex)
            {
                LogService.Error("[ANTI-DUPLICIDADE] Erro ao carregar cache de resumos.", ex);
                _summaryCache = new List<SummaryCacheItem>();
            }
        }

        private void SaveSummaryCache()
        {
            try
            {
                // Opcional: Aqui no futuro você pode colocar um código para apagar resumos mais velhos que 30 dias!
                string json = JsonConvert.SerializeObject(_summaryCache, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(_summaryCachePath, json);
            }
            catch (Exception ex)
            {
                LogService.Error("[ANTI-DUPLICIDADE] Erro crítico ao salvar cache de resumos.", ex);
            }
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            LogService.Info(">>> Aplicativo Iniciado. Carregando memórias...");
            LoadLastResults();
            LoadEvaluatedCache();

            // 👉 CARREGA O NOVO CACHE AQUI:
            LoadSummaryCache();
        }
    }
}