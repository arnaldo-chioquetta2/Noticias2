using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Storage;
using NewsImpactRanker.WinForms.Services;

namespace NewsImpactRanker.WinForms.Forms
{
    public class PostedUrlsViewerForm : Form
    {
        private readonly TextBox txtSearch = new TextBox();
        private readonly ComboBox cmbProvider = new ComboBox();
        private readonly ComboBox cmbPeriod = new ComboBox();
        private readonly Button btnRefresh = new Button();
        private readonly Button btnClear = new Button();
        private readonly DataGridView grid = new DataGridView();
        private readonly Label lblCount = new Label();
        private readonly TextBox txtDetails = new TextBox();
        private readonly Button btnCopyUrl = new Button();
        private readonly Button btnOpenUrl = new Button();
        private readonly Button btnClose = new Button();
        private readonly Timer searchTimer = new Timer();
        private List<PostedUrlItem> items = new List<PostedUrlItem>();
        private List<PostedUrlItem> filteredItems = new List<PostedUrlItem>();

        public PostedUrlsViewerForm()
        {
            LogService.Info("[POSTED_VIEWER] Abrindo visualizador");
            InitializeLayout();
            searchTimer.Interval = 300;
            searchTimer.Tick += (s, e) => { searchTimer.Stop(); ApplyFilter(); };
            LoadItems();
        }

        private void InitializeLayout()
        {
            Text = "URLs já postadas";
            ClientSize = new Size(1000, 650);
            MinimumSize = new Size(800, 500);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F);

            var searchLabel = new Label { Text = "Busca:", AutoSize = true, Location = new Point(12, 16) };
            txtSearch.Location = new Point(65, 12); txtSearch.Width = 360;
            txtSearch.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSearch.TextChanged += (s, e) => { searchTimer.Stop(); searchTimer.Start(); };

            var providerLabel = new Label { Text = "Provider:", AutoSize = true, Location = new Point(440, 16) };
            cmbProvider.Location = new Point(510, 12); cmbProvider.Width = 150;
            cmbProvider.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbProvider.SelectedIndexChanged += (s, e) => ApplyFilter();

            var periodLabel = new Label { Text = "Período:", AutoSize = true, Location = new Point(675, 16) };
            cmbPeriod.Location = new Point(740, 12); cmbPeriod.Width = 125;
            cmbPeriod.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbPeriod.Items.AddRange(new object[] { "Todas", "Hoje", "Últimos 7 dias", "Últimos 30 dias" });
            cmbPeriod.SelectedIndex = 0;
            cmbPeriod.SelectedIndexChanged += (s, e) => ApplyFilter();

            btnRefresh.Text = "Atualizar"; btnRefresh.Location = new Point(12, 47); btnRefresh.Width = 95; btnRefresh.Click += (s, e) => LoadItems();
            btnClear.Text = "Limpar busca"; btnClear.Location = new Point(115, 47); btnClear.Width = 110; btnClear.Click += (s, e) => { txtSearch.Clear(); cmbProvider.SelectedIndex = 0; cmbPeriod.SelectedIndex = 0; ApplyFilter(); };
            lblCount.AutoSize = true; lblCount.Location = new Point(245, 53);

