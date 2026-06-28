using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Storage;
using NewsImpactRanker.WinForms.Utils;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NewsImpactRanker.WinForms.Forms
{
    public partial class ConfigForm : Form
    {
        private Label labelKimiApiKey;
        private TextBox txtKimiApiKey;
        private Label labelKimiBaseUrl;
        private TextBox txtKimiBaseUrl;
        private Label labelKimiModel;
        private TextBox txtKimiModel;
        private CheckBox chkKimiEnableSearch;
        private CheckBox chkKimiEnableThinking;

        public ConfigForm()
        {
            InitializeComponent();
            InitializeKimiControls();
        }

        private void InitializeKimiControls()
        {
            labelKimiApiKey = new Label
            {
                AutoSize = true,
                Location = new Point(24, 18),
                Name = "labelKimiApiKey",
                Size = new Size(88, 13),
                Text = "Chave Kimi API:"
            };

            txtKimiApiKey = new TextBox
            {
                Location = new Point(24, 38),
                Name = "txtKimiApiKey",
                Size = new Size(444, 20)
            };

            labelKimiBaseUrl = new Label
            {
                AutoSize = true,
                Location = new Point(24, 70),
                Name = "labelKimiBaseUrl",
                Size = new Size(85, 13),
                Text = "Base URL Kimi:"
            };

            txtKimiBaseUrl = new TextBox
            {
                Location = new Point(24, 90),
                Name = "txtKimiBaseUrl",
                Size = new Size(444, 20)
            };

            labelKimiModel = new Label
            {
                AutoSize = true,
                Location = new Point(24, 122),
                Name = "labelKimiModel",
                Size = new Size(77, 13),
                Text = "Modelo Kimi:"
            };

            txtKimiModel = new TextBox
            {
                Location = new Point(24, 142),
                Name = "txtKimiModel",
                Size = new Size(444, 20)
            };

            chkKimiEnableSearch = new CheckBox
            {
                AutoSize = true,
                Location = new Point(24, 184),
                Name = "chkKimiEnableSearch",
                Text = "Habilitar Search"
            };

            chkKimiEnableThinking = new CheckBox
            {
                AutoSize = true,
                Location = new Point(160, 184),
                Name = "chkKimiEnableThinking",
                Text = "Habilitar Thinking"
            };

            tabKimi.Controls.Add(labelKimiApiKey);
            tabKimi.Controls.Add(txtKimiApiKey);
            tabKimi.Controls.Add(labelKimiBaseUrl);
            tabKimi.Controls.Add(txtKimiBaseUrl);
            tabKimi.Controls.Add(labelKimiModel);
            tabKimi.Controls.Add(txtKimiModel);
            tabKimi.Controls.Add(chkKimiEnableSearch);
            tabKimi.Controls.Add(chkKimiEnableThinking);

            chkKimiEnableSearch.BringToFront();
            chkKimiEnableThinking.BringToFront();
        }

        private void ConfigForm_Load(object sender, EventArgs e)
        {
            var config = StorageManager.LoadConfig();

            cmbProvider.DataSource = Enum.GetValues(typeof(AiProvider));
            cmbProvider.SelectedItem = config.SelectedProvider;

            txtApiKey.Text = config.AiApiKey;
            txtGeminiApiKey.Text = config.GeminiApiKey;
            txtGeminiModel.Text = string.IsNullOrWhiteSpace(config.GeminiModel) ? "gemini-2.0-flash" : config.GeminiModel;
            txtDeepSeekApiKey.Text = config.DeepSeekApiKey;
            txtDeepSeekModel.Text = string.IsNullOrWhiteSpace(config.DeepSeekModel) ? "deepseek-chat" : config.DeepSeekModel;
            txtDeepSeekBaseUrl.Text = string.IsNullOrWhiteSpace(config.DeepSeekBaseUrl) ? "https://api.deepseek.com" : config.DeepSeekBaseUrl;
            txtMistralApiKey.Text = config.MistralApiKey;
            txtMistralModel.Text = string.IsNullOrWhiteSpace(config.MistralModel) ? "open-mixtral-8x7b" : config.MistralModel;
            txtKimiApiKey.Text = config.KimiApiKey;
            txtKimiBaseUrl.Text = string.IsNullOrWhiteSpace(config.KimiBaseUrl) ? "https://servidorapi.duckdns.org/v1" : config.KimiBaseUrl;
            txtKimiModel.Text = string.IsNullOrWhiteSpace(config.KimiModel) ? "kimi-k2" : config.KimiModel;
            chkKimiEnableSearch.Checked = config.KimiEnableSearch;
            chkKimiEnableThinking.Checked = config.KimiEnableThinking;
            txtModel.Text = string.IsNullOrWhiteSpace(config.SelectedModel) ? "llama-3.1-8b-instant" : config.SelectedModel;
            txtNewsFile.Text = config.NewsFilePath;
            txtPromptFile.Text = config.PromptFilePath;
            nudSummaryWordCount.Value = config.SummaryWordCount > 0 ? config.SummaryWordCount : 10;

            UpdateProviderFields();
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            int valorNaTela = (int)nudSummaryWordCount.Value;
            var config = new AppConfig
            {
                AiApiKey = txtApiKey.Text.Trim(),
                GeminiApiKey = txtGeminiApiKey.Text.Trim(),
                GeminiModel = txtGeminiModel.Text.Trim(),
                DeepSeekApiKey = txtDeepSeekApiKey.Text.Trim(),
                DeepSeekModel = txtDeepSeekModel.Text.Trim(),
                DeepSeekBaseUrl = txtDeepSeekBaseUrl.Text.Trim(),
                KimiApiKey = txtKimiApiKey.Text.Trim(),
                KimiBaseUrl = txtKimiBaseUrl.Text.Trim(),
                KimiModel = txtKimiModel.Text.Trim(),
                KimiEnableSearch = chkKimiEnableSearch.Checked,
                KimiEnableThinking = chkKimiEnableThinking.Checked,
                MistralApiKey = txtMistralApiKey.Text.Trim(),
                MistralModel = txtMistralModel.Text.Trim(),
                ProviderPriority = "DeepSeek>Groq>Gemini>Kimi",
                SelectedModel = txtModel.Text.Trim(),
                PromptFilePath = txtPromptFile.Text.Trim(),
                NewsFilePath = txtNewsFile.Text.Trim(),
                SelectedProvider = (AiProvider)cmbProvider.SelectedItem,
                SummaryWordCount = valorNaTela
            };

            StorageManager.SaveConfig(config);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Arquivos de texto (*.txt)|*.txt";
                dialog.Title = "Selecione o arquivo com as URLs";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtNewsFile.Text = dialog.FileName;
                }
            }
        }

        private void btnBrowsePrompt_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Arquivos de texto (*.txt)|*.txt|Todos (*.*)|*.*";

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtPromptFile.Text = dialog.FileName;
                }
            }
        }

        private void cmbProvider_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateProviderFields();
        }

        private void UpdateProviderFields()
        {
            var selected = (AiProvider)cmbProvider.SelectedItem;

            SetProviderFieldState(txtApiKey, selected == AiProvider.Groq);
            SetProviderFieldState(txtModel, selected == AiProvider.Groq);
            SetProviderFieldState(txtGeminiApiKey, selected == AiProvider.Gemini);
            SetProviderFieldState(txtGeminiModel, selected == AiProvider.Gemini);
            SetProviderFieldState(txtDeepSeekApiKey, selected == AiProvider.DeepSeek);
            SetProviderFieldState(txtDeepSeekModel, selected == AiProvider.DeepSeek);
            SetProviderFieldState(txtDeepSeekBaseUrl, selected == AiProvider.DeepSeek);
            SetProviderFieldState(txtMistralApiKey, selected == AiProvider.Mistral);
            SetProviderFieldState(txtMistralModel, selected == AiProvider.Mistral);
            SetProviderFieldState(txtKimiApiKey, selected == AiProvider.Kimi);
            SetProviderFieldState(txtKimiBaseUrl, selected == AiProvider.Kimi);
            SetProviderFieldState(txtKimiModel, selected == AiProvider.Kimi);
            SetProviderFieldState(chkKimiEnableSearch, selected == AiProvider.Kimi);
            SetProviderFieldState(chkKimiEnableThinking, selected == AiProvider.Kimi);
        }

        private void SetProviderFieldState(TextBox textBox, bool enabled)
        {
            textBox.Enabled = enabled;
            textBox.BackColor = enabled ? Color.White : Color.LightGray;
        }

        private void SetProviderFieldState(CheckBox checkBox, bool enabled)
        {
            checkBox.Enabled = enabled;
        }
    }
}
