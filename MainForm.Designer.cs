namespace ClassFirewall
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.CheckedListBox _siteList;
        private System.Windows.Forms.Button _applyBtn;
        private System.Windows.Forms.Button _clearBtn;
        private System.Windows.Forms.Button _flushBtn;
        private System.Windows.Forms.Label _status;
        private System.Windows.Forms.TextBox _preview;
        private System.Windows.Forms.Label _tip;
        private System.Windows.Forms.FlowLayoutPanel _buttonsPanel;
        private System.Windows.Forms.TableLayoutPanel _rootLayout;
        private System.Windows.Forms.CheckBox _sniToggle;
        private System.Windows.Forms.CheckBox _dnsToggle;
        private System.Windows.Forms.CheckBox _autoStartToggle;
        private System.Windows.Forms.CheckBox _autoBlockToggle;
        private System.Windows.Forms.FlowLayoutPanel _autoPanel;
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
            _applyBtn = new System.Windows.Forms.Button();
            _clearBtn = new System.Windows.Forms.Button();
            _flushBtn = new System.Windows.Forms.Button();
            _status = new System.Windows.Forms.Label();
            _preview = new System.Windows.Forms.TextBox();
            _tip = new System.Windows.Forms.Label();
            _buttonsPanel = new System.Windows.Forms.FlowLayoutPanel();
            _rootLayout = new System.Windows.Forms.TableLayoutPanel();
            _sniToggle = new System.Windows.Forms.CheckBox();
            _dnsToggle = new System.Windows.Forms.CheckBox();
            _autoStartToggle = new System.Windows.Forms.CheckBox();
            _autoBlockToggle = new System.Windows.Forms.CheckBox();
            _autoPanel = new System.Windows.Forms.FlowLayoutPanel();
            _logBox = new System.Windows.Forms.TextBox();
            _buttonsPanel.SuspendLayout();
            _autoPanel.SuspendLayout();
            _rootLayout.SuspendLayout();
            SuspendLayout();
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
            _siteList.Size = new System.Drawing.Size(594, 171);
            _siteList.TabIndex = 1;
            // 
            // _applyBtn
            // 
            _applyBtn.AutoSize = true;
            _applyBtn.Location = new System.Drawing.Point(0, 8);
            _applyBtn.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            _applyBtn.Name = "_applyBtn";
            _applyBtn.Padding = new System.Windows.Forms.Padding(10, 4, 10, 4);
            _applyBtn.Size = new System.Drawing.Size(99, 38);
            _applyBtn.TabIndex = 0;
            _applyBtn.Text = "应用屏蔽";
            // 
            // _clearBtn
            // 
            _clearBtn.AutoSize = true;
            _clearBtn.Location = new System.Drawing.Point(107, 8);
            _clearBtn.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            _clearBtn.Name = "_clearBtn";
            _clearBtn.Padding = new System.Windows.Forms.Padding(10, 4, 10, 4);
            _clearBtn.Size = new System.Drawing.Size(99, 38);
            _clearBtn.TabIndex = 1;
            _clearBtn.Text = "全部解除";
            // 
            // _flushBtn
            // 
            _flushBtn.AutoSize = true;
            _flushBtn.Location = new System.Drawing.Point(214, 8);
            _flushBtn.Margin = new System.Windows.Forms.Padding(0, 0, 8, 0);
            _flushBtn.Name = "_flushBtn";
            _flushBtn.Padding = new System.Windows.Forms.Padding(10, 4, 10, 4);
            _flushBtn.Size = new System.Drawing.Size(105, 38);
            _flushBtn.TabIndex = 2;
            _flushBtn.Text = "刷新 DNS";
            // 
            // _buttonsPanel
            // 
            _buttonsPanel.AutoSize = true;
            _buttonsPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            _buttonsPanel.Controls.Add(_applyBtn);
            _buttonsPanel.Controls.Add(_clearBtn);
            _buttonsPanel.Controls.Add(_flushBtn);
            _buttonsPanel.Location = new System.Drawing.Point(13, 236);
            _buttonsPanel.Name = "_buttonsPanel";
            _buttonsPanel.Padding = new System.Windows.Forms.Padding(0, 8, 0, 8);
            _buttonsPanel.Size = new System.Drawing.Size(327, 54);
            _buttonsPanel.TabIndex = 2;
            // 
            // _preview
            // 
            _preview.BackColor = System.Drawing.Color.White;
            _preview.Dock = System.Windows.Forms.DockStyle.Fill;
            _preview.Font = new System.Drawing.Font("Consolas", 9F);
            _preview.Location = new System.Drawing.Point(13, 296);
            _preview.Multiline = true;
            _preview.Name = "_preview";
            _preview.ReadOnly = true;
            _preview.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            _preview.Size = new System.Drawing.Size(594, 84);
            _preview.TabIndex = 3;
            // 
            // _tip
            // 
            _tip.AutoSize = true;
            _tip.Location = new System.Drawing.Point(13, 10);
            _tip.MaximumSize = new System.Drawing.Size(580, 0);
            _tip.Name = "_tip";
            _tip.Padding = new System.Windows.Forms.Padding(0, 0, 0, 6);
            _tip.Size = new System.Drawing.Size(416, 46);
            _tip.TabIndex = 0;
            _tip.Text = "勾选要屏蔽的网站";
            // 
            // _sniToggle
            // 
            _sniToggle.AutoSize = true;
            _sniToggle.Location = new System.Drawing.Point(10, 389);
            _sniToggle.Margin = new System.Windows.Forms.Padding(0, 6, 0, 4);
            _sniToggle.Name = "_sniToggle";
            _sniToggle.Size = new System.Drawing.Size(384, 24);
            _sniToggle.TabIndex = 4;
            _sniToggle.Text = "启用深度包检查（HTTP 80 + HTTPS 443，需管理员权限）";
            // 
            // _dnsToggle
            // 
            _dnsToggle.AutoSize = true;
            _dnsToggle.Location = new System.Drawing.Point(10, 423);
            _dnsToggle.Margin = new System.Windows.Forms.Padding(0, 4, 0, 6);
            _dnsToggle.Name = "_dnsToggle";
            _dnsToggle.Size = new System.Drawing.Size(500, 24);
            _dnsToggle.TabIndex = 5;
            _dnsToggle.Text = "接管系统 DNS（监听 127.0.0.1:53，自动切换网卡 DNS，关闭时还原）";
            // 
            // _autoStartToggle
            // 
            _autoStartToggle.AutoSize = true;
            _autoStartToggle.Location = new System.Drawing.Point(0, 6);
            _autoStartToggle.Margin = new System.Windows.Forms.Padding(0, 6, 20, 0);
            _autoStartToggle.Name = "_autoStartToggle";
            _autoStartToggle.Size = new System.Drawing.Size(200, 24);
            _autoStartToggle.TabIndex = 0;
            _autoStartToggle.Text = "开机自动启动";
            // 
            // _autoBlockToggle
            // 
            _autoBlockToggle.AutoSize = true;
            _autoBlockToggle.Location = new System.Drawing.Point(220, 6);
            _autoBlockToggle.Margin = new System.Windows.Forms.Padding(0, 6, 0, 0);
            _autoBlockToggle.Name = "_autoBlockToggle";
            _autoBlockToggle.Size = new System.Drawing.Size(260, 24);
            _autoBlockToggle.TabIndex = 1;
            _autoBlockToggle.Text = "启动时自动启用屏蔽（hosts + DPI + DNS）";
            // 
            // _autoPanel
            // 
            _autoPanel.AutoSize = true;
            _autoPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            _autoPanel.Controls.Add(_autoStartToggle);
            _autoPanel.Controls.Add(_autoBlockToggle);
            _autoPanel.Location = new System.Drawing.Point(13, 453);
            _autoPanel.Name = "_autoPanel";
            _autoPanel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 6);
            _autoPanel.Size = new System.Drawing.Size(500, 30);
            _autoPanel.TabIndex = 6;
            // 
            // _logBox
            // 
            _logBox.BackColor = System.Drawing.Color.WhiteSmoke;
            _logBox.Dock = System.Windows.Forms.DockStyle.Fill;
            _logBox.Font = new System.Drawing.Font("Consolas", 9F);
            _logBox.Location = new System.Drawing.Point(13, 489);
            _logBox.Multiline = true;
            _logBox.Name = "_logBox";
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            _logBox.Size = new System.Drawing.Size(594, 168);
            _logBox.TabIndex = 7;
            // 
            // _status
            // 
            _status.Dock = System.Windows.Forms.DockStyle.Fill;
            _status.Location = new System.Drawing.Point(13, 660);
            _status.Name = "_status";
            _status.Padding = new System.Windows.Forms.Padding(2, 6, 2, 0);
            _status.Size = new System.Drawing.Size(594, 25);
            _status.TabIndex = 8;
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
            _rootLayout.Controls.Add(_preview, 0, 3);
            _rootLayout.Controls.Add(_sniToggle, 0, 4);
            _rootLayout.Controls.Add(_dnsToggle, 0, 5);
            _rootLayout.Controls.Add(_autoPanel, 0, 6);
            _rootLayout.Controls.Add(_logBox, 0, 7);
            _rootLayout.Controls.Add(_status, 0, 8);
            _rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            _rootLayout.Location = new System.Drawing.Point(0, 0);
            _rootLayout.Name = "_rootLayout";
            _rootLayout.Padding = new System.Windows.Forms.Padding(10);
            _rootLayout.RowCount = 9;
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 45F));
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 90F));
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 55F));
            _rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle());
            _rootLayout.Size = new System.Drawing.Size(620, 700);
            _rootLayout.TabIndex = 0;
            // 
            // MainForm
            // 
            ClientSize = new System.Drawing.Size(620, 700);
            Controls.Add(_rootLayout);
            Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            MinimumSize = new System.Drawing.Size(480, 560);
            Name = "MainForm";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "Class Firewall(CFW) 班级黑名单网站屏蔽工具";
            _buttonsPanel.ResumeLayout(false);
            _buttonsPanel.PerformLayout();
            _autoPanel.ResumeLayout(false);
            _autoPanel.PerformLayout();
            _rootLayout.ResumeLayout(false);
            _rootLayout.PerformLayout();
            ResumeLayout(false);
        }
    }
}