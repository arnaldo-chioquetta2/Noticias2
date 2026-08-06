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
            this.labelDeepSeek = new System.Windows.Forms.Label();
            this.txtDeepSeekApiKey = new System.Windows.Forms.TextBox();
            this.labelDeepSeekModel = new System.Windows.Forms.Label();
            this.txtDeepSeekModel = new System.Windows.Forms.TextBox();
            this.labelDeepSeekBaseUrl = new System.Windows.Forms.Label();
            this.txtDeepSeekBaseUrl = new System.Windows.Forms.TextBox();
            this.labelMistralApiKey = new System.Windows.Forms.Label();
            this.txtMistralApiKey = new System.Windows.Forms.TextBox();
            this.labelMistralModel = new System.Windows.Forms.Label();
            this.txtMistralModel = new System.Windows.Forms.TextBox();
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
            this.tabConfig = new System.Windows.Forms.TabControl();
            this.tabGeral = new System.Windows.Forms.TabPage();
            this.tabGroq = new System.Windows.Forms.TabPage();
            this.tabGemini = new System.Windows.Forms.TabPage();
            this.tabDeepSeek = new System.Windows.Forms.TabPage();
            this.tabMistral = new System.Windows.Forms.TabPage();
            this.tabKimi = new System.Windows.Forms.TabPage();
            this.tabCategorias = new System.Windows.Forms.TabPage();
            this.clbTopics = new System.Windows.Forms.CheckedListBox();
            this.btnMarkAllTopics = new System.Windows.Forms.Button();
            this.btnUnmarkAllTopics = new System.Windows.Forms.Button();
            this.lblTopicCount = new System.Windows.Forms.Label();
            this.labelGeminiModel = new System.Windows.Forms.Label();
            this.txtGeminiModel = new System.Windows.Forms.TextBox();
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
            // labelDeepSeek
            // 
            this.labelDeepSeek.AutoSize = true;
            this.labelDeepSeek.Location = new System.Drawing.Point(12, 145);
            this.labelDeepSeek.Name = "labelDeepSeek";
            this.labelDeepSeek.Size = new System.Drawing.Size(121, 13);
            this.labelDeepSeek.TabIndex = 18;
            this.labelDeepSeek.Text = "Chave da API DeepSeek:";
            // 
            // txtDeepSeekApiKey
            // 
            this.txtDeepSeekApiKey.Location = new System.Drawing.Point(12, 161);
            this.txtDeepSeekApiKey.Name = "txtDeepSeekApiKey";
            this.txtDeepSeekApiKey.Size = new System.Drawing.Size(381, 20);
            this.txtDeepSeekApiKey.TabIndex = 19;
            // 
            // labelDeepSeekModel
            // 
            this.labelDeepSeekModel.AutoSize = true;
            this.labelDeepSeekModel.Location = new System.Drawing.Point(12, 190);
            this.labelDeepSeekModel.Name = "labelDeepSeekModel";
            this.labelDeepSeekModel.Size = new System.Drawing.Size(99, 13);
            this.labelDeepSeekModel.TabIndex = 20;
            this.labelDeepSeekModel.Text = "Modelo DeepSeek:";
            // 
            // txtDeepSeekModel
            // 
            this.txtDeepSeekModel.Location = new System.Drawing.Point(12, 206);
            this.txtDeepSeekModel.Name = "txtDeepSeekModel";
            this.txtDeepSeekModel.Size = new System.Drawing.Size(381, 20);
            this.txtDeepSeekModel.TabIndex = 21;
            // 
            // labelDeepSeekBaseUrl
            // 
            this.labelDeepSeekBaseUrl.AutoSize = true;
            this.labelDeepSeekBaseUrl.Location = new System.Drawing.Point(12, 235);
            this.labelDeepSeekBaseUrl.Name = "labelDeepSeekBaseUrl";
            this.labelDeepSeekBaseUrl.Size = new System.Drawing.Size(102, 13);
            this.labelDeepSeekBaseUrl.TabIndex = 22;
            this.labelDeepSeekBaseUrl.Text = "Base URL DeepSeek:";
            // 
            // txtDeepSeekBaseUrl
            // 
            this.txtDeepSeekBaseUrl.Location = new System.Drawing.Point(12, 251);
            this.txtDeepSeekBaseUrl.Name = "txtDeepSeekBaseUrl";
            this.txtDeepSeekBaseUrl.Size = new System.Drawing.Size(381, 20);
            this.txtDeepSeekBaseUrl.TabIndex = 23;
            // 
            // labelMistralApiKey
            // 
            this.labelMistralApiKey.AutoSize = true;
            this.labelMistralApiKey.Location = new System.Drawing.Point(12, 280);
            this.labelMistralApiKey.Name = "labelMistralApiKey";
            this.labelMistralApiKey.Size = new System.Drawing.Size(107, 13);
            this.labelMistralApiKey.TabIndex = 24;
            this.labelMistralApiKey.Text = "Chave da API Mistral:";
            // 
            // txtMistralApiKey
            // 
            this.txtMistralApiKey.Location = new System.Drawing.Point(12, 296);
            this.txtMistralApiKey.Name = "txtMistralApiKey";
            this.txtMistralApiKey.Size = new System.Drawing.Size(381, 20);
            this.txtMistralApiKey.TabIndex = 25;
            // 
            // labelMistralModel
            // 
            this.labelMistralModel.AutoSize = true;
            this.labelMistralModel.Location = new System.Drawing.Point(12, 325);
            this.labelMistralModel.Name = "labelMistralModel";
            this.labelMistralModel.Size = new System.Drawing.Size(79, 13);
            this.labelMistralModel.TabIndex = 26;
            this.labelMistralModel.Text = "Modelo Mistral:";
            // 
            // txtMistralModel
            // 
            this.txtMistralModel.Location = new System.Drawing.Point(12, 341);
            this.txtMistralModel.Name = "txtMistralModel";
            this.txtMistralModel.Size = new System.Drawing.Size(381, 20);
            this.txtMistralModel.TabIndex = 27;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(12, 370);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(76, 13);
            this.label2.TabIndex = 8;
            this.label2.Text = "Modelo Groq:";
            // 
            // txtModel
            // 
            this.txtModel.Location = new System.Drawing.Point(12, 386);
            this.txtModel.Name = "txtModel";
            this.txtModel.Size = new System.Drawing.Size(381, 20);
            this.txtModel.TabIndex = 7;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(12, 415);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(95, 13);
            this.label3.TabIndex = 13;
            this.label3.Text = "Arquivo de noticias:";
            // 
            // txtNewsFile
            // 
            this.txtNewsFile.Location = new System.Drawing.Point(12, 431);
            this.txtNewsFile.Name = "txtNewsFile";
            this.txtNewsFile.Size = new System.Drawing.Size(300, 20);
            this.txtNewsFile.TabIndex = 14;
            // 
            // btnBrowse
            // 
            this.btnBrowse.Location = new System.Drawing.Point(318, 429);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(75, 23);
            this.btnBrowse.TabIndex = 15;
            this.btnBrowse.Text = "Selecionar";
            this.btnBrowse.Click += new System.EventHandler(this.btnBrowse_Click);
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(12, 460);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(97, 13);
            this.label4.TabIndex = 4;
            this.label4.Text = "Arquivo de Prompt:";
            // 
            // txtPromptFile
            // 
            this.txtPromptFile.Location = new System.Drawing.Point(12, 476);
            this.txtPromptFile.Name = "txtPromptFile";
            this.txtPromptFile.Size = new System.Drawing.Size(300, 20);
            this.txtPromptFile.TabIndex = 5;
            // 
            // btnBrowsePrompt
            // 
            this.btnBrowsePrompt.Location = new System.Drawing.Point(318, 474);
            this.btnBrowsePrompt.Name = "btnBrowsePrompt";
            this.btnBrowsePrompt.Size = new System.Drawing.Size(75, 23);
            this.btnBrowsePrompt.TabIndex = 6;
            this.btnBrowsePrompt.Text = "Selecionar";
            this.btnBrowsePrompt.Click += new System.EventHandler(this.btnBrowsePrompt_Click);
            // 
            // btnSave
            // 
            this.btnSave.Location = new System.Drawing.Point(12, 560);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(75, 23);
            this.btnSave.TabIndex = 10;
            this.btnSave.Text = "Salvar";
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(318, 560);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(75, 23);
            this.btnCancel.TabIndex = 9;
            this.btnCancel.Text = "Cancelar";
            this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
            // 
            // nudSummaryWordCount
            // 
            this.nudSummaryWordCount.Location = new System.Drawing.Point(351, 518);
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
            this.label5.Location = new System.Drawing.Point(245, 520);
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
            this.ClientSize = new System.Drawing.Size(520, 320);
            this.Controls.Add(this.labelMistralApiKey);
            this.Controls.Add(this.txtMistralApiKey);
            this.Controls.Add(this.labelMistralModel);
            this.Controls.Add(this.txtMistralModel);
            this.Controls.Add(this.labelDeepSeek);
            this.Controls.Add(this.txtDeepSeekApiKey);
            this.Controls.Add(this.labelDeepSeekModel);
            this.Controls.Add(this.txtDeepSeekModel);
            this.Controls.Add(this.labelDeepSeekBaseUrl);
            this.Controls.Add(this.txtDeepSeekBaseUrl);
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
            this.ConfigureTabs();
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "ConfigForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Configuracoes da IA";
            this.Load += new System.EventHandler(this.ConfigForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.nudSummaryWordCount)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void ConfigureTabs()
        {
            this.tabConfig.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabConfig.Name = "tabConfig";
            this.tabConfig.Padding = new System.Drawing.Point(12, 5);
            this.tabConfig.Controls.AddRange(new System.Windows.Forms.Control[] {
                this.tabGeral, this.tabGroq, this.tabGemini,
                this.tabDeepSeek, this.tabMistral, this.tabKimi, this.tabCategorias
            });

            ConfigurePage(this.tabGeral, "Geral");
            ConfigurePage(this.tabGroq, "Groq");
            ConfigurePage(this.tabGemini, "Gemini");
            ConfigurePage(this.tabDeepSeek, "DeepSeek");
            ConfigurePage(this.tabMistral, "Mistral");
            ConfigurePage(this.tabKimi, "Kimi");

            ConfigureGeneralTab();
            ConfigureProviderTabs();
            ConfigureCategoriesTab();
            this.Controls.Add(this.tabConfig);
            this.tabConfig.BringToFront();
        }

        private void ConfigureGeneralTab()
        {
            this.tabGeral.Controls.AddRange(new System.Windows.Forms.Control[] {
                this.labelProvider, this.cmbProvider,
                this.label3, this.txtNewsFile, this.btnBrowse,
                this.label4, this.txtPromptFile, this.btnBrowsePrompt,
                this.label5, this.nudSummaryWordCount,
                this.btnSave, this.btnCancel
            });
            ConfigureGeneralFields();
        }

        private void ConfigureGeneralFields()
        {
            SetField(this.labelProvider, this.cmbProvider, "Provider de IA", 20, 460);
            SetFileField(this.label3, this.txtNewsFile, this.btnBrowse, "Arquivo de noticias", 74);
            SetFileField(this.label4, this.txtPromptFile, this.btnBrowsePrompt, "Arquivo de prompt", 128);
            ConfigureGeneralActions();
        }

        private void ConfigureGeneralActions()
        {
            this.label5.Text = "Numero de palavras do resumo";
            this.label5.Location = new System.Drawing.Point(22, 182);
            this.nudSummaryWordCount.Location = new System.Drawing.Point(22, 202);
            this.nudSummaryWordCount.Size = new System.Drawing.Size(90, 20);
            SetButton(this.btnSave, "Salvar", 310, 232, 82);
            SetButton(this.btnCancel, "Cancelar", 400, 232, 82);
        }

        private void ConfigureProviderTabs()
        {
            AddProviderField(this.tabGroq, this.label1, this.txtApiKey, "API Key", 24);
            AddProviderField(this.tabGroq, this.label2, this.txtModel, "Modelo", 82);
            AddProviderField(this.tabGemini, this.labelGemini, this.txtGeminiApiKey, "API Key", 24);
            AddProviderField(this.tabGemini, this.labelGeminiModel, this.txtGeminiModel, "Modelo", 82);
            AddProviderField(this.tabDeepSeek, this.labelDeepSeek, this.txtDeepSeekApiKey, "API Key", 24);
            AddProviderField(this.tabDeepSeek, this.labelDeepSeekModel, this.txtDeepSeekModel, "Modelo", 82);
            AddProviderField(this.tabDeepSeek, this.labelDeepSeekBaseUrl, this.txtDeepSeekBaseUrl, "Base URL", 140);
            AddProviderField(this.tabMistral, this.labelMistralApiKey, this.txtMistralApiKey, "API Key", 24);
            AddProviderField(this.tabMistral, this.labelMistralModel, this.txtMistralModel, "Modelo", 82);
        }

        private void ConfigureCategoriesTab()
        {
            ConfigurePage(this.tabCategorias, "Categorias");
            this.clbTopics.Dock = System.Windows.Forms.DockStyle.Fill;
            this.clbTopics.CheckOnClick = true;
            this.clbTopics.HorizontalScrollbar = true;
            this.clbTopics.Name = "clbTopics";
            this.clbTopics.TabIndex = 0;
            this.clbTopics.ItemCheck += new System.Windows.Forms.ItemCheckEventHandler(this.clbTopics_ItemCheck);
            foreach (string code in NewsImpactRanker.WinForms.Models.TopicCatalog.Codes)
            {
                string name = NewsImpactRanker.WinForms.Models.TopicCatalog.CodeToName[code];
                this.clbTopics.Items.Add(code + " - " + name, true);
            }

            this.btnMarkAllTopics.Location = new System.Drawing.Point(12, 205);
            this.btnMarkAllTopics.Name = "btnMarkAllTopics";
            this.btnMarkAllTopics.Size = new System.Drawing.Size(115, 28);
            this.btnMarkAllTopics.TabIndex = 1;
            this.btnMarkAllTopics.Text = "Marcar todas";
            this.btnMarkAllTopics.UseVisualStyleBackColor = true;
            this.btnMarkAllTopics.Click += new System.EventHandler(this.btnMarkAllTopics_Click);

            this.btnUnmarkAllTopics.Location = new System.Drawing.Point(133, 205);
            this.btnUnmarkAllTopics.Name = "btnUnmarkAllTopics";
            this.btnUnmarkAllTopics.Size = new System.Drawing.Size(125, 28);
            this.btnUnmarkAllTopics.TabIndex = 2;
            this.btnUnmarkAllTopics.Text = "Desmarcar todas";
            this.btnUnmarkAllTopics.UseVisualStyleBackColor = true;
            this.btnUnmarkAllTopics.Click += new System.EventHandler(this.btnUnmarkAllTopics_Click);

            this.lblTopicCount.AutoSize = true;
            this.lblTopicCount.Location = new System.Drawing.Point(275, 212);
            this.lblTopicCount.Name = "lblTopicCount";
            this.lblTopicCount.Size = new System.Drawing.Size(110, 13);
            this.lblTopicCount.TabIndex = 3;
            this.lblTopicCount.Text = "Habilitadas: 26/26";

            this.tabCategorias.Controls.Add(this.clbTopics);
            this.tabCategorias.Controls.Add(this.btnMarkAllTopics);
            this.tabCategorias.Controls.Add(this.btnUnmarkAllTopics);
            this.tabCategorias.Controls.Add(this.lblTopicCount);
        }
        private static void ConfigurePage(System.Windows.Forms.TabPage page, string text)
        {
            page.BackColor = System.Drawing.SystemColors.Control;
            page.Padding = new System.Windows.Forms.Padding(3);
            page.Text = text;
        }

        private static void SetField(System.Windows.Forms.Label label, System.Windows.Forms.Control control, string text, int y, int width)
        {
            label.AutoSize = true;
            label.Location = new System.Drawing.Point(22, y);
            label.Text = text;
            control.Location = new System.Drawing.Point(22, y + 20);
            control.Size = new System.Drawing.Size(width, control.Height);
        }

        private static void SetFileField(System.Windows.Forms.Label label, System.Windows.Forms.TextBox textBox, System.Windows.Forms.Button button, string text, int y)
        {
            SetField(label, textBox, text, y, 362);
            SetButton(button, "Selecionar...", 390, y + 18, 92);
        }

        private static void SetButton(System.Windows.Forms.Button button, string text, int x, int y, int width)
        {
            button.Location = new System.Drawing.Point(x, y);
            button.Size = new System.Drawing.Size(width, 25);
            button.Text = text;
        }

        private static void AddProviderField(System.Windows.Forms.TabPage page, System.Windows.Forms.Label label, System.Windows.Forms.TextBox textBox, string text, int y)
        {
            SetField(label, textBox, text, y, 444);
            page.Controls.Add(label);
            page.Controls.Add(textBox);
        }

        private System.Windows.Forms.Label labelProvider;
        private System.Windows.Forms.ComboBox cmbProvider;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtApiKey;
        private System.Windows.Forms.Label labelGemini;
        private System.Windows.Forms.TextBox txtGeminiApiKey;
        private System.Windows.Forms.Label labelDeepSeek;
        private System.Windows.Forms.TextBox txtDeepSeekApiKey;
        private System.Windows.Forms.Label labelDeepSeekModel;
        private System.Windows.Forms.TextBox txtDeepSeekModel;
        private System.Windows.Forms.Label labelDeepSeekBaseUrl;
        private System.Windows.Forms.TextBox txtDeepSeekBaseUrl;
        private System.Windows.Forms.Label labelMistralApiKey;
        private System.Windows.Forms.TextBox txtMistralApiKey;
        private System.Windows.Forms.Label labelMistralModel;
        private System.Windows.Forms.TextBox txtMistralModel;
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
        private System.Windows.Forms.TabControl tabConfig;
        private System.Windows.Forms.TabPage tabGeral;
        private System.Windows.Forms.TabPage tabGroq;
        private System.Windows.Forms.TabPage tabGemini;
        private System.Windows.Forms.TabPage tabDeepSeek;
        private System.Windows.Forms.TabPage tabMistral;
        private System.Windows.Forms.TabPage tabKimi;
        private System.Windows.Forms.TabPage tabCategorias;
        private System.Windows.Forms.CheckedListBox clbTopics;
        private System.Windows.Forms.Button btnMarkAllTopics;
        private System.Windows.Forms.Button btnUnmarkAllTopics;
        private System.Windows.Forms.Label lblTopicCount;
        private System.Windows.Forms.Label labelGeminiModel;
        private System.Windows.Forms.TextBox txtGeminiModel;
    }
}
