namespace NewsImpactRanker.WinForms.Forms
{
    partial class ConfigForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.labelProvider = new System.Windows.Forms.Label();
            this.cmbProvider = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.txtApiKey = new System.Windows.Forms.TextBox();
            this.labelGemini = new System.Windows.Forms.Label();
            this.txtGeminiApiKey = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.txtModel = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.txtNewsFile = new System.Windows.Forms.TextBox();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.label4 = new System.Windows.Forms.Label();
            this.txtPromptFile = new System.Windows.Forms.TextBox();
            this.btnBrowsePrompt = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.nudSummaryWordCount = new System.Windows.Forms.NumericUpDown();
            this.label5 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.nudSummaryWordCount)).BeginInit();
            this.SuspendLayout();
            // 
            // labelProvider
            // 
            this.labelProvider.AutoSize = true;
            this.labelProvider.Location = new System.Drawing.Point(12, 9);
            this.labelProvider.Name = "labelProvider";
            this.labelProvider.Size = new System.Drawing.Size(81, 13);
            this.labelProvider.TabIndex = 1;
            this.labelProvider.Text = "Provedor de IA:";
            // 
            // cmbProvider
            // 
            this.cmbProvider.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbProvider.FormattingEnabled = true;
            this.cmbProvider.Location = new System.Drawing.Point(12, 25);
            this.cmbProvider.Name = "cmbProvider";
            this.cmbProvider.Size = new System.Drawing.Size(381, 21);
            this.cmbProvider.TabIndex = 0;
            this.cmbProvider.SelectedIndexChanged += new System.EventHandler(this.cmbProvider_SelectedIndexChanged);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 55);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(102, 13);
            this.label1.TabIndex = 12;
            this.label1.Text = "Chave da API Groq:";
            // 
            // txtApiKey
            // 
            this.txtApiKey.Location = new System.Drawing.Point(12, 71);
            this.txtApiKey.Name = "txtApiKey";
            this.txtApiKey.Size = new System.Drawing.Size(381, 20);
            this.txtApiKey.TabIndex = 11;
            // 
            // labelGemini
            // 
            this.labelGemini.AutoSize = true;
            this.labelGemini.Location = new System.Drawing.Point(12, 100);
            this.labelGemini.Name = "labelGemini";
            this.labelGemini.Size = new System.Drawing.Size(111, 13);
            this.labelGemini.TabIndex = 2;
            this.labelGemini.Text = "Chave da API Gemini:";
            // 
            // txtGeminiApiKey
            // 
            this.txtGeminiApiKey.Location = new System.Drawing.Point(12, 116);
            this.txtGeminiApiKey.Name = "txtGeminiApiKey";
            this.txtGeminiApiKey.Size = new System.Drawing.Size(381, 20);
            this.txtGeminiApiKey.TabIndex = 3;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(12, 145);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(45, 13);
            this.label2.TabIndex = 8;
            this.label2.Text = "Modelo:";
            // 
            // txtModel
            // 
            this.txtModel.Location = new System.Drawing.Point(12, 161);
            this.txtModel.Name = "txtModel";
            this.txtModel.Size = new System.Drawing.Size(381, 20);
            this.txtModel.TabIndex = 7;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(12, 190);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(102, 13);
            this.label3.TabIndex = 13;
            this.label3.Text = "Arquivo de notícias:";
            // 
            // txtNewsFile
            // 
            this.txtNewsFile.Location = new System.Drawing.Point(12, 206);
            this.txtNewsFile.Name = "txtNewsFile";
            this.txtNewsFile.Size = new System.Drawing.Size(300, 20);
            this.txtNewsFile.TabIndex = 14;
            // 
            // btnBrowse
            // 
            this.btnBrowse.Location = new System.Drawing.Point(318, 204);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(75, 23);
            this.btnBrowse.TabIndex = 15;
            this.btnBrowse.Text = "Selecionar";
            this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(12, 235);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(97, 13);
            this.label4.TabIndex = 4;
            this.label4.Text = "Arquivo de Prompt:";
            // 
            // txtPromptFile
            // 
            this.txtPromptFile.Location = new System.Drawing.Point(12, 251);
            this.txtPromptFile.Name = "txtPromptFile";
            this.txtPromptFile.Size = new System.Drawing.Size(300, 20);
            this.txtPromptFile.TabIndex = 5;
            // 
            // btnBrowsePrompt
            // 
            this.btnBrowsePrompt.Location = new System.Drawing.Point(318, 249);
            this.btnBrowsePrompt.Name = "btnBrowsePrompt";
            this.btnBrowsePrompt.Size = new System.Drawing.Size(75, 23);
            this.btnBrowsePrompt.TabIndex = 6;
            this.btnBrowsePrompt.Text = "Selecionar";
            this.btnBrowsePrompt.Click += new System.EventHandler(this.btnBrowsePrompt_Click);
            // 
            // btnSave
            // 
            this.btnSave.Location = new System.Drawing.Point(12, 335);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(75, 23);
            this.btnSave.TabIndex = 10;
            this.btnSave.Text = "Salvar";
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(318, 335);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 9;
            this.btnCancel.Text = "Cancelar";
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // nudSummaryWordCount
            // 
            this.nudSummaryWordCount.Location = new System.Drawing.Point(351, 293);
            this.nudSummaryWordCount.Maximum = new decimal(new int[] {
            30,
            0,
            0,
            0});
            this.nudSummaryWordCount.Minimum = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.nudSummaryWordCount.Name = "nudSummaryWordCount";
            this.nudSummaryWordCount.Size = new System.Drawing.Size(43, 20);
            this.nudSummaryWordCount.TabIndex = 16;
            this.nudSummaryWordCount.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(245, 295);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(100, 13);
            this.label5.TabIndex = 17;
            this.label5.Text = "Palavras do resumo";
            // 
            // ConfigForm
            // 
            this.AcceptButton = this.btnSave;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(410, 379);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.nudSummaryWordCount);
            this.Controls.Add(this.cmbProvider);
            this.Controls.Add(this.labelProvider);
            this.Controls.Add(this.labelGemini);
            this.Controls.Add(this.txtGeminiApiKey);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.txtPromptFile);
            this.Controls.Add(this.btnBrowsePrompt);
            this.Controls.Add(this.txtModel);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.txtApiKey);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.txtNewsFile);
            this.Controls.Add(this.btnBrowse);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ConfigForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Configurações da IA";
            this.Load += new System.EventHandler(this.ConfigForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.nudSummaryWordCount)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.Label labelProvider;
        private System.Windows.Forms.ComboBox cmbProvider;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtApiKey;
        private System.Windows.Forms.Label labelGemini;
        private System.Windows.Forms.TextBox txtGeminiApiKey;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox txtModel;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtNewsFile;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox txtPromptFile;
        private System.Windows.Forms.Button btnBrowsePrompt;
        private System.Windows.Forms.NumericUpDown nudSummaryWordCount;
        private System.Windows.Forms.Label label5;
    }
}