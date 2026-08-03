using System;
using System.Windows.Forms;

namespace NewsImpactRanker.WinForms.Forms
{
    partial class MainForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.label1 = new System.Windows.Forms.Label();
            this.txtUrls = new System.Windows.Forms.TextBox();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnConfig = new System.Windows.Forms.Button();
            this.btnSummaryCache = new System.Windows.Forms.Button();
            this.btnViewPostedUrls = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblProgress = new System.Windows.Forms.Label();
            this.dgvResults = new System.Windows.Forms.DataGridView();
            this.colImpact = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colTitle = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colUrl = new System.Windows.Forms.DataGridViewLinkColumn();
            this.colCategory = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colReason = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colStatus = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colDate = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colAlreadyPosted = new System.Windows.Forms.DataGridViewButtonColumn();
            this.btnOpenReport = new System.Windows.Forms.Button();
            this.btnOpenLog = new System.Windows.Forms.Button();
            this.dgvTopicResults = new System.Windows.Forms.DataGridView();
            this.colTopic = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colTopicUrl = new System.Windows.Forms.DataGridViewLinkColumn();
            this.colTopicScore = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.lblInfo = new System.Windows.Forms.Label();
            this.btZeraCache = new System.Windows.Forms.Button();
            this.lblTotalCost = new System.Windows.Forms.Label();
            this.btnCopyCost = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.dgvResults)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvTopicResults)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(144, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Cole as URLs (uma por linha)";
            // 
            // txtUrls
            // 
            this.txtUrls.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtUrls.Location = new System.Drawing.Point(12, 25);
            this.txtUrls.Multiline = true;
            this.txtUrls.Name = "txtUrls";
            this.txtUrls.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtUrls.Size = new System.Drawing.Size(1156, 41);
            this.txtUrls.TabIndex = 1;
            // 
            // btnStart
            // 
            this.btnStart.Location = new System.Drawing.Point(12, 129);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(130, 23);
            this.btnStart.TabIndex = 4;
            this.btnStart.Text = "Iniciar Processamento";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // btnConfig
            // 
            this.btnConfig.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnConfig.Location = new System.Drawing.Point(1068, 129);
            this.btnConfig.Name = "btnConfig";
            this.btnConfig.Size = new System.Drawing.Size(100, 23);
            this.btnConfig.TabIndex = 5;
            this.btnConfig.Text = "ConfiguraÃ§Ãµes";
            this.btnConfig.UseVisualStyleBackColor = true;
            this.btnConfig.Click += new System.EventHandler(this.btnConfig_Click);
            // 
            // btnSummaryCache
            // 
            this.btnSummaryCache.Location = new System.Drawing.Point(424, 128);
            this.btnSummaryCache.Name = "btnSummaryCache";
            this.btnSummaryCache.Size = new System.Drawing.Size(160, 25);
            this.btnSummaryCache.TabIndex = 1004;
            this.btnSummaryCache.Text = "Ver cache de resumos";
            this.btnSummaryCache.UseVisualStyleBackColor = true;
            this.btnSummaryCache.Click += new System.EventHandler(this.btnSummaryCache_Click);
            // 
            // btnViewPostedUrls
            // 
            this.btnViewPostedUrls.Location = new System.Drawing.Point(590, 127);
            this.btnViewPostedUrls.Name = "btnViewPostedUrls";
            this.btnViewPostedUrls.Size = new System.Drawing.Size(160, 25);
            this.btnViewPostedUrls.TabIndex = 1005;
            this.btnViewPostedUrls.Text = "Ver URLs já postadas";
            this.btnViewPostedUrls.UseVisualStyleBackColor = true;
            this.btnViewPostedUrls.Click += new System.EventHandler(this.btnViewPostedUrls_Click);
            // 
            // progressBar
            // 
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(12, 158);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(1117, 23);
            this.progressBar.TabIndex = 6;
            // 
            // lblProgress
            // 
            this.lblProgress.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblProgress.AutoSize = true;
            this.lblProgress.Location = new System.Drawing.Point(1135, 168);
            this.lblProgress.Name = "lblProgress";
            this.lblProgress.Size = new System.Drawing.Size(24, 13);
            this.lblProgress.TabIndex = 7;
            this.lblProgress.Text = "0/0";
            // 
            // dgvResults
            // 
            this.dgvResults.AllowUserToAddRows = false;
            this.dgvResults.AllowUserToDeleteRows = false;
            this.dgvResults.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dgvResults.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvResults.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colImpact,
            this.colTitle,
            this.colUrl,
            this.colCategory,
            this.colReason,
            this.colStatus,
            this.colDate,
            this.colAlreadyPosted});
            this.dgvResults.Location = new System.Drawing.Point(12, 187);
            this.dgvResults.Name = "dgvResults";
            this.dgvResults.ReadOnly = true;
            this.dgvResults.RowHeadersVisible = false;
            this.dgvResults.Size = new System.Drawing.Size(1156, 390);
            this.dgvResults.TabIndex = 8;
            this.dgvResults.Visible = false;
            this.dgvResults.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvResults_CellContentClick);
            // 
            // colImpact
            // 
            this.colImpact.HeaderText = "Impacto";
            this.colImpact.Name = "colImpact";
            this.colImpact.ReadOnly = true;
            this.colImpact.Width = 60;
            // 
            // colTitle
            // 
            this.colTitle.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
            this.colTitle.HeaderText = "TÃ­tulo";
            this.colTitle.Name = "colTitle";
            this.colTitle.ReadOnly = true;
            // 
            // colUrl
            // 
            this.colUrl.HeaderText = "URL";
            this.colUrl.Name = "colUrl";
            this.colUrl.ReadOnly = true;
            this.colUrl.Width = 150;
            // 
            // colCategory
            // 
            this.colCategory.HeaderText = "Categoria";
            this.colCategory.Name = "colCategory";
            this.colCategory.ReadOnly = true;
            // 
            // colReason
            // 
            this.colReason.HeaderText = "Motivo";
            this.colReason.Name = "colReason";
            this.colReason.ReadOnly = true;
            this.colReason.Width = 150;
            // 
            // colStatus
            // 
            this.colStatus.HeaderText = "Status";
            this.colStatus.Name = "colStatus";
            this.colStatus.ReadOnly = true;
            this.colStatus.Width = 80;
            // 
            // colDate
            // 
            this.colDate.HeaderText = "Data";
            this.colDate.Name = "colDate";
            this.colDate.ReadOnly = true;
            this.colDate.Width = 120;
            // 
            // colAlreadyPosted
            // 
            this.colAlreadyPosted.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.None;
            this.colAlreadyPosted.HeaderText = "Já postada";
            this.colAlreadyPosted.MinimumWidth = 90;
            this.colAlreadyPosted.Name = "colAlreadyPosted";
            this.colAlreadyPosted.ReadOnly = true;
            this.colAlreadyPosted.Text = "Já postada";
            this.colAlreadyPosted.Width = 95;
            // 
            // btnOpenReport
            // 
            this.btnOpenReport.Location = new System.Drawing.Point(912, 129);
            this.btnOpenReport.Name = "btnOpenReport";
            this.btnOpenReport.Size = new System.Drawing.Size(150, 25);
            this.btnOpenReport.TabIndex = 9;
            this.btnOpenReport.Text = "Abrir Ranking (.txt)";
            this.btnOpenReport.UseVisualStyleBackColor = true;
            this.btnOpenReport.Click += new System.EventHandler(this.btnOpenReport_Click);
            // 
            // btnOpenLog
            // 
            this.btnOpenLog.Location = new System.Drawing.Point(756, 129);
            this.btnOpenLog.Name = "btnOpenLog";
            this.btnOpenLog.Size = new System.Drawing.Size(150, 25);
            this.btnOpenLog.TabIndex = 10;
            this.btnOpenLog.Text = "Abrir Log";
            this.btnOpenLog.UseVisualStyleBackColor = true;
            this.btnOpenLog.Click += new System.EventHandler(this.btnOpenLog_Click);
            // 
            // dgvTopicResults
            // 
            this.dgvTopicResults.AllowUserToAddRows = false;
            this.dgvTopicResults.AllowUserToDeleteRows = false;
            this.dgvTopicResults.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dgvTopicResults.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colTopic,
            this.colTopicUrl,
            this.colTopicScore});
            this.dgvTopicResults.Location = new System.Drawing.Point(12, 187);
            this.dgvTopicResults.Name = "dgvTopicResults";
            this.dgvTopicResults.ReadOnly = true;
            this.dgvTopicResults.RowHeadersVisible = false;
            this.dgvTopicResults.Size = new System.Drawing.Size(1156, 390);
            this.dgvTopicResults.TabIndex = 999;
            this.dgvTopicResults.CellContentClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgvTopicResults_CellContentClick);
            // 
            // colTopic
            // 
            this.colTopic.HeaderText = "Assunto";
            this.colTopic.Name = "colTopic";
            this.colTopic.ReadOnly = true;
            this.colTopic.Width = 220;
            // 
            // colTopicUrl
            // 
            this.colTopicUrl.HeaderText = "URL";
            this.colTopicUrl.Name = "colTopicUrl";
            this.colTopicUrl.ReadOnly = true;
            this.colTopicUrl.Width = 560;
            // 
            // colTopicScore
            // 
            this.colTopicScore.HeaderText = "Score";
            this.colTopicScore.Name = "colTopicScore";
            this.colTopicScore.ReadOnly = true;
            this.colTopicScore.Width = 80;
            // 
            // lblInfo
            // 
            this.lblInfo.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblInfo.Location = new System.Drawing.Point(12, 69);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(893, 56);
            this.lblInfo.TabIndex = 1000;
            this.lblInfo.Text = ".";
            this.lblInfo.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // btZeraCache
            // 
            this.btZeraCache.Location = new System.Drawing.Point(600, 129);
            this.btZeraCache.Name = "btZeraCache";
            this.btZeraCache.Size = new System.Drawing.Size(150, 25);
            this.btZeraCache.TabIndex = 1001;
            this.btZeraCache.Text = "Zerar o cache";
            this.btZeraCache.UseVisualStyleBackColor = true;
            // 
            // lblTotalCost
            // 
            this.lblTotalCost.AutoSize = true;
            this.lblTotalCost.Location = new System.Drawing.Point(12, 586);
            this.lblTotalCost.Name = "lblTotalCost";
            this.lblTotalCost.Size = new System.Drawing.Size(76, 13);
            this.lblTotalCost.TabIndex = 1002;
            this.lblTotalCost.Text = "Total: $0.0000";
            // 
            // btnCopyCost
            // 
            this.btnCopyCost.Location = new System.Drawing.Point(200, 581);
            this.btnCopyCost.Name = "btnCopyCost";
            this.btnCopyCost.Size = new System.Drawing.Size(110, 23);
            this.btnCopyCost.TabIndex = 1003;
            this.btnCopyCost.Text = "Copiar Custo";
            this.btnCopyCost.UseVisualStyleBackColor = true;
            this.btnCopyCost.Click += new System.EventHandler(this.btnCopyCost_Click);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1180, 620);
            this.Controls.Add(this.btnViewPostedUrls);
            this.Controls.Add(this.btnSummaryCache);
            this.Controls.Add(this.btZeraCache);
            this.Controls.Add(this.btnCopyCost);
            this.Controls.Add(this.lblTotalCost);
            this.Controls.Add(this.lblInfo);
            this.Controls.Add(this.btnOpenLog);
            this.Controls.Add(this.btnOpenReport);
            this.Controls.Add(this.dgvResults);
            this.Controls.Add(this.lblProgress);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.btnConfig);
            this.Controls.Add(this.btnStart);
            this.Controls.Add(this.txtUrls);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.dgvTopicResults);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "NewsImpactRanker - Classificador de Impacto de NotÃ­cias";
            this.Load += new System.EventHandler(this.MainForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.dgvResults)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.dgvTopicResults)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox txtUrls;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnConfig;
        private System.Windows.Forms.Button btnSummaryCache;
        private System.Windows.Forms.Button btnViewPostedUrls;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblProgress;
        private System.Windows.Forms.DataGridView dgvResults;
        private System.Windows.Forms.DataGridViewTextBoxColumn colImpact;
        private System.Windows.Forms.DataGridViewTextBoxColumn colTitle;
        private System.Windows.Forms.DataGridViewLinkColumn colUrl;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCategory;
        private System.Windows.Forms.DataGridViewTextBoxColumn colReason;
        private System.Windows.Forms.DataGridViewTextBoxColumn colStatus;
        private System.Windows.Forms.DataGridViewTextBoxColumn colDate;
        private System.Windows.Forms.DataGridViewButtonColumn colAlreadyPosted;
        private System.Windows.Forms.Button btnOpenReport;
        private System.Windows.Forms.Button btnOpenLog;

        private System.Windows.Forms.DataGridView dgvTopicResults;
        private System.Windows.Forms.DataGridViewTextBoxColumn colTopic;
        private System.Windows.Forms.DataGridViewLinkColumn colTopicUrl;
        private System.Windows.Forms.DataGridViewTextBoxColumn colTopicScore;
        private Label lblInfo;
        private Button btZeraCache;
        private Label lblTotalCost;
        private Button btnCopyCost;
    }
}


