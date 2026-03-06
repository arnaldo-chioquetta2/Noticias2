using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Storage;
using System;
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
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            var config = new AppConfig
            {
                AiApiKey = txtApiKey.Text.Trim(),
                GeminiApiKey = txtGeminiApiKey.Text.Trim(),
                SelectedModel = txtModel.Text.Trim(),
                PromptFilePath = txtPromptFile.Text.Trim(),
                NewsFilePath = txtNewsFile.Text.Trim(),
                SelectedProvider = (AiProvider)cmbProvider.SelectedItem
            };

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
