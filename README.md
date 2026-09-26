# Class Firewall (CFW)

面向**班级 / 家庭**场景的 Windows 网站屏蔽工具。

管理员勾选要屏蔽的网站，使用者就访问不了；取消勾选立刻恢复。单个 exe，免安装，目标电脑无需预装 .NET 运行时。

---

## 一、现在是什么样

**只做一层：本地 DNS 屏蔽。** 这是有意的取舍 —— 早期版本堆了四层防御（DPI / MITM / TUN / 防火墙），实测问题多、维护成本高，最终收敛成最简单可靠的一层。

原理：

```
① 程序在本机监听 127.0.0.1:53 当 DNS 服务器
② 把系统网卡的 DNS 指向 127.0.0.1
③ 命中黑名单的域名 → 一律解析到 127.0.0.1 → 网站打不开
④ 未命中的查询 → 转发上游（223.5.5.5 / 114.114.114.114 / 8.8.8.8），正常上网不受影响
⑤ 关闭程序时 → 自动把网卡 DNS 还原为自动获取（DHCP）
```

DNS 结果缓存 60 秒。黑名单支持**实时更新**：勾选/取消勾选立刻同步到运行中的服务，不需要重启，也不需要点"应用"。

---

## 二、使用方法

1. 双击 `ClassFirewall.exe`（会请求管理员权限，`app.manifest` 里已声明 `requireAdministrator`）
2. **勾选要屏蔽的网站** —— 勾上就生效
3. 需要时点「**刷新 DNS 缓存**」清掉系统和浏览器的旧解析结果
4. 想全部恢复点「**全部解除**」

界面上的三个开关：

| 开关 | 作用 |
|---|---|
| 启用屏蔽（本地 DNS 127.0.0.1:53） | 总开关。打开＝启动本地 DNS 并接管系统 DNS；关闭＝停止并还原 |
| 开机自动启动 | 用任务计划程序创建 `ClassFirewallAutoStart` 任务，以最高权限运行，**不弹 UAC** |
| 启动时自动恢复上次的屏蔽 | 程序启动后自动按上次的勾选和开关状态恢复屏蔽 |

配置文件：`%AppData%\ClassFirewall\settings.json`

---

## 三、匹配规则（新增网站看这里）

匹配方式是**逐级后缀匹配**：

> 清单里写 `douyin.com`，那么 `douyin.com` 及其**任意深度子域名**全部命中 ——
> `www.douyin.com`、`api-hl.amemv.douyin.com`、以及随便什么 `.douyin.com` 都会被拦。

**所以新增站点时，优先只写基础域名（两段式），不要逐个列举子域名。** 在 `SiteCatalog.cs` 里加一条记录即可：
```C#
new SiteInfo
            {
                Name = "哔哩哔哩 (Bilibili)",
                Domains = new[]
                {
                    "bilibili.com",     // 主站（www / m / api 等全部由它覆盖）
                    "hdslb.com",        // 图片 CDN（含 i0 / i1 / i2）
                    "bilivideo.com",    // 视频 CDN
                    "b23.tv"            // 短链
                }
            },
```

### 唯一的例外：共用基础域名不能整片封

有两个基础域名被**不相关的服务**共用，整片封会误伤正常使用，只能逐条列具体子域名：

| 基础域名 | 为什么不能封 | 现在的处理 |
|---|---|---|
| `qq.com` | QQ、**微信网页版**、QQ邮箱、腾讯网，以及大量第三方站点的 **QQ 授权登录** | 腾讯视频、QQ音乐只列 `v.qq.com` / `y.qq.com` 等具体子域 |
| `163.com` | 163邮箱、网易新闻 | 网易云音乐只列 `music.163.com`；它的 CDN 走 `126.net`（网易独立域名，可整片封） |

代价：`foo.qq.com` 这类"腾讯自己的其他子域"不会被拦。这是这个方案的固有边界。

清单里有单元测试守着这条底线（`mail.qq.com`、`www.163.com` 必须保持**放行**）。

---

## 四、构建与发布

```bat
dotnet publish -c Release        :: 或直接双击 publish.bat
```

产物：

```
bin\Release\net8.0-windows\win-x64\publish\ClassFirewall.exe
```

约 **63 MB 单文件**，配置要点（`ClassFirewall.csproj`）：

