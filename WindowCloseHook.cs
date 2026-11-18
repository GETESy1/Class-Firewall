using System;
using System.Runtime.InteropServices;

namespace ClassFirewall
{
    /// <summary>
    /// 拦截窗口的 WM_CLOSE（点 × / Alt+F4）。
    ///
    /// WinUI 3 的 Window.Closed 是「已经关了」，拦不住；要拦住必须在消息层
    /// 子类化窗口过程。这里用 comctl32 的 SetWindowSubclass（微软推荐的做法，
    /// 不需要自己保存旧 WndProc，而且与第三方子类化兼容）。
    /// </summary>
    internal sealed class WindowCloseHook : IDisposable
    {
        private const uint WM_CLOSE = 0x0010;
        private const uint WM_NCDESTROY = 0x0082;

        private readonly IntPtr _hwnd;
        private readonly Action _onCloseRequested;
        private readonly SubclassProc _proc;    // 必须保引用，否则被 GC 回收导致崩溃
        private bool _disposed;

        public WindowCloseHook(IntPtr hwnd, Action onCloseRequested)
        {
            _hwnd = hwnd;
            _onCloseRequested = onCloseRequested;
            _proc = WndProc;

            try
            {
                SetWindowSubclass(_hwnd, _proc, SubclassId, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                // 挂不上也不能让程序起不来：退化成「× = 直接退出」
                Program.WriteCrash("WindowCloseHook.SetWindowSubclass", ex);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            UIntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (msg == WM_CLOSE)
            {
                // 拦住默认的销毁，交给上层决定是「收进托盘」还是「真退出」
                try { _onCloseRequested(); } catch (Exception ex) { Program.WriteCrash("WindowCloseHook", ex); }
                return IntPtr.Zero;
            }

            if (msg == WM_NCDESTROY)
            {
                RemoveWindowSubclass(hWnd, _proc, SubclassId);
            }

            return DefSubclassProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { RemoveWindowSubclass(_hwnd, _proc, SubclassId); } catch { }
        }

        private static readonly UIntPtr SubclassId = new(0x43460001); // "CF"

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
            UIntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
            UIntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass,
            UIntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