            grid.Location = new Point(12, 82); grid.Size = new Size(976, 390);
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoGenerateColumns = false; grid.RowHeadersVisible = false;
            grid.SelectionChanged += (s, e) => ShowSelectedDetails();
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "MarkedAt", HeaderText = "Data marcada", Width = 145 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Url", HeaderText = "URL", Width = 330 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Summary", HeaderText = "Resumo", Width = 300 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Provider", HeaderText = "Provider", Width = 95 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Version", HeaderText = "Versão", Width = 75 });

            var detailsLabel = new Label { Text = "Detalhes", AutoSize = true, Location = new Point(12, 484), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            txtDetails.Location = new Point(12, 507); txtDetails.Size = new Size(700, 90); txtDetails.Multiline = true; txtDetails.ReadOnly = true; txtDetails.ScrollBars = ScrollBars.Vertical;
            txtDetails.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            btnCopyUrl.Text = "Copiar URL"; btnCopyUrl.Location = new Point(730, 507); btnCopyUrl.Width = 110; btnCopyUrl.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; btnCopyUrl.Click += (s, e) => CopySelectedUrl();
            btnOpenUrl.Text = "Abrir URL"; btnOpenUrl.Location = new Point(850, 507); btnOpenUrl.Width = 110; btnOpenUrl.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; btnOpenUrl.Click += (s, e) => OpenSelectedUrl();
            btnClose.Text = "Fechar"; btnClose.DialogResult = DialogResult.OK; btnClose.Location = new Point(850, 610); btnClose.Width = 110; btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            AcceptButton = btnClose;

            Controls.AddRange(new Control[] { searchLabel, txtSearch, providerLabel, cmbProvider, periodLabel, cmbPeriod, btnRefresh, btnClear, lblCount, grid, detailsLabel, txtDetails, btnCopyUrl, btnOpenUrl, btnClose });
        }

        private void LoadItems()
        {
            items = PostedUrlsManager.Load().OrderByDescending(x => x.MarkedAt).ToList();
            LogService.Info($"[POSTED_VIEWER] Arquivo: {PostedUrlsManager.GetFilePath()}");
            LogService.Info($"[POSTED_VIEWER] Registros carregados: {items.Count}");
            cmbProvider.Items.Clear(); cmbProvider.Items.Add("Todos");
            foreach (string provider in items.Select(x => x.Provider ?? string.Empty).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x)) cmbProvider.Items.Add(provider);
            cmbProvider.SelectedIndex = 0;
            ApplyFilter();
            if (items.Count == 0) lblCount.Text = "Nenhuma URL foi marcada como postada.";
        }

        private void ApplyFilter()
        {
            string[] tokens = NormalizeText(txtSearch.Text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string provider = cmbProvider.SelectedItem?.ToString() ?? "Todos";
            string period = cmbPeriod.SelectedItem?.ToString() ?? "Todas";
            DateTime now = DateTime.Now;
            filteredItems = items.Where(item =>
            {
                string searchable = NormalizeText(string.Join(" ", item.Url, item.NormalizedUrl, item.Summary, item.Provider, item.ApplicationVersion, item.MarkedAt.ToString("dd/MM/yyyy HH:mm")));
                bool textOk = tokens.All(searchable.Contains);
                bool providerOk = provider == "Todos" || string.Equals(item.Provider ?? string.Empty, provider, StringComparison.OrdinalIgnoreCase);
                bool dateOk = period == "Todas" || (item.MarkedAt != default(DateTime) && (period == "Hoje" ? item.MarkedAt.Date == now.Date : period == "Últimos 7 dias" ? item.MarkedAt >= now.AddDays(-7) : item.MarkedAt >= now.AddDays(-30)));
                return textOk && providerOk && dateOk;
            }).OrderByDescending(x => x.MarkedAt).ToList();

            grid.Rows.Clear();
            foreach (PostedUrlItem item in filteredItems)
                grid.Rows.Add(item.MarkedAt == default(DateTime) ? string.Empty : item.MarkedAt.ToString("dd/MM/yyyy HH:mm"), item.Url, item.Summary, item.Provider, item.ApplicationVersion);
            lblCount.Text = $"Total no arquivo: {items.Count} | Exibidos: {filteredItems.Count}";
            LogService.Info($"[POSTED_VIEWER] Busca aplicada: {txtSearch.Text} | Provider: {provider} | Período: {period} | Resultados: {filteredItems.Count}");
            ShowSelectedDetails();
        }

        private PostedUrlItem GetSelectedItem()
        {
            return grid.CurrentRow != null && grid.CurrentRow.Index >= 0 && grid.CurrentRow.Index < filteredItems.Count ? filteredItems[grid.CurrentRow.Index] : null;
        }

        private void ShowSelectedDetails()
        {
            PostedUrlItem item = GetSelectedItem();
            txtDetails.Text = item == null ? string.Empty : string.Format("URL: {0}\r\nURL normalizada: {1}\r\nMarcada em: {2}\r\nResumo: {3}\r\nProvider: {4}\r\nVersão: {5}", item.Url, item.NormalizedUrl, item.MarkedAt == default(DateTime) ? "" : item.MarkedAt.ToString("dd/MM/yyyy HH:mm"), item.Summary, item.Provider, item.ApplicationVersion);
        }

        private void CopySelectedUrl()
        {
            PostedUrlItem item = GetSelectedItem();
            if (item == null) { MessageBox.Show("Selecione uma URL.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            Clipboard.SetText(item.Url ?? string.Empty);
            LogService.Info($"[POSTED_VIEWER] URL copiada: {item.Url}");
        }

        private void OpenSelectedUrl()
        {
            PostedUrlItem item = GetSelectedItem();
            if (item == null) { MessageBox.Show("Selecione uma URL.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!Uri.TryCreate(item.Url, UriKind.Absolute, out Uri uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) { MessageBox.Show("A URL selecionada é inválida.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            try { LogService.Info($"[POSTED_VIEWER] Abrindo URL: {item.Url}"); Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true }); }
            catch (Exception ex) { LogService.Error("[POSTED_VIEWER] ERRO ao abrir URL", ex); MessageBox.Show("Não foi possível abrir a URL.", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string formD = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (char ch in formD) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            return string.Join(" ", builder.ToString().Normalize(NormalizationForm.FormC).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}