| 属性 | 值 | 作用 |
|---|---|---|
| `SelfContained` | `true` | 目标电脑无需预装 .NET 8 |
| `PublishSingleFile` | `true` | 打包成单个 exe |
| `EnableCompressionInSingleFile` | `true` | 单文件压缩（**必须先 `SelfContained=true`**，否则报 NETSDK1176） |
| `RuntimeIdentifier` | `win-x64` | |
| `DebugType` | `none` | 不打包调试符号，减小体积 |

> 发布时如果报 `Access to the path ...ClassFirewall.exe is denied`，说明程序正在运行、文件被锁。
> **先正常关闭它**（点关闭按钮，让 `FormClosing` 还原网卡 DNS），再重新发布。

---

## 五、能力边界（诚实声明）

| 场景 | 能否拦住 | 说明 |
|---|---|---|
| 浏览器访问（走系统 DNS） | ✅ | 主要场景 |
| 桌面应用走系统 DNS | ✅ | |
| 应用**硬编码 DNS**（如自带 8.8.8.8） | ❌ | 不走系统 DNS 就绕过 |
| 浏览器开启 **DoH / 安全 DNS** | ❌ | 加密 DNS，同上 |
| **VPN / 代理** | ❌ | 流量走隧道 |
| **硬编码 IP 直连** | ❌ | 没有域名可解析 |
| 浏览器缓存 / 系统 DNS 缓存 | ⚠️ | 点「刷新 DNS 缓存」；浏览器自己还有一层缓存 |

后三行原本由已下线的 DPI / TUN / 防火墙层负责，现在没有了 —— 这是简化的代价。

---

## 六、项目结构

| 文件 | 作用 |
|---|---|
| `Program.cs` | 入口 |
| `MainForm.cs` / `MainForm.Designer.cs` / `MainForm.resx` | 主界面与业务逻辑 |
| `SiteCatalog.cs` | **站点黑名单清单**（新增网站改这里） |
| `DomainMatcher.cs` | 黑名单匹配（后缀匹配，DNS 层唯一实现） |
| `DnsServer.cs` | 本地 DNS 服务器（UDP 53，含缓存与上游转发） |
| `DnsConfigurator.cs` | 切换 / 还原系统网卡 DNS，刷新 DNS 缓存 |
| `PortHelper.cs` | 检测并释放 53 端口占用（保护系统关键进程不被误杀） |
| `SettingsStore.cs` | 配置持久化 |
| `AutoStartManager.cs` | 任务计划程序开机自启 |
| `LegacyHostsCleaner.cs` | 升级清理：摘掉旧版本写在 hosts 里的屏蔽块 |
| `LegacyFirewallCleaner.cs` | 升级清理：删掉旧版本写入的防火墙规则 |
| `app.manifest` | 管理员权限（**UTF-8 无 BOM，首行直接是 `<assembly>`，无 XML 声明**） |
| `publish.bat` | 一键发布（`chcp 65001`） |

---

## 七、已下线的功能（`archive/`）

这些曾经实现过，后来因为复杂度 / 可靠性问题下线。代码**移到了 `archive/`，不参与编译**（`csproj` 里有 `<Compile Remove="archive/**" />`），需要时可移回项目根目录恢复。

| 文件 | 原功能 | 下线原因 |
|---|---|---|
| `DeepInspector.cs` | DPI：解析 HTTP `Host` 头与 TLS SNI | |
| `CertificateAuthority.cs` | MITM 自签根证书 + 域名证书签发 | 为了显示 403 页面而接管 80/443 端口并往系统装根证书，代价远大于收益 |
| `DpiSelfTest.cs` | DPI 链路自检 | 随 DPI 一起下线 |
| `TunEngine.cs` / `TunRouteManager.cs` | TUN 模式：用户态接管流量 | 需要 wintun 驱动，且不集成 tun2socks 就无法做全网接管 |
| `WintunInterop.cs` / `WintunLoader.cs` / `wintun.dll` | Wintun 驱动绑定与加载 | 同上 |
| `PacketCodec.cs` | IPv4/TCP/UDP/DNS 报文编解码 | TUN 的数据面 |
| `ExternalDns.cs` | 外部 DNS 解析真实 IP | 曾用于防火墙 / TUN 的 IP 层 |

---

## 八、如何彻底清理

