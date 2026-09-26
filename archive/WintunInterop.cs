// ==================== WintunInterop.cs ====================
using System;
using System.Runtime.InteropServices;

namespace ClassFirewall
{
    /// <summary>
    /// Wintun 驱动 API 绑定（对应官方 wintun.h，0.14.x 稳定接口）。
    ///
    /// 注意：不做任何 P/Invoke 的静态初始化 —— 如果 wintun.dll 不存在，
    /// 直接调用这些方法会抛 DllNotFoundException，所以必须先经
    /// <see cref="WintunLoader"/> 确认已加载。
    /// </summary>
    internal static class WintunInterop
    {
        internal const string DllName = "wintun.dll";

        /// <summary>环回队列容量，官方示例用 0x400000（4 MiB）</summary>
        internal const uint DefaultRingCapacity = 0x400000;

        /// <summary>WintunReceivePacket 无数据时的等待事件超时</summary>
        internal const uint ReadWaitTimeoutMs = 500;

        [StructLayout(LayoutKind.Sequential)]
        internal struct NetLuid
        {
            public ulong Value;
        }

        /// <summary>日志级别，用于 WintunSetLogger</summary>
        internal enum LoggerLevel : uint
        {
            Info = 0,
            Warn = 1,
            Err = 2
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate void LoggerCallback(
            IntPtr logger,
            LoggerLevel level,
            ulong timestamp,
            [MarshalAs(UnmanagedType.LPWStr)] string message);

        // ---------------- 适配器生命周期 ----------------

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        internal static extern IntPtr WintunCreateAdapter(
            [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string tunnelType,
            IntPtr requestedGuid);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        internal static extern IntPtr WintunOpenAdapter(
            [MarshalAs(UnmanagedType.LPWStr)] string name);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        internal static extern void WintunCloseAdapter(IntPtr adapter);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        internal static extern void WintunDeleteDriver();

        // ★ 注意导出名是全大写 LUID（wintun.h 里就是 WintunGetAdapterLUID），
        //   写错大小写会在调用时抛 EntryPointNotFoundException。
        [DllImport(DllName, EntryPoint = "WintunGetAdapterLUID",
            CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WintunGetAdapterLuid(IntPtr adapter, out NetLuid luid);

        /// <summary>返回 MAKELONG(major, minor)；驱动未运行时返回 0</summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        internal static extern uint WintunGetRunningDriverVersion();

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        internal static extern void WintunSetLogger(LoggerCallback newLogger);

        // ---------------- 会话与数据面 ----------------

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        internal static extern IntPtr WintunStartSession(IntPtr adapter, uint capacity);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        internal static extern void WintunEndSession(IntPtr session);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        internal static extern IntPtr WintunGetReadWaitEvent(IntPtr session);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        internal static extern IntPtr WintunReceivePacket(IntPtr session, out uint packetSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        internal static extern void WintunReleaseReceivePacket(IntPtr session, IntPtr packet);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        internal static extern IntPtr WintunAllocateSendPacket(IntPtr session, uint packetSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Winapi)]
        internal static extern void WintunSendPacket(IntPtr session, IntPtr packet);

        // ---------------- Win32 辅助 ----------------

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadLibraryW(string fileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        internal const uint WAIT_OBJECT_0 = 0x00000000;
        internal const uint WAIT_TIMEOUT = 0x00000102;

        /// <summary>ERROR_NO_MORE_ITEMS：WintunReceivePacket 返回空时表示队列暂时为空</summary>
        internal const int ERROR_NO_MORE_ITEMS = 259;

        internal static string VersionString(uint version) =>
            version == 0 ? "(驱动未运行)" : $"{version & 0xFFFF}.{(version >> 16) & 0xFFFF}";
    }
}
