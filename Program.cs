using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ClassFirewall
{
    /// <summary>
    /// 进程入口。自定义 Main 的原因：
    ///   · 需要在 XAML 框架起来之前解析 <c>-silent</c>（开机自启用）；
    ///   · 需要装好崩溃日志钩子，WinUI 3 的未处理异常不会自己落盘。
    /// </summary>
    internal static class Program
    {
        /// <summary>开机自启传入 -silent：直接进托盘，不弹任何窗口</summary>
        public static bool SilentMode { get; private set; }

        public static string[] StartupArgs { get; private set; } = Array.Empty<string>();

        private static readonly string CrashLog = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClassFirewall", "crash.log");

        [STAThread]
        private static void Main(string[] args)
        {
            StartupArgs = args ?? Array.Empty<string>();
            SilentMode = StartupArgs.Any(a => a.Equals("-silent", StringComparison.OrdinalIgnoreCase));

            InstallCrashLogging();

            // 同一时间只允许一个实例（两个实例抢 53 端口只会互相打架）
            if (!SingleInstance.TryAcquire())
                return;

            WinRT.ComWrappersSupport.InitializeComWrappers();

            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);

                new App();
            });
        }

        private static void InstallCrashLogging()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                WriteCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);

            AppDomain.CurrentDomain.ProcessExit += (_, __) => SingleInstance.Release();
        }

        internal static void WriteCrash(string source, Exception? ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLog)!);

                var sb = new StringBuilder();
                sb.AppendLine("==================== 崩溃 ====================");
                sb.AppendLine($"时间    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"来源    : {source}");
                sb.AppendLine($"静默模式: {SilentMode}");
                sb.AppendLine($"命令行  : {Environment.CommandLine}");
                sb.AppendLine($"异常    : {ex?.GetType().FullName ?? "(非 Exception 对象)"}");
                sb.AppendLine($"消息    : {ex?.Message}");
                sb.AppendLine(ex?.StackTrace);

                File.AppendAllText(CrashLog, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    /// <summary>命名互斥体实现的单实例锁。</summary>
    internal static class SingleInstance
    {
        private const string MutexName = @"Local\ClassFirewall.SingleInstance";
        private static Mutex? _mutex;
        private static bool _owned;

        public static bool TryAcquire()
        {
            try
            {
                _mutex = new Mutex(initiallyOwned: false, MutexName);
                try
                {
                    _owned = _mutex.WaitOne(TimeSpan.FromMilliseconds(200));
                    return _owned;
                }
                catch (AbandonedMutexException)
                {
                    // 上一个实例非正常退出，这里也算拿到
                    _owned = true;
                    return true;
                }
            }
            catch
            {
                // 拿不到互斥体不应该阻止程序启动
                return true;
            }
        }

        public static void Release()
        {
            try
            {
                if (_owned) _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch { }
        }
    }
}
