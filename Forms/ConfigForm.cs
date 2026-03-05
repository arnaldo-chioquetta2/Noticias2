using System;
using System.Windows.Forms;
using NewsImpactRanker.WinForms.Models;
using NewsImpactRanker.WinForms.Storage;

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

            txtApiKey.Text = config.AiApiKey;
            txtModel.Text = config.AiModel ?? "mixtral-8x7b-32768";
            txtNewsFile.Text = config.NewsFilePath ?? "";
            txtPromptFile.Text = config.PromptFilePath;
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtApiKey.Text))
            {
                MessageBox.Show("A chave da API não pode estar vazia.",
                    "Erro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var config = StorageManager.LoadConfig();

            config.AiApiKey = txtApiKey.Text.Trim();
            config.AiModel = string.IsNullOrWhiteSpace(txtModel.Text)
                ? "mixtral-8x7b-32768"
                : txtModel.Text.Trim();

            config.NewsFilePath = txtNewsFile.Text?.Trim();

            config.PromptFilePath = txtPromptFile.Text.Trim();

            StorageManager.SaveConfig(config);

            this.DialogResult = DialogResult.OK;
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

    }
}
