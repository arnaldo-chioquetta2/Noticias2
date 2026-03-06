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
using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Utils;
using NewsImpactRanker.WinForms.Storage;
using NewsImpactRanker.WinForms.Services;

namespace NewsImpactRanker.WinForms.Forms
{
    public partial class MainForm : Form
    {
        // private bool _limitToFive = true;
        private int _registroLimite = 0;

        private readonly ScrapingService _scrapingService;
        private readonly GroqService _groqService;
        private CancellationTokenSource _cts;        
        private bool _currentExecutionUsesFile = true;
        private readonly List<string> _failedDomains = new List<string>();
        private readonly object _failedDomainsLock = new object();
        private string _lastReportPath;
        private int _processedCount = 0;
        private int _successCount = 0;
        private int _iaErrorCount = 0;
        private int _scrapErrorCount = 0;
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

        // Adicione estas linhas junto com as outras declarações de campo (no topo da classe)
        //private string _lastIaError = "Nenhum";
        private readonly Queue<long> _lastProcessingTimes = new Queue<long>();
        //private readonly Stopwatch _executionTimer = new Stopwatch();

        private readonly GeminiService _geminiService; // Adicione esta linha

        public MainForm()
        {
            InitializeComponent();
            _scrapingService = new ScrapingService();
            // _geminiService = new GeminiService();
            _groqService = new GroqService();
            _geminiService = new GeminiService();
            dgvResults.SortCompare += DgvResults_SortCompare;
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

                // Atualiza a grid de resultados por assunto
                DisplayTopicResults(topicResults);

                // Salva o arquivo de texto com o ranking
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

        // METHOD v8: ProcessUrlsAsync
        private async Task ProcessUrlsAsync(List<string> urls)
        {
            _executionTimer.Restart(); // Inicia cronômetro global para o ETA
            _lastIaError = "Nenhum";
            _lastProcessingTimes.Clear(); // Limpa médias anteriores

            foreach (var url in urls)
            {
                // 1. Verificação de Cancelamento
                if (_cts != null && _cts.IsCancellationRequested) break;

                // Cronômetro individual para esta notícia (usado para calcular a média de tempo)
                Stopwatch itemTimer = Stopwatch.StartNew();

                try
                {
                    UpdateInfoLabel(GetFormattedStatus($"Scraping: {url}"));

                    // 2. Executa o Scraping
                    var scrapedNews = await _scrapingService.ScrapeAsync(url);

                    if (scrapedNews == null || scrapedNews.Status != "Sucesso")
                    {
                        if (scrapedNews?.Status == "Bloqueado" || scrapedNews?.Status == "Sem Conteúdo")
                            _scrapErrorCount++;

                        UpdateProgress(); // Avança barra para não travar o ETA
                        continue;
                    }

                    // 3. Gerenciamento de Limites e Throttling
                    var config = StorageManager.LoadConfig();

                    if (config.SelectedProvider == AiProvider.Groq)
                    {
                        // Groq precisa da pausa de tokens (Prompt1.txt ~2000 tokens de peso)
                        await CheckAndDelayForTokenLimitAsync(scrapedNews.RawText);
                    }
                    else
                    {
                        UpdateInfoLabel(GetFormattedStatus("Gemini processando (sem filas)..."));
                    }

                    // 4. Chamada da Inteligência Artificial (Dinâmica)
                    bool success;
                    dynamic iaData;
                    string errorMsg;

                    if (config.SelectedProvider == AiProvider.Gemini)
                    {
                        var res = await _geminiService.ClassifyNewsAsync(scrapedNews.RawText);
                        success = res.Success;
                        iaData = res.Data;
                        errorMsg = res.ErrorMessage;
                    }
                    else
                    {
                        var res = await _groqService.ClassifyNewsAsync(scrapedNews.RawText);
                        success = res.Success;
                        iaData = res.Data;
                        errorMsg = res.ErrorMessage;
                    }

                    // 5. Processamento do Resultado da IA
                    if (success && iaData != null && iaData.scores != null)
                    {
                        // Conversão segura do dynamic para o Dicionário que o Ranking espera
                        var scores = JsonConvert.DeserializeObject<Dictionary<string, int>>(iaData.scores.ToString());

                        // Método que salva na lista global e atualiza as Grids
                        HandleClassificationSuccess(url, scrapedNews.Title, scores);
                    }
                    else
                    {
                        _lastIaError = errorMsg ?? "Resposta inválida (JSON)";
                        LogService.Warn($"IA falhou para {url}: {_lastIaError}");
                        _iaErrorCount++;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error($"Erro crítico em {url}: {ex.Message}");
                    _lastIaError = ex.Message;
                    _iaErrorCount++;
                }
                finally
                {
                    // 6. Finalização da Rodada e Cálculo de Tempo
                    itemTimer.Stop();

                    lock (_lastProcessingTimes)
                    {
                        _lastProcessingTimes.Enqueue(itemTimer.ElapsedMilliseconds);
                        if (_lastProcessingTimes.Count > 10) _lastProcessingTimes.Dequeue();
                    }

                    UpdateProgress();    // Atualiza barra visual
                    UpdateStatusLabel(); // Atualiza contadores numéricos
                }

                // Pausa fixa de "respiro" para a interface e API
                await Task.Delay(2000);
            }

            _executionTimer.Stop();
            UpdateInfoLabel(GetFormattedStatus("Processamento finalizado!"));
        }
        // METHOD v1: HandleClassificationSuccess
        // Este é o método que processa o resultado positivo da IA (substitui o que você chamou de ProcessClassificationResult)
        private void HandleClassificationSuccess(string url, string title, Dictionary<string, int> scores)
        {
            lock (_scoresLock)
            {
                // Criamos o objeto de Score para o Ranking
                var item = new NewsScoresItem
                {
                    Url = url,
                    Title = title,
                    Scores = scores,
                    SourceOrder = _allNewsScores.Count
                };

                // Evita duplicatas na lista global
                if (!_allNewsScores.Any(n => n.Url == url))
                {
                    _allNewsScores.Add(item);
                    _successCount++;
                }
            }

            // Atualiza a UI (Labels e Grids)
            UpdateStatusLabel();

            // Recalcula os vencedores e atualiza a grid de tópicos em tempo real
            var partialResults = SelectBestNewsPerTopic();
            DisplayTopicResults(partialResults);

            LogService.Info($"Notícia classificada com sucesso: {url}");
        }

        private async Task<NewsScoresItem> ProcessSingleUrlAsync(string url)
        {
            LogService.Info("METHOD v3: ProcessSingleUrlAsync");

            try
            {
                // 1️⃣ Scraping
                var scraped = await _scrapingService.ScrapeAsync(url);

                if (scraped.Status != "Sucesso")
                {
                    LogService.Warn($"Falha no scraping: {url}");
                    _scrapErrorCount++;
                    UpdateStatusLabel();
                    return null;
                }

                // 2️⃣ IA
                var responseResult = await _groqService.ClassifyNewsAsync(scraped.RawText);

                if (!responseResult.Success)
                {
                    string errorMessage = $"{responseResult.ErrorMessage}\n\nConsultas processadas antes do erro: {_successCount}";
                    LogService.Error($"Erro na IA: {errorMessage}");
                    MessageBox.Show(errorMessage, "Limite de Uso Atingido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _iaErrorCount++;
                    UpdateStatusLabel();
                    Application.Exit();
                    return null;
                }

                if (responseResult.Data == null || responseResult.Data.scores == null)
                {
                    LogService.Warn($"IA retornou resposta inválida (scores nulo): {url}");
                    _iaErrorCount++;
                    UpdateStatusLabel();
                    return null;
                }

                NewsScoresItem item;

                lock (_scoresLock)
                {
                    item = new NewsScoresItem
                    {
                        Url = url,
                        Title = scraped.Title,
                        Scores = responseResult.Data.scores,
                        SourceOrder = _allNewsScores.Count
                    };

                    if (!_allNewsScores.Any(n => n.Url == item.Url))
                    {
                        _allNewsScores.Add(item);
                        LogService.Info($"DEBUG: notícia adicionada {_allNewsScores.Count}");
                    }
                    else
                    {
                        LogService.Warn($"DEBUG: duplicata ignorada {url}");
                    }
                }

                LogService.Info($"Notícia classificada: {url}");
                _successCount++;
                UpdateStatusLabel();

                // 4️⃣ Recalcular ranking parcial
                var partialResults = SelectBestNewsPerTopic();

                // 5️⃣ Atualizar grid em tempo real
                DisplayTopicResults(partialResults);

                return item;
            }
            catch (Exception ex)
            {
                LogService.Error($"Erro ao processar URL {url}", ex);
                _iaErrorCount++;
                UpdateStatusLabel();
                return null;
            }
        }

        private void UpdateStatusLabel()
        {
            lblInfo.Text = $"Sucesso: {_successCount} | Erro de IA: {_iaErrorCount} | Erro de Scrap: {_scrapErrorCount}";
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
            return results;
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
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            if (dgvTopicResults.Columns[e.ColumnIndex].Name != "colTopicUrl")
                return;

            try
            {
                var row = dgvTopicResults.Rows[e.RowIndex];
                string url = row.Cells[e.ColumnIndex].Value?.ToString();

                if (string.IsNullOrWhiteSpace(url))
                    return;

                // 1️⃣ copiar para clipboard
                Clipboard.SetText(url);

                // 2️⃣ pintar linha
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 205); // laranja claro

                // 3️⃣ registrar log
                LogService.Info($"URL copiada do ranking: {url}");

                // 4️⃣ remover do arquivo se a execução veio de arquivo
                if (_currentExecutionUsesFile)
                {
                    RemoveUrlFromConfiguredFile(url);
                }

                // feedback visual
                var cell = row.Cells[e.ColumnIndex];
                var originalValue = cell.Value;
                var originalColor = cell.Style.ForeColor;

                cell.Value = "✓ Copiado!";
                cell.Style.ForeColor = Color.Green;

                var timer = new System.Windows.Forms.Timer();
                timer.Interval = 1500;

                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    timer.Dispose();

                    if (!this.IsDisposed)
                    {
                        cell.Value = originalValue;
                        cell.Style.ForeColor = originalColor;
                    }
                };

                timer.Start();
            }
            catch (Exception ex)
            {
                LogService.Error("Erro ao copiar URL da grid de tópicos", ex);

                MessageBox.Show(
                    "Não foi possível copiar o link.",
                    "Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
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

        // METHOD v5: GetFormattedStatus
        private string GetFormattedStatus(string currentAction = "")
        {
            int total = progressBar.Maximum;
            int processados = progressBar.Value;
            int restantes = total - processados;

            string eta = "Calculando...";

            // Só calcula ETA após processar pelo menos uma notícia para ter uma média real
            if (processados > 0 && _executionTimer.IsRunning)
            {
                long msDecorridos = _executionTimer.ElapsedMilliseconds;
                long msPorNoticia = msDecorridos / processados;
                TimeSpan tempoRestante = TimeSpan.FromMilliseconds(msPorNoticia * restantes);

                eta = tempoRestante.TotalHours >= 1
                    ? tempoRestante.ToString(@"hh\:mm\:ss")
                    : tempoRestante.ToString(@"mm\:ss");
            }

            // Dashboard completo com Estatísticas e Último Erro
            string dashboard = $"✅ {_successCount} | 🤖 {_iaErrorCount} | 🌐 {_scrapErrorCount} | 📈 {processados}/{total} | ⏳ Faltam: {eta}";

            if (!string.IsNullOrEmpty(currentAction))
                dashboard += $"\n→ Status: {currentAction}";

            if (_iaErrorCount > 0)
                dashboard += $"\n⚠ Último Erro IA: {_lastIaError}";

            return dashboard;
        }

    }
}