using System;
using System.Runtime.InteropServices;

namespace ClassFirewall
{
    /// <summary>
    /// 系统托盘图标 + 右键菜单。
    ///
    /// WinUI 3 没有 NotifyIcon（那是 WinForms 的），所以这里直接调 Shell_NotifyIcon。
    /// 实现方式：
    ///   · 注册一个不可见的普通窗口（消息窗口）接收托盘事件；
    ///   · 右键用 CreatePopupMenu / TrackPopupMenu 弹原生菜单；
    ///   · 图标从当前 exe 里用 ExtractIconEx 取，不额外带文件。
    /// </summary>
    internal sealed class TrayIcon : IDisposable
    {
        private const int WM_APP = 0x8000;
        private const int WM_TRAYICON = WM_APP + 1;
        private const int WM_COMMAND = 0x0111;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;
        private const int WM_NCDESTROY = 0x0082;
        private const int WM_NULL = 0x0000;
        private const int NIN_SELECT = 0x0400;
        private const int NIN_KEYSELECT = 0x0401;

        private const int MF_STRING = 0x0000;
        private const int MF_SEPARATOR = 0x0800;
        private const int TPM_RETURNCMD = 0x0100;
        private const int TPM_RIGHTBUTTON = 0x0002;
        private const int TPM_LEFTALIGN = 0x0000;
        private const int TPM_NONOTIFY = 0x0080;

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;
        private const int NIF_SHOWTIP = 0x00000080;

        private const int ID_OPEN = 1;
        private const int ID_EXIT = 2;

        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly Action _onOpen;
        private readonly Action _onExit;
        private readonly string _tip;

        private IntPtr _hwnd;
        private IntPtr _hicon;
        private WndProcDelegate? _wndProc;      // 必须保引用，否则被 GC 回收导致崩溃
        private bool _added;
        private bool _disposed;

        public TrayIcon(string tooltip, Action onOpen, Action onExit)
        {
            _tip = tooltip ?? "Class Firewall";
            _onOpen = onOpen;
            _onExit = onExit;
        }

        /// <summary>把图标挂到通知区域（重复调用无副作用）</summary>
        public void Show()
        {
            if (_disposed) return;
            EnsureWindow();

            _hicon = ExtractAppIcon() ;
            ApplyIcon();

            if (!_added)
            {
                var data = BuildData();
                if (Shell_NotifyIcon(NIM_ADD, ref data))
                    _added = true;
            }
            else
            {
                var data = BuildData();
                Shell_NotifyIcon(NIM_MODIFY, ref data);
            }
        }

        /// <summary>把图标从通知区域撤掉（不销毁窗口）</summary>
        public void Hide()
        {
            if (!_added) return;

            var data = BuildData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { Hide(); } catch { }
            try { if (_hicon != IntPtr.Zero) DestroyIcon(_hicon); } catch { }
            _hicon = IntPtr.Zero;

            if (_hwnd != IntPtr.Zero)
            {
                try { DestroyWindow(_hwnd); } catch { }
                _hwnd = IntPtr.Zero;
            }
        }

        // ---------------- 窗口与消息 ----------------

        private void EnsureWindow()
        {
            if (_hwnd != IntPtr.Zero) return;

            _wndProc = WindowProc;

            var hInstance = GetModuleHandle(null);
            string className = "ClassFirewallTrayWindow_" + Guid.NewGuid().ToString("N");

            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                style = 0,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInstance,
                lpszClassName = className
            };

            if (RegisterClassEx(ref wc) == 0)
                return;

            _hwnd = CreateWindowEx(
                WS_EX_TOOLWINDOW, className, "ClassFirewallTray",
                WS_POPUP, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_TRAYICON:
                {
                    int evt = lParam.ToInt32() & 0xFFFF;
                    if (evt is WM_LBUTTONUP or WM_LBUTTONDBLCLK or NIN_SELECT or NIN_KEYSELECT)
                    {
                        _onOpen?.Invoke();
                        return IntPtr.Zero;
                    }
                    if (evt is WM_RBUTTONUP or WM_CONTEXTMENU)
                    {
                        ShowMenu();
                        return IntPtr.Zero;
                    }
                    break;
                }

                case WM_COMMAND:
                {
                    int id = wParam.ToInt32() & 0xFFFF;
                    if (id == ID_OPEN) _onOpen?.Invoke();
                    else if (id == ID_EXIT) _onExit?.Invoke();
                    return IntPtr.Zero;
                }

                case WM_NCDESTROY:
                    break;
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void ShowMenu()
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return;

            try
            {
                AppendMenu(menu, MF_STRING, ID_OPEN, "打开主界面");
                AppendMenu(menu, MF_SEPARATOR, 0, null);
                AppendMenu(menu, MF_STRING, ID_EXIT, "退出程序");

                GetCursorPos(out POINT pt);

                // 这两句是微软文档要求的：否则菜单点外面不会消失
                SetForegroundWindow(_hwnd);

                int cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY,
                    pt.X, pt.Y, _hwnd, IntPtr.Zero);

                if (cmd != 0)
                    PostMessage(_hwnd, WM_COMMAND, new IntPtr(cmd), IntPtr.Zero);
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        // ---------------- NOTIFYICONDATA ----------------

        private NOTIFYICONDATAW BuildData() => new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hicon,
            szTip = _tip
        };

        private void ApplyIcon()
        {
            if (_hicon == IntPtr.Zero)
                _hicon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);
        }

        /// <summary>从当前 exe 里取应用图标；取不到就退回系统默认图标</summary>
        private static IntPtr ExtractAppIcon()
        {
            try
            {
                string exe = Environment.ProcessPath ?? "";
                if (string.IsNullOrEmpty(exe)) return LoadIcon(IntPtr.Zero, IDI_APPLICATION);

                var icons = new IntPtr[1];
                int n = ExtractIconEx(exe, 0, icons, null, 1);
                if (n > 0 && icons[0] != IntPtr.Zero) return icons[0];
            }
            catch { }

            return LoadIcon(IntPtr.Zero, IDI_APPLICATION);
        }

        private static readonly IntPtr IDI_APPLICATION = new(32512);

        // ---------------- P/Invoke ----------------

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string? lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hMenu, int uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATAW lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int ExtractIconEx(string lpszFile, int nIconIndex,
            IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, int nIcons);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
