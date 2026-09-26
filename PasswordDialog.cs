// ==================== PasswordDialog.cs ====================
using System;
using System.Drawing;
using System.Windows.Forms;

namespace ClassFirewall
{
    /// <summary>
    /// 密码输入对话框。两种用法：
    ///   · Verify —— 单个输入框，用来解锁
    ///   · SetNew —— 两个输入框（新密码 + 确认），留空表示取消密码保护
    ///
    /// 界面全部用代码搭建，不占用 Designer 文件。
    /// </summary>
    internal sealed class PasswordDialog : Form
    {
        private readonly TextBox _first;
        private readonly TextBox? _second;
        private readonly Label _error;
        private readonly bool _allowEmpty;

        private PasswordDialog(string title, string prompt, bool confirmMode, bool allowEmpty)
        {
            _allowEmpty = allowEmpty;

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(380, confirmMode ? 190 : 145);

            var label = new Label
            {
                Text = prompt,
                Location = new Point(16, 14),
                Size = new Size(348, 36)
            };

            _first = new TextBox
            {
                Location = new Point(16, 54),
                Size = new Size(348, 25),
                UseSystemPasswordChar = true
            };

            if (confirmMode)
            {
                _second = new TextBox
                {
                    Location = new Point(16, 88),
                    Size = new Size(348, 25),
                    UseSystemPasswordChar = true,
                    PlaceholderText = "再输入一次确认"
                };
                _first.PlaceholderText = "新密码";
            }

            _error = new Label
            {
                ForeColor = Color.Firebrick,
                Location = new Point(16, confirmMode ? 116 : 82),
                Size = new Size(348, 20),
                Text = ""
            };

            var ok = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                Location = new Point(190, confirmMode ? 146 : 110),
                Size = new Size(84, 30)
            };

            var cancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(280, confirmMode ? 146 : 110),
                Size = new Size(84, 30)
            };

            Controls.AddRange(new Control[] { label, _first, _error, ok, cancel });
            if (_second != null) Controls.Add(_second);

            AcceptButton = ok;
            CancelButton = cancel;

            // 自己接管"确定"，做非空/一致性校验后再放行
            ok.Click += (_, e) =>
            {
                string first = _first.Text;
                string? second = _second?.Text;

                if (_second != null)
                {
                    if (first.Length == 0 && !_allowEmpty)
                    {
                        _error.Text = "密码不能为空";
                        DialogResult = DialogResult.None;
                        return;
                    }
                    if (!string.Equals(first, second, StringComparison.Ordinal))
                    {
                        _error.Text = "两次输入不一致";
                        DialogResult = DialogResult.None;
                        return;
                    }
                }
                else if (first.Length == 0)
                {
                    _error.Text = "请输入密码";
                    DialogResult = DialogResult.None;
                }
            };

            Shown += (_, __) => _first.Focus();
        }

        /// <summary>弹出"输入密码"框。取消返回 null</summary>
        public static string? Verify(IWin32Window? owner, string title = "需要密码",
            string prompt = "请输入解锁密码：")
        {
            using var dlg = new PasswordDialog(title, prompt, confirmMode: false, allowEmpty: false);
            return dlg.ShowDialog(owner) == DialogResult.OK ? dlg._first.Text : null;
        }

        /// <summary>
        /// 弹出"设置新密码"框。
        /// 返回 true 表示用户确认了；<paramref name="newPassword"/> 为空串表示取消密码保护。
        /// </summary>
        public static bool SetNew(IWin32Window? owner, out string newPassword,
            string prompt = "设置一个密码，之后打开本程序需要输入它才能解锁。\r\n（留空并确定 = 取消密码保护）")
        {
            using var dlg = new PasswordDialog("设置密码", prompt, confirmMode: true, allowEmpty: true);
            bool ok = dlg.ShowDialog(owner) == DialogResult.OK;
            newPassword = ok ? dlg._first.Text : "";
            return ok;
        }
    }
}
