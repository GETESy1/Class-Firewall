// ==================== WintunLoader.cs ====================
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ClassFirewall
{
    /// <summary>
    /// 负责找到并加载 wintun.dll。
    ///
    /// 查找顺序：
    ///   1. 已加载（重复调用直接返回）
    ///   2. 本程序内嵌资源（把 wintun.dll 放到项目目录即可被打进单文件 exe）
    ///   3. exe 同目录
    ///   4. %LocalAppData%\ClassFirewall\
    ///
    /// 找不到时给出可操作的提示，而不是抛 DllNotFoundException 崩掉界面。
    /// </summary>
    internal static class WintunLoader
    {
        private static readonly object Gate = new();
        private static IntPtr _module;
        private static bool _initialized;
        private static string _error = "";

        public static string LoadedPath { get; private set; } = "";
        public static uint DriverVersion { get; private set; }

        public static string LastError => _error;

        /// <summary>驱动日志回调，便于把 wintun 自己的报错显示到 UI 日志框</summary>
        public static event Action<string>? OnDriverLog;

        private static WintunInterop.LoggerCallback? _loggerKeepAlive;

        public static bool IsInitialized
        {
            get { lock (Gate) return _initialized; }
        }

        public static string DriverVersionString =>
            _initialized ? WintunInterop.VersionString(DriverVersion) : "(未加载)";

        /// <summary>动态库目录（内嵌资源释放到这里）</summary>
        public static string SupportDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClassFirewall");

        /// <summary>
        /// 确保 wintun.dll 可用。失败时返回 false，并在 <see cref="LastError"/> 里给出原因。
        /// </summary>
        public static bool TryInitialize()
        {
            lock (Gate)
            {
                if (_initialized) return true;
                _error = "";

                foreach (var candidate in EnumerateCandidates())
                {
                    if (string.IsNullOrEmpty(candidate)) continue;

                    try
                    {
                        if (!File.Exists(candidate)) continue;
                    }
                    catch { continue; }

                    // ★ 先核对位数。32 位的 wintun.dll 无法加载进 64 位进程，
                    //   LoadLibrary 只会给出 Win32 错误 193，对用户毫无提示价值，
                    //   所以这里主动读 PE 头并给出可操作的说明。
                    string arch = DescribeArchitecture(candidate);
                    if (arch != "x64")
                    {
                        _error =
                            $"wintun.dll 位数不匹配：{candidate}\n" +
                            $"         该文件是 {arch} 版本，而本程序是 64 位 (x64)。\n" +
                            "         请下载 amd64 版本（压缩包里 wintun\\bin\\amd64\\wintun.dll）。";
                        continue;
                    }

                    if (TryLoad(candidate, out string why)) return true;
                    _error = why;
                }

                if (_error.Length == 0)
                {
                    _error =
                        "未找到 wintun.dll。请从 https://www.wintun.net 下载后，" +
                        $"放到程序目录，或放到 {SupportDirectory}\\ 下。";
                }
                return false;
            }
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateCandidates()
        {
            // 1) 内嵌资源：优先释放到磁盘再加载
            string? extracted = TryExtractEmbedded();
            if (extracted != null) yield return extracted;

            // 2) exe 同目录 / 当前目录
            string baseDir = AppContext.BaseDirectory;
            yield return Path.Combine(baseDir, WintunInterop.DllName);

            // 迭代器里不能把 yield 放进带 catch 的 try，所以先取出来再判断
            string cwd = "";
            try { cwd = Directory.GetCurrentDirectory(); } catch { }

            if (cwd.Length > 0 && !string.Equals(cwd, baseDir, StringComparison.OrdinalIgnoreCase))
                yield return Path.Combine(cwd, WintunInterop.DllName);

            // 3) LocalAppData
            yield return Path.Combine(SupportDirectory, WintunInterop.DllName);
        }

        private static string? TryExtractEmbedded()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var resName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(WintunInterop.DllName, StringComparison.OrdinalIgnoreCase));

                if (resName == null) return null;

                Directory.CreateDirectory(SupportDirectory);
                string target = Path.Combine(SupportDirectory, WintunInterop.DllName);

                using var stream = asm.GetManifestResourceStream(resName);
                if (stream == null) return null;

                // 已存在且大小一致就不重复释放
                if (File.Exists(target) && new FileInfo(target).Length == stream.Length)
                    return target;

                // 先写临时文件再替换，避免覆盖正在使用的 dll 导致失败
                string tmp = target + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    stream.CopyTo(fs);

                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(tmp, target);
                }
                catch
                {
                    // 覆盖失败（可能被其他进程占用）：退而使用临时文件
                    return tmp;
                }

                return target;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryLoad(string path, out string error)
        {
            error = "";
            try
            {
                // 先把绝对路径加载进进程，之后 DllImport("wintun.dll") 会复用它
                IntPtr handle = WintunInterop.LoadLibraryW(path);
                if (handle == IntPtr.Zero)
                {
                    int code = Marshal.GetLastWin32Error();
                    error = code == 193
                        ? $"{path} 无法加载：位数不匹配（Win32 193 = 不是有效的 Win32 应用程序）。" +
                          "本程序是 64 位，需要 amd64 版本的 wintun.dll。"
                        : $"加载 {path} 失败（Win32 错误 {code}）";
                    return false;
                }

                // 真正验证一下 DllImport 能否解析（同时留一个 keep-alive 的委托给驱动日志）
                _loggerKeepAlive = (logger, level, timestamp, message) =>
                {
                    try { OnDriverLog?.Invoke($"[Wintun/{level}] {message}"); } catch { }
                };
                WintunInterop.WintunSetLogger(_loggerKeepAlive);

                DriverVersion = WintunInterop.WintunGetRunningDriverVersion();

                _module = handle;
                LoadedPath = path;
                _initialized = true;
                return true;
            }
            catch (DllNotFoundException ex)
            {
                error = $"wintun.dll 已找到但无法加载: {ex.Message}";
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                error = $"wintun.dll 版本不兼容（缺少导出函数）: {ex.Message}";
                return false;
            }
            catch (BadImageFormatException)
            {
                error = $"{path} 不是有效的 wintun.dll（位数不匹配？本程序是 x64）";
                return false;
            }
            catch (Exception ex)
            {
                error = $"加载 wintun.dll 异常: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 读取 PE 头判断 DLL 位数。返回 "x64" / "x86" / "ARM64" 或错误说明。
        /// 抽成独立方法是为了能直接对真实的 wintun.dll 做单元测试。
        /// </summary>
        internal static string DescribeArchitecture(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                var dos = new byte[0x40];
                if (fs.Read(dos, 0, dos.Length) < dos.Length) return "文件过小，不完整";
                if (dos[0] != 'M' || dos[1] != 'Z') return "不是有效的 PE 文件";

                int peOffset = BitConverter.ToInt32(dos, 0x3C);
                if (peOffset <= 0) return "PE 头偏移非法";

                fs.Position = peOffset;
                var pe = new byte[6];
                if (fs.Read(pe, 0, pe.Length) < pe.Length) return "PE 头不完整";
                if (pe[0] != 'P' || pe[1] != 'E' || pe[2] != 0 || pe[3] != 0) return "PE 签名非法";

                ushort machine = (ushort)(pe[4] | (pe[5] << 8));
                switch (machine)
                {
                    case 0x014C: return "x86";
                    case 0x8664: return "x64";
                    case 0xAA64: return "ARM64";
                    default: return $"未知架构(0x{machine:X4})";
                }
            }
            catch (Exception ex)
            {
                return "读取失败: " + ex.Message;
            }
        }

        /// <summary>释放我们对 dll 的引用（驱动本身由 wintun 管理）</summary>
        public static void Shutdown()
        {
            lock (Gate)
            {
                if (_module != IntPtr.Zero)
                {
                    try { WintunInterop.FreeLibrary(_module); } catch { }
                    _module = IntPtr.Zero;
                }
                _initialized = false;
                LoadedPath = "";
            }
        }
    }
}
