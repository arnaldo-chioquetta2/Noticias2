using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Services;
using NewsImpactRanker.WinForms.Storage;
using NewsImpactRanker.WinForms.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Cryptography;
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

            // 1. Configura o ComboBox de Provedores
            cmbProvider.DataSource = Enum.GetValues(typeof(AiProvider));
            cmbProvider.SelectedItem = config.SelectedProvider;

            // 2. Carrega as chaves
            txtApiKey.Text = config.AiApiKey;           // Chave Groq
            txtGeminiApiKey.Text = config.GeminiApiKey; // Chave Gemini

            // 3. Modelo e Arquivos (Corrigindo os nomes conforme o Designer)
            txtModel.Text = config.SelectedModel ?? "llama-3.1-8b-instant";
            txtNewsFile.Text = config.NewsFilePath;     // Nome correto: txtNewsFile
            txtPromptFile.Text = config.PromptFilePath;

            // Se você tiver um ComboBox para escolher o provedor:
            if (cmbProvider != null)
            {
                cmbProvider.DataSource = Enum.GetValues(typeof(AiProvider));
                cmbProvider.SelectedItem = config.SelectedProvider;
            }

            nudSummaryWordCount.Value = config.SummaryWordCount > 0 ? config.SummaryWordCount : 10;
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            int valorNaTela = (int)nudSummaryWordCount.Value;
            var config = new AppConfig
            {
                AiApiKey = txtApiKey.Text.Trim(),
                GeminiApiKey = txtGeminiApiKey.Text.Trim(),
                SelectedModel = txtModel.Text.Trim(),
                PromptFilePath = txtPromptFile.Text.Trim(),
                NewsFilePath = txtNewsFile.Text.Trim(),
                SelectedProvider = (AiProvider)cmbProvider.SelectedItem,
                SummaryWordCount = valorNaTela
            };
            bool wordCountChanged = config.SummaryWordCount != (int)nudSummaryWordCount.Value;

            if (wordCountChanged)
            {
                // 2. Pergunta se ele tem certeza, já que isso vai apagar o histórico
                var result = MessageBox.Show(
                    "Você alterou o tamanho do resumo. Para o filtro anti-duplicidade funcionar corretamente, o histórico antigo de resumos precisará ser apagado. Deseja continuar?",
                    "Aviso de Alteração",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result == DialogResult.No)
                {
                    return; // Cancela o salvamento e deixa o usuário na tela
                }

                try
                {
                    // 3. Chama o nosso novo Gerente para limpar o arquivo físico!
                    SummaryCacheManager.ClearCache();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erro ao limpar cache antigo: " + ex.Message);
                    return;
                }
            }

            StorageManager.SaveConfig(config);
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
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

        // Método para atualizar a interface conforme o provedor selecionado
        private void cmbProvider_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selected = (AiProvider)cmbProvider.SelectedItem;

            if (selected == AiProvider.Gemini)
            {
                // Se for Gemini, foca na chave do Gemini
                txtGeminiApiKey.Enabled = true;
                txtGeminiApiKey.BackColor = Color.White;

                // "Apaga" visualmente o Groq para não confundir
                txtApiKey.Enabled = false;
                txtApiKey.BackColor = Color.LightGray;

                //lblGemini.ForeColor = Color.Blue; // Destaque visual
                //lblGroq.ForeColor = Color.Gray;
            }
            else
            {
                // Se for Groq, faz o contrário
                txtApiKey.Enabled = true;
                txtApiKey.BackColor = Color.White;

                txtGeminiApiKey.Enabled = false;
                txtGeminiApiKey.BackColor = Color.LightGray;

                //lblGroq.ForeColor = Color.Blue;
                //lblGemini.ForeColor = Color.Gray;
            }
        }

    }
}