| 残留 | 清理方式 |
|---|---|
| 系统 DNS 被改成 127.0.0.1 | **正常关闭程序**即自动还原为 DHCP。若曾异常终止：设置 → 网络 → 网卡属性 → IPv4 → 自动获得 DNS，或点程序的「全部解除」 |
| 开机自启任务 | 取消「开机自动启动」勾选；或 `schtasks /Delete /TN ClassFirewallAutoStart /F` |
| MITM 根证书（仅早期版本装过） | 现在的界面已无此按钮。用 `certutil -delstore Root "ClassFirewall Local CA"`，或在 `certlm.msc` → 受信任的根证书颁发机构 里删除 |
| hosts 遗留屏蔽块 | 程序启动时自动清理。备份文件 `drivers\etc\hosts.classfirewall.bak` 需手动删除 |
| 防火墙遗留规则 | 程序启动时自动清理 |
| 配置 | 删除 `%AppData%\ClassFirewall\` |

---

## 九、踩过的坑（开发记录）

留着给后来人，都是真实发生过、且有测试或现场日志佐证的。

| 问题 | 根因 | 解决 |
|---|---|---|
| **并行配置错误** | `app.manifest` 带 BOM 和 XML 声明 | 存成 UTF-8 无 BOM，首行直接 `<assembly>` |
| **DNS 收到 ICMP 后崩溃** | UDP socket 传播 ICMP 错误 | 设 `SIO_UDP_CONNRESET`；捕获 `ConnectionReset` 时 `continue` 而不是终止监听 |
| **子域名没被拦住** | 后缀匹配写成 `Substring(idx)`，得到带前导点的 `".douyin.com"`，永远匹配不到清单里的 `douyin.com`。这段逻辑**曾在三个类里各写一份，其中两份都错了** | 合并成唯一的 `DomainMatcher.cs`，配单元测试 |
| **MITM 的 403 页面从来出不来** | 解析 SNI 时已把 ClientHello 从 socket 读走，再交给 `SslStream` 时它收不到 ClientHello，一直阻塞到 8 秒超时（日志：`TLS 握手失败: The operation was canceled`） | 加了 `PrefixedStream` 回放已消费的字节 |
| **回放之后仍然握手超时** | `PrefixedStream` 把**零长度读**误判为"前缀已耗尽"并转发到底层 socket，而 socket 的零长度读会阻塞等数据 → 死锁。现场日志 `回放 0/174 字节, 底层读取 1 次, 写出 0 次` | 三个 `Read` 重载都加零长度短路（`Stream` 契约：零长度读必须立即返回 0） |
| **wintun.dll 加载失败** | 下载的是 x86 版本，装不进 64 位进程，`LoadLibrary` 只报 Win32 193 | 加载前先读 PE 头判断位数，给出可操作提示；并发现绑定的导出名写错大小写（应为 `WintunGetAdapterLUID`） |
| **程序崩了会断网** | 网卡 DNS 指向 127.0.0.1 而进程已死 → 没有任何 DNS 解析 | 启动时自愈 + 关闭/解除时无条件还原 DHCP；**切勿直接杀进程，要走正常关闭** |
| **架构决策：不做 0.0.0.0/0 全网接管** | 不集成 tun2socks 就没有用户态 TCP 栈，全量劫持路由会导致**未命中流量无法转发**，等于整机断网 | 原 TUN 实现改用选择性 `/32` 路由 + 启动自愈，崩溃也不会断网。该实现现已一并下线 |
| **单文件发布报 NETSDK1176** | `EnableCompressionInSingleFile` 要求 `SelfContained=true` | 两者配对使用 |

---

## 十、给接手者的话

1. **保持简单。** 这个项目最大的教训是：能可靠工作的一层，胜过堆三层花哨但修不好的。新增功能前先问"这层坏了会不会让用户断网"。
2. **改 `SiteCatalog.cs` 就够了**，别动匹配逻辑 —— 现有规则有测试守着。
3. **不要引入需要额外安装的依赖**（Node.js / Python / 驱动），必须保持单文件发布。
4. **`.NET 8` + `WinForms`**，不要引入 WPF / WinUI。
5. **`app.manifest` 必须 UTF-8 无 BOM。**
6. **碰 53 端口的释放要谨慎**，`PortHelper` 里保护了 `svchost` / `lsass` / `System` 等关键进程。
7. **所有拦截 / 错误事件都带时间戳打到界面日志框**，排查问题几乎全靠它。

**本项目使用了AI辅助**

