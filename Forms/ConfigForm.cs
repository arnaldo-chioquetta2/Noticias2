using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Storage;
using NewsImpactRanker.WinForms.Utils;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace NewsImpactRanker.WinForms.Forms
{
    public partial class ConfigForm : Form
    {
        public ConfigForm()
        {
            InitializeComponent();
        }

        private void ConfigForm_Load(object sender, EventArgs e)
        {
            var config = StorageManager.LoadConfig();

            cmbProvider.DataSource = Enum.GetValues(typeof(AiProvider));
            cmbProvider.SelectedItem = config.SelectedProvider;

            txtApiKey.Text = config.AiApiKey;
            txtGeminiApiKey.Text = config.GeminiApiKey;
            txtDeepSeekApiKey.Text = config.DeepSeekApiKey;
            txtDeepSeekModel.Text = string.IsNullOrWhiteSpace(config.DeepSeekModel) ? "deepseek-chat" : config.DeepSeekModel;
            txtDeepSeekBaseUrl.Text = string.IsNullOrWhiteSpace(config.DeepSeekBaseUrl) ? "https://api.deepseek.com" : config.DeepSeekBaseUrl;
            txtMistralApiKey.Text = config.MistralApiKey;
            txtMistralModel.Text = string.IsNullOrWhiteSpace(config.MistralModel) ? "open-mixtral-8x7b" : config.MistralModel;

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
                DeepSeekApiKey = txtDeepSeekApiKey.Text.Trim(),
                DeepSeekModel = txtDeepSeekModel.Text.Trim(),
                DeepSeekBaseUrl = txtDeepSeekBaseUrl.Text.Trim(),
                MistralApiKey = txtMistralApiKey.Text.Trim(),
                MistralModel = txtMistralModel.Text.Trim(),
                ProviderPriority = "DeepSeek>Groq>Gemini",
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
            SetProviderFieldState(txtDeepSeekApiKey, selected == AiProvider.DeepSeek);
            SetProviderFieldState(txtDeepSeekModel, selected == AiProvider.DeepSeek);
            SetProviderFieldState(txtDeepSeekBaseUrl, selected == AiProvider.DeepSeek);
            SetProviderFieldState(txtMistralApiKey, selected == AiProvider.Mistral);
            SetProviderFieldState(txtMistralModel, selected == AiProvider.Mistral);
        }

        private void SetProviderFieldState(TextBox textBox, bool enabled)
        {
            textBox.Enabled = enabled;
            textBox.BackColor = enabled ? Color.White : Color.LightGray;
        }
    }
}
