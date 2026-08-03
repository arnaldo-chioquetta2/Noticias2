using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Services;
using NewsImpactRanker.WinForms.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace NewsImpactRanker.WinForms.Forms
{
    public sealed class SummaryCacheViewerForm : Form
    {
        private readonly string _initialSearch;
        private readonly TextBox _searchBox = new TextBox();
        private readonly Button _refreshButton = new Button();
        private readonly Button _clearButton = new Button();
        private readonly Button _copySummaryButton = new Button();
        private readonly Button _copyUrlButton = new Button();
        private readonly Button _openUrlButton = new Button();
        private readonly Button _similarButton = new Button();
        private readonly ComboBox _canonicalFilter = new ComboBox();
        private readonly ComboBox _providerFilter = new ComboBox();
        private readonly ComboBox _wordFilter = new ComboBox();
        private readonly ComboBox _dateFilter = new ComboBox();
        private readonly Label _countLabel = new Label();
        private readonly DataGridView _grid = new DataGridView();
        private readonly TextBox _details = new TextBox();
        private readonly Timer _debounceTimer = new Timer();
        private List<SummaryCacheItem> _items = new List<SummaryCacheItem>();
        private List<SummaryCacheItem> _shown = new List<SummaryCacheItem>();

        public SummaryCacheViewerForm(string initialSearch = null)
        {
            _initialSearch = initialSearch;
            Text = "Cache de resumos canônicos";
            Width = 1200;
            Height = 700;
            MinimumSize = new Size(900, 500);
            StartPosition = FormStartPosition.CenterParent;
            LogService.Info("[SUMMARY_VIEWER] Abrindo cache de resumos");
            BuildLayout();
            _debounceTimer.Interval = 300;
            _debounceTimer.Tick += (s, e) => { _debounceTimer.Stop(); ApplyFilters(); };
            _searchBox.TextChanged += (s, e) => { _debounceTimer.Stop(); _debounceTimer.Start(); };
            _refreshButton.Click += (s, e) => LoadData();
            _clearButton.Click += (s, e) => { _searchBox.Clear(); ApplyFilters(); };
            _canonicalFilter.SelectedIndexChanged += (s, e) => ApplyFilters();
            _providerFilter.SelectedIndexChanged += (s, e) => ApplyFilters();
            _wordFilter.SelectedIndexChanged += (s, e) => ApplyFilters();
            _dateFilter.SelectedIndexChanged += (s, e) => ApplyFilters();
            _grid.SelectionChanged += (s, e) => ShowDetails();
            _copySummaryButton.Click += (s, e) => CopySelected(false);
            _copyUrlButton.Click += (s, e) => CopySelected(true);
            _openUrlButton.Click += (s, e) => OpenSelectedUrl();
            _similarButton.Click += (s, e) => ShowSimilar();
            Shown += (s, e) => LoadData();
        }

        private void BuildLayout()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 68, Padding = new Padding(8), WrapContents = true };
            top.Controls.Add(new Label { Text = "Buscar:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
            _searchBox.Width = 220;
            top.Controls.Add(_searchBox);
            _refreshButton.Text = "Atualizar"; _refreshButton.Width = 85; top.Controls.Add(_refreshButton);
            _clearButton.Text = "Limpar busca"; _clearButton.Width = 95; top.Controls.Add(_clearButton);
            _canonicalFilter.Width = 120; top.Controls.Add(_canonicalFilter);
            _providerFilter.Width = 130; top.Controls.Add(_providerFilter);
            _wordFilter.Width = 90; top.Controls.Add(_wordFilter);
            _dateFilter.Width = 110; top.Controls.Add(_dateFilter);
            _countLabel.AutoSize = true; _countLabel.Margin = new Padding(8, 7, 3, 3); top.Controls.Add(_countLabel);
            Controls.Add(top);

            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoGenerateColumns = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            AddColumn("Data", 125); AddColumn("Resumo original", 270); AddColumn("Resumo normalizado", 270);
            AddColumn("URL", 300); AddColumn("Provider", 90); AddColumn("Palavras", 65); AddColumn("Canônico", 70);
            Controls.Add(_grid);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 150, Padding = new Padding(8) };
            _details.Multiline = true; _details.ReadOnly = true; _details.ScrollBars = ScrollBars.Vertical;
            _details.Dock = DockStyle.Fill;
            bottom.Controls.Add(_details);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 30 };
            _copySummaryButton.Text = "Copiar resumo normalizado";
            _copyUrlButton.Text = "Copiar URL";
            _openUrlButton.Text = "Abrir URL no navegador";
            _similarButton.Text = "Encontrar semelhantes";
            actions.Controls.Add(_copySummaryButton); actions.Controls.Add(_copyUrlButton);
            actions.Controls.Add(_openUrlButton); actions.Controls.Add(_similarButton);
            bottom.Controls.Add(actions);
            Controls.Add(bottom);
        }

        private void AddColumn(string header, int width)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.Automatic });
        }

        private void LoadData()
        {
            _items = SummaryCacheManager.LoadCache()
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Summary))
                .OrderByDescending(x => x.DateAdded == default(DateTime) ? DateTime.MinValue : x.DateAdded)
                .ToList();
            BuildFilters();
            ApplyFilters();
            LogService.Info($"[SUMMARY_VIEWER] Cache recarregado");
            LogService.Info($"[SUMMARY_VIEWER] Registros: {_items.Count}");
        }

        private void BuildFilters()
        {
            SetItems(_canonicalFilter, new[] { "Todos", "Somente canônicos", "Somente não canônicos" });
            SetItems(_providerFilter, new[] { "Todos" }.Concat(_items.Select(x => string.IsNullOrWhiteSpace(x.Provider) ? "(vazio)" : x.Provider).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)).ToArray());
            SetItems(_wordFilter, new[] { "Todos" }.Concat(_items.Select(x => x.WordCount.ToString(CultureInfo.InvariantCulture)).Distinct().OrderBy(x => x)).ToArray());
            SetItems(_dateFilter, new[] { "Todas", "Hoje", "Últimos 7 dias", "Últimos 30 dias" });
        }

        private static void SetItems(ComboBox combo, string[] values)
        {
            string old = combo.SelectedItem as string;
            combo.Items.Clear(); combo.Items.AddRange(values);
            combo.SelectedItem = values.Contains(old) ? old : values[0];
        }

        private void ApplyFilters()
        {
            string query = CanonicalSummaryMatcher.Normalize(_searchBox.Text);
            var tokens = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string canonical = _canonicalFilter.SelectedItem as string ?? "Todos";
            string provider = _providerFilter.SelectedItem as string ?? "Todos";
            string words = _wordFilter.SelectedItem as string ?? "Todos";
            string date = _dateFilter.SelectedItem as string ?? "Todas";
            DateTime now = DateTime.Now;

            _shown = _items.Where(item =>
            {
                if (canonical == "Somente canônicos" && !item.IsCanonical) return false;
                if (canonical == "Somente não canônicos" && item.IsCanonical) return false;
                if (provider != "Todos" && (item.Provider ?? "(vazio)") != provider) return false;
                if (words != "Todos" && item.WordCount.ToString(CultureInfo.InvariantCulture) != words) return false;
                if (date == "Hoje" && item.DateAdded.Date != now.Date) return false;
                if (date == "Últimos 7 dias" && item.DateAdded < now.AddDays(-7)) return false;
                if (date == "Últimos 30 dias" && item.DateAdded < now.AddDays(-30)) return false;
                if (tokens.Length == 0) return true;
                string haystack = CanonicalSummaryMatcher.Normalize(string.Join(" ", new[] { item.Summary, item.NormalizedSummary, item.Url, item.Provider, item.WordCount.ToString(), item.IsCanonical.ToString(), item.DateAdded.ToString("yyyy-MM-dd") }));
                return tokens.All(haystack.Contains);
            }).ToList();

            _grid.Rows.Clear();
            foreach (var item in _shown)
            {
                int row = _grid.Rows.Add(
                    item.DateAdded == default(DateTime) ? "" : item.DateAdded.ToString("yyyy-MM-dd HH:mm"),
                    item.Summary, item.NormalizedSummary, item.Url, item.Provider, item.WordCount,
                    item.IsCanonical ? "Sim" : "Não");
                _grid.Rows[row].Tag = item;
            }
            _countLabel.Text = $"Total: {_items.Count} | Canônicos: {_items.Count(x => x.IsCanonical)} | Não canônicos: {_items.Count(x => !x.IsCanonical)} | Exibidos: {_shown.Count}";
            LogService.Info($"[SUMMARY_VIEWER] Busca: {_searchBox.Text}; Resultados: {_shown.Count}");
            ShowDetails();
        }

        private SummaryCacheItem SelectedItem()
        {
            return _grid.CurrentRow?.Tag as SummaryCacheItem;
        }

        private void ShowDetails()
        {
            var item = SelectedItem();
            _details.Text = item == null
                ? string.Empty
                : $"Resumo original:{Environment.NewLine}{item.Summary}{Environment.NewLine}{Environment.NewLine}" +
                  $"Resumo normalizado:{Environment.NewLine}{item.NormalizedSummary}{Environment.NewLine}{Environment.NewLine}" +
                  $"URL: {item.Url ?? "(não registrada)"}{Environment.NewLine}" +
                  $"Provider: {item.Provider ?? "(não registrado)"}{Environment.NewLine}" +
                  $"Data: {(item.DateAdded == default(DateTime) ? "(não registrada)" : item.DateAdded.ToString("o"))}{Environment.NewLine}" +
                  $"Palavras: {item.WordCount}{Environment.NewLine}IsCanonical: {item.IsCanonical}";
        }

        private void CopySelected(bool url)
        {
            var item = SelectedItem();
            if (item == null) return;
            Clipboard.SetText(url ? (item.Url ?? string.Empty) : (item.NormalizedSummary ?? CanonicalSummaryMatcher.Normalize(item.Summary)));
        }

        private void OpenSelectedUrl()
        {
            var item = SelectedItem();
            if (item == null || !Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show("O registro selecionado não possui uma URL HTTP/HTTPS válida.", "Cache de resumos", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }

        private void ShowSimilar()
        {
            var selected = SelectedItem();
            if (selected == null) return;
            var rows = _items.Where(x => x != selected && x.IsCanonical)
                .Select(x => new { Item = x, Similarity = CanonicalSummaryMatcher.Similarity(selected.Summary, x.Summary) })
                .OrderByDescending(x => x.Similarity).Take(20).ToList();

            using (var dialog = new Form { Text = "Resumos semelhantes", Width = 900, Height = 500, StartPosition = FormStartPosition.CenterParent })
            {
                var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoGenerateColumns = true };
                grid.DataSource = rows.Select(x => new { Similaridade = x.Similarity, Resumo = x.Item.Summary, Url = x.Item.Url, Data = x.Item.DateAdded, Provider = x.Item.Provider }).ToList();
                dialog.Controls.Add(grid);
                dialog.ShowDialog(this);
            }
        }
    }
}
