using System;
using System.IO;
using System.Data;
using System.Linq;
using System.Drawing;
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
        private readonly ScrapingService _scrapingService;
        private readonly GroqService _groqService;
        private CancellationTokenSource _cts;
        private bool _limitToFive = true;   // 🔥 mude para false quando quiser liberar geral
        private bool _currentExecutionUsesFile = true;

        // ✅ NOVO: Lista para rastrear falhas de scraping por domínio
        private readonly List<string> _failedDomains = new List<string>();
        private readonly object _failedDomainsLock = new object();
        private string _lastReportPath;

        private readonly object _newsScoresLock = new object();

        private readonly object _scoresLock = new object();
        private List<NewsScoresItem> _allNewsScores = new List<NewsScoresItem>();

        public MainForm()
        {
            InitializeComponent();
            _scrapingService = new ScrapingService();
            // _geminiService = new GeminiService();
            _groqService = new GroqService();

            dgvResults.SortCompare += DgvResults_SortCompare;

            // ✅ Log de inicialização
            //LogService.Info("=== NewsImpactRanker Iniciado ===");
            //LogService.Info($"Config path: {StorageManager.ConfigPath}");
            //LogService.Info($"Cache path: {StorageManager.CachePath}");
            //LogService.Info($"Logs path: {StorageManager.LogsPath}");
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
            var config = StorageManager.LoadConfig();
            LogService.ResetLog();
            LogService.Info("=== Processamento iniciado ===");


            if (string.IsNullOrEmpty(config.AiApiKey))
            {
                MessageBox.Show("Configure a API Key antes de iniciar.",
                    "Aviso",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                btnConfig_Click(null, null);
                return;
            }

            List<string> validUrls;
            bool usingFile = false;

            try
            {
                // 🔹 Ler URLs da fonte (campo ou arquivo)
                validUrls = LoadUrlsFromSource(config);

                // detectar se veio do arquivo
                var typedUrls = txtUrls.Lines
                    .Select(l => l.Trim())
                    .Where(l => UrlValidator.IsValid(l))
                    .Distinct()
                    .ToList();

                usingFile = !typedUrls.Any();

                if (_limitToFive)
                    validUrls = validUrls.Take(5).ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message,
                    "Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (validUrls.Count == 0)
            {
                MessageBox.Show("Nenhuma URL válida encontrada.",
                    "Aviso",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _currentExecutionUsesFile = usingFile;

            LogService.Info($"URLs carregadas: {validUrls.Count}" +
                (_limitToFive ? " (modo limite 5 ativo)" : ""));

            if (usingFile)
            {
                LogService.Info($"Arquivo utilizado: {config.NewsFilePath}");
            }
            else
            {
                LogService.Info("Modo: URLs digitadas manualmente");
            }

            // 🔹 limpar resultados anteriores
            _allNewsScores.Clear();

            // 🔹 Resetar falhas
            lock (_failedDomainsLock)
            {
                _failedDomains.Clear();
            }

            ToggleUI(false);

            dgvResults.Rows.Clear();
            dgvTopicResults.Rows.Clear();

            progressBar.Maximum = validUrls.Count;
            progressBar.Value = 0;
            lblProgress.Text = $"0/{validUrls.Count}";

            _cts = new CancellationTokenSource();

            int parallelism = (int)nudParallelism.Value;

            if (parallelism > 2)
            {
                LogService.Info($"Paralelismo reduzido de {parallelism} para 2 (API Free Tier)");
                parallelism = 2;
            }

            try
            {
                // 🔹 processamento das URLs
                await ProcessUrlsAsync(validUrls, parallelism, _cts.Token);

                LogService.Info($"Total de notícias classificadas: {_allNewsScores.Count}");

                // 🔹 selecionar melhor notícia por assunto
                var topicResults = SelectBestNewsPerTopic();

                // 🔹 mostrar resultados na nova grid
                DisplayTopicResults(topicResults);

                // 🔹 salvar relatório final
                SaveFinalRankingToFile(topicResults);

                LogService.Info($"Total de tópicos selecionados: {topicResults.Count}");
            }
            catch (OperationCanceledException)
            {
                LogService.Info("Processamento cancelado pelo usuário.");

                MessageBox.Show("Processamento cancelado.",
                    "Informação",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogService.Error("Erro fatal no processamento", ex);

                MessageBox.Show($"Ocorreu um erro: {ex.Message}",
                    "Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                ToggleUI(true);
            }
        }

        private async Task ProcessUrlsAsync(List<string> urls, int parallelism, CancellationToken ct)
        {
            LogService.Info("METHOD v2: ProcessUrlsAsync");

            using (var semaphore = new SemaphoreSlim(parallelism))
            {
                var tasks = urls.Select(async url =>
                {
                    await semaphore.WaitAsync(ct);

                    try
                    {
                        if (ct.IsCancellationRequested) return;

                        // 1) Processa a URL e retorna o item (SEM adicionar em _allNewsScores lá dentro)
                        var item = await ProcessSingleUrlAsync(url);

                        if (item == null)
                            return;

                        bool added = false;

                        // 2) Armazena uma única vez (evita duplicação)
                        lock (_scoresLock)
                        {
                            if (!_allNewsScores.Any(n => n.Url == item.Url))
                            {
                                item.SourceOrder = _allNewsScores.Count;
                                _allNewsScores.Add(item);
                                added = true;
                            }
                        }

                        if (added)
                            LogService.Info($"Notícia armazenada para análise: {item.Url}");
                        else
                            LogService.Warn($"DEBUG: duplicata ignorada {item.Url}");

                        // 3) Atualiza ranking parcial (uma única vez por URL processada)
                        var partialResults = SelectBestNewsPerTopic();
                        DisplayTopicResults(partialResults);
                    }
                    catch (OperationCanceledException)
                    {
                        // ok, cancelado
                    }
                    catch (Exception ex)
                    {
                        LogService.Error($"Erro ao processar URL {url} dentro de ProcessUrlsAsync", ex);
                    }
                    finally
                    {
                        semaphore.Release();
                        UpdateProgress();
                    }
                });

                await Task.WhenAll(tasks);
            }
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
                    return null;
                }

                // 2️⃣ IA
                var response = await _groqService.ClassifyNewsAsync(scraped.RawText);

                // ✅ AGORA: o campo correto é "scores"
                if (response == null || response.scores == null)
                {
                    LogService.Warn($"IA retornou resposta inválida (scores nulo): {url}");
                    return null;
                }

                NewsScoresItem item;

                lock (_scoresLock)
                {
                    item = new NewsScoresItem
                    {
                        Url = url,
                        Title = scraped.Title,
                        Scores = response.scores, // ✅ usar scores (siglas)
                        SourceOrder = _allNewsScores.Count
                    };

                    // 🔴 impedir duplicação
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

                // 4️⃣ Recalcular ranking parcial
                var partialResults = SelectBestNewsPerTopic();

                // 5️⃣ Atualizar grid em tempo real
                DisplayTopicResults(partialResults);

                return item;
            }
            catch (Exception ex)
            {
                LogService.Error($"Erro ao processar URL {url}", ex);
                return null;
            }
        }

        private void SaveFinalRankingToFile(List<TopicResult> results)
        {
            try
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                _lastReportPath = Path.Combine(
                    folder,
                    $"NewsRanking_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                );

                var lines = new List<string>();

                lines.Add("===== NEWS TOPIC RANKING =====");
                lines.Add($"Data: {DateTime.Now}");
                lines.Add("");

                lines.Add("===== RESULTADOS =====");
                lines.Add("");

                foreach (var r in results)
                {
                    lines.Add($"Assunto : {r.Topic}");
                    lines.Add($"Score   : {r.Score}");
                    lines.Add($"URL     : {r.Url}");
                    lines.Add("");
                }

                lines.Add("");
                lines.Add("===== RESUMO =====");
                lines.Add($"Total de notícias analisadas : {_allNewsScores.Count}");
                lines.Add($"Total de tópicos selecionados: {results.Count}");
                lines.Add("");

                List<string> failedDomainsCopy;

                lock (_failedDomainsLock)
                {
                    failedDomainsCopy = new List<string>(_failedDomains);
                }

                lines.Add("===== DOMÍNIOS COM FALHA =====");

                if (failedDomainsCopy.Count == 0)
                {
                    lines.Add("Nenhum domínio falhou.");
                }
                else
                {
                    foreach (var d in failedDomainsCopy)
                        lines.Add(d);
                }

                File.WriteAllLines(_lastReportPath, lines);

                LogService.Info($"Relatório salvo em {_lastReportPath}");
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
            nudParallelism.Enabled = enabled;
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

        //private IEnumerable<string> GetTopics()
        //{
        //    return TopicCatalog.Topics;
        //}

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


    }
}