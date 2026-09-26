namespace ClassFirewall
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.CheckedListBox _siteList;
        private System.Windows.Forms.Button _clearBtn;
        private System.Windows.Forms.Button _flushBtn;
        private System.Windows.Forms.Label _status;
        private System.Windows.Forms.TextBox _preview;
        private System.Windows.Forms.Label _tip;
        private System.Windows.Forms.Label _previewLabel;
        private System.Windows.Forms.FlowLayoutPanel _buttonsPanel;
        private System.Windows.Forms.TableLayoutPanel _rootLayout;
        private System.Windows.Forms.CheckBox _blockToggle;
        private System.Windows.Forms.CheckBox _autoStartToggle;
        private System.Windows.Forms.CheckBox _autoBlockToggle;
        private System.Windows.Forms.CheckBox _dohToggle;
        private System.Windows.Forms.FlowLayoutPanel _optionPanel;
        private System.Windows.Forms.TextBox _logBox;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _siteList = new System.Windows.Forms.CheckedListBox();
            _clearBtn = new System.Windows.Forms.Button();
            _flushBtn = new System.Windows.Forms.Button();
            _status = new System.Windows.Forms.Label();
            _preview = new System.Windows.Forms.TextBox();
            _tip = new System.Windows.Forms.Label();
            _previewLabel = new System.Windows.Forms.Label();
            _buttonsPanel = new System.Windows.Forms.FlowLayoutPanel();
            _rootLayout = new System.Windows.Forms.TableLayoutPanel();
            _blockToggle = new System.Windows.Forms.CheckBox();
            _autoStartToggle = new System.Windows.Forms.CheckBox();
            _autoBlockToggle = new System.Windows.Forms.CheckBox();
            _dohToggle = new System.Windows.Forms.CheckBox();
            _optionPanel = new System.Windows.Forms.FlowLayoutPanel();
            _logBox = new System.Windows.Forms.TextBox();
            _buttonsPanel.SuspendLayout();
            _optionPanel.SuspendLayout();
            _rootLayout.SuspendLayout();
            SuspendLayout();
            // 
            // _tip
            // 
            _tip.AutoSize = true;
            _tip.Location = new System.Drawing.Point(13, 10);
            _tip.MaximumSize = new System.Drawing.Size(580, 0);
            _tip.Name = "_tip";
            _tip.Padding = new System.Windows.Forms.Padding(0, 0, 0, 6);
            _tip.Size = new System.Drawing.Size(560, 46);
            _tip.TabIndex = 0;
            _tip.Text = "勾选要屏蔽的网站，勾选后立即生效。\r\n屏蔽方式：本地 DNS 把这些域名解析到 127.0.0.1，网站打不开。";
            // 
            // _siteList
            // 
            _siteList.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            _siteList.CheckOnClick = true;
            _siteList.Dock = System.Windows.Forms.DockStyle.Fill;
            _siteList.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            _siteList.IntegralHeight = false;
            _siteList.Location = new System.Drawing.Point(13, 59);
            _siteList.Name = "_siteList";
            _siteList.Size = new System.Drawing.Size(594, 250);
            _siteList.TabIndex = 1;
            // 
            // _buttonsPanel
            // 
            _buttonsPanel.AutoSize = true;
            _buttonsPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            _buttonsPanel.Controls.Add(_clearBtn);
            _buttonsPanel.Controls.Add(_flushBtn);
            _buttonsPanel.Location = new System.Drawing.Point(13, 315);
            _buttonsPanel.Name = "_buttonsPanel";
            _buttonsPanel.Padding = new System.Windows.Forms.Padding(0, 8, 0, 8);
            _buttonsPanel.Size = new System.Drawing.Size(220, 54);
            _buttonsPanel.TabIndex = 2;
            // 
            // _clearBtn
            // 
            _clearBtn.AutoSize = true;
            _clearBtn.Location = new System.Drawing.Point(0, 8);
            _clearBtn.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            _clearBtn.Name = "_clearBtn";
            _clearBtn.Padding = new System.Windows.Forms.Padding(10, 4, 10, 4);
            _clearBtn.Size = new System.Drawing.Size(99, 38);
            _clearBtn.TabIndex = 0;
            _clearBtn.Text = "全部解除";
            // 
            // _flushBtn
            // 
            _flushBtn.AutoSize = true;
            _flushBtn.Location = new System.Drawing.Point(107, 8);
            _flushBtn.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            _flushBtn.Name = "_flushBtn";
            _flushBtn.Padding = new System.Windows.Forms.Padding(10, 4, 10, 4);
            _flushBtn.Size = new System.Drawing.Size(105, 38);
            _flushBtn.TabIndex = 1;
            _flushBtn.Text = "刷新 DNS 缓存";
            // 
            // _previewLabel
            // 
            _previewLabel.AutoSize = true;
            _previewLabel.Location = new System.Drawing.Point(13, 375);
            _previewLabel.Name = "_previewLabel";
            _previewLabel.Size = new System.Drawing.Size(300, 20);
            _previewLabel.TabIndex = 3;
            _previewLabel.Text = "当前屏蔽的域名";
            // 
            // _preview
            // 
            _preview.BackColor = System.Drawing.Color.White;
            _preview.Dock = System.Windows.Forms.DockStyle.Fill;
            _preview.Font = new System.Drawing.Font("Consolas", 9F);
            _preview.Location = new System.Drawing.Point(13, 399);
            _preview.Multiline = true;
            _preview.Name = "_preview";
            _preview.ReadOnly = true;
            _preview.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            _preview.Size = new System.Drawing.Size(594, 80);
            _preview.TabIndex = 4;
            // 
            // _blockToggle
            // 
            _blockToggle.AutoSize = true;
            _blockToggle.Location = new System.Drawing.Point(0, 6);
            _blockToggle.Margin = new System.Windows.Forms.Padding(0, 6, 24, 4);
            _blockToggle.Name = "_blockToggle";
            _blockToggle.Size = new System.Drawing.Size(300, 24);
            _blockToggle.TabIndex = 0;
            _blockToggle.Text = "启用屏蔽（本地 DNS 127.0.0.1:53）";
            // 
            // _autoStartToggle
            // 
            _autoStartToggle.AutoSize = true;
            _autoStartToggle.Location = new System.Drawing.Point(324, 6);
            _autoStartToggle.Margin = new System.Windows.Forms.Padding(0, 6, 24, 4);
            _autoStartToggle.Name = "_autoStartToggle";
            _autoStartToggle.Size = new System.Drawing.Size(140, 24);
            _autoStartToggle.TabIndex = 1;
            _autoStartToggle.Text = "开机自动启动";
            // 
            // _autoBlockToggle
            // 
            _autoBlockToggle.AutoSize = true;
            _autoBlockToggle.Location = new System.Drawing.Point(0, 40);
            _autoBlockToggle.Margin = new System.Windows.Forms.Padding(0, 4, 0, 0);
            _autoBlockToggle.Name = "_autoBlockToggle";
            _autoBlockToggle.Size = new System.Drawing.Size(300, 24);
            _autoBlockToggle.TabIndex = 2;
            _autoBlockToggle.Text = "启动时自动恢复上次的屏蔽";
            // 
            // _dohToggle
            // 
            _dohToggle.AutoSize = true;
            _dohToggle.Location = new System.Drawing.Point(324, 40);
            _dohToggle.Margin = new System.Windows.Forms.Padding(24, 4, 0, 0);
            _dohToggle.Name = "_dohToggle";
            _dohToggle.Size = new System.Drawing.Size(340, 24);
            _dohToggle.TabIndex = 3;
            _dohToggle.Text = "阻止浏览器加密 DNS（DoH，需重启浏览器）";
            // 
            // _optionPanel
            // 
            _optionPanel.AutoSize = true;
            _optionPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            _optionPanel.Controls.Add(_blockToggle);
            _optionPanel.Controls.Add(_autoStartToggle);
            _optionPanel.Controls.Add(_autoBlockToggle);
            _optionPanel.Controls.Add(_dohToggle);
            _optionPanel.Location = new System.Drawing.Point(13, 489);
            _optionPanel.MaximumSize = new System.Drawing.Size(594, 0);   // 限制宽度，让开关自动换行
            _optionPanel.Name = "_optionPanel";
            _optionPanel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 6);
            _optionPanel.Size = new System.Drawing.Size(500, 70);
            _optionPanel.TabIndex = 5;
            // 
            // _logBox
            // 
            _logBox.BackColor = System.Drawing.Color.WhiteSmoke;
            _logBox.Dock = System.Windows.Forms.DockStyle.Fill;
            _logBox.Font = new System.Drawing.Font("Consolas", 9F);
            _logBox.Location = new System.Drawing.Point(13, 569);
            _logBox.Multiline = true;
            _logBox.Name = "_logBox";
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            _logBox.Size = new System.Drawing.Size(594, 140);
            _logBox.TabIndex = 6;
            // 
            // _status
            // 
            _status.Dock = System.Windows.Forms.DockStyle.Fill;
            _status.Location = new System.Drawing.Point(13, 709);
            _status.Name = "_status";
            _status.Padding = new System.Windows.Forms.Padding(2, 6, 2, 0);
            _status.Size = new System.Drawing.Size(594, 25);
            _status.TabIndex = 7;
            _status.Text = "就绪";
            _status.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // _rootLayout
            // 
            _rootLayout.ColumnCount = 1;
            _rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            _rootLayout.Controls.Add(_tip, 0, 0);
            _rootLayout.Controls.Add(_siteList, 0, 1);
            _rootLayout.Controls.Add(_buttonsPanel, 0, 2);
            _rootLayout.Controls.Add(_previewLabel, 0, 3);
            _rootLayout.Controls.Add(_preview, 0, 4);
            _rootLayout.Controls.Add(_optionPanel, 0, 5);
            _rootLayout.Controls.Add(_logBox, 0, 6);
            _rootLayout.Controls.Add(_status, 0, 7);
            _rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            _rootLayout.Location = new System.Drawing.Point(0, 0);
            _rootLayout.Name = "_rootLayout";
            _rootLayout.Padding = new System.Windows.Forms.Padding(10);
            _rootLayout.RowCount = 8;
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 86F));
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.Size = new System.Drawing.Size(620, 744);
            _rootLayout.TabIndex = 0;
            // 
            // MainForm
            // 
            ClientSize = new System.Drawing.Size(620, 744);
            Controls.Add(_rootLayout);
            Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            MinimumSize = new System.Drawing.Size(520, 600);
            Name = "MainForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "Class Firewall — 班级网站屏蔽";
            _buttonsPanel.ResumeLayout(false);
            _buttonsPanel.PerformLayout();
            _optionPanel.ResumeLayout(false);
            _optionPanel.PerformLayout();
            _rootLayout.ResumeLayout(false);
            _rootLayout.PerformLayout();
            ResumeLayout(false);
        }
    }
}
