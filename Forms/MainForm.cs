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
        private int _registroLimite = 10;
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
        // Cronômetro para saber até que horas o Groq deve ficar "de castigo"
        private DateTime _groqCooldownUntil = DateTime.MinValue;

        private int _groqSuccessCount = 0;
        private int _geminiSuccessCount = 0;

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
#if DEBUG
            bool isCached = false;
#else
            bool isCached = _evaluatedCache != null && _evaluatedCache.ContainsKey(url);
#endif

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
                        s.Summary.Equals(summary, StringComparison.OrdinalIgnoreCase) &&
                        s.TopCategory.Equals(topCategory.Key, StringComparison.OrdinalIgnoreCase));

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

                HandleClassificationSuccess(url, title, scores, summary, false);
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

                // Exemplo de como adicionar no seu StringBuilder (sb) ou texto do relatório:
                lines.Add("=== RESUMO DE PROCESSAMENTO ===");
                lines.Add($"Total de Sucessos: {_successCount}");
                lines.Add($"- Processados pelo Groq: {_groqSuccessCount}");
                lines.Add($"- Processados pelo Gemini: {_geminiSuccessCount}");
                lines.Add($"- Recuperados da Memória (Cache): {_cacheHitCount}");
                lines.Add($"Falhas de IA: {_iaErrorCount}");
                lines.Add($"Falhas de Scraping: {_scrapErrorCount}");
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
            var topicResults = new List<TopicResult>();

            // 1. Navegamos pelos códigos oficiais do seu TopicCatalog
            foreach (var sigla in TopicCatalog.Codes)
            {
                // 2. Filtramos a notícia vencedora para esta sigla
                var winner = _allNewsScores
                    .Where(n => n.Scores.ContainsKey(sigla) && n.Scores[sigla] > 0)
                    // REGRA DE OURO: A notícia só aparece na categoria onde teve sua nota MÁXIMA
                    .Where(n => n.Scores.OrderByDescending(x => x.Value).FirstOrDefault().Key == sigla)
                    .OrderByDescending(n => n.Scores[sigla])
                    .FirstOrDefault();

                // 3. Se encontrou um vencedor, adiciona à Grid traduzindo o nome
                if (winner != null)
                {
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
                Name = "colTopicUrl",
                HeaderText = "URL",
                DataPropertyName = "Url",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });

            // Estética adicional
            dgvTopicResults.RowHeadersVisible = false;
            dgvTopicResults.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvTopicResults.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 240, 240); // Linhas alternadas para facilitar leitura

            dgvTopicResults.DataSource = results;
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

                                SummaryCacheManager.SaveCache(_summaryCache);
                                // SaveCache(); // Salva o JSON do cache de resumos
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

        private void MainForm_Load(object sender, EventArgs e)
        {
            LogService.Info(">>> Aplicativo Iniciado. Carregando memórias...");
            LoadLastResults();
            LoadEvaluatedCache();

            // 👉 CARREGA O NOVO CACHE AQUI:
            SummaryCacheManager.LoadCache();
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

    }
}