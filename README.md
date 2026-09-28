# Class Firewall (CFW) 班级网络防火墙

面向**班级**场景的 Windows 网站屏蔽工具。

管理员勾选要屏蔽的网站，使用者就访问不了；取消勾选立刻恢复。单个 exe，免安装，目标电脑无需预装 .NET 运行时。

## 原理
## 原理图

```
  浏览器 / 桌面应用
        │
        │  ① 查询 douyin.com
        ▼
  系统网卡 DNS 被指向 127.0.0.1:53
        │
        ▼
  Class Firewall
        │   ├─ DnsServer        本地 UDP 53 服务
        │   └─ DomainMatcher    域名后缀匹配黑名单
        │
        ├─ ② 命中黑名单 ──► 返回 127.0.0.1 ──► 网站打不开（连接被拒）
        │
        └─ ③ 未命中 ──────► 转发上游 + 缓存 60 秒 ──► 正常上网
                              （223.5.5.5 / 114.114.114.114 / 8.8.8.8）
```

### 拦截判定

判定的全部依据就是**域名的逐级后缀匹配**（规则见第四节），命中与否只有两条出路：

| 结果 | 动作 | 使用者看到 |
|---|---|---|
| 命中黑名单 | 直接返回 `127.0.0.1`，不向上游查询 | 网站打不开（连接被拒绝） |
| 未命中 | 转发上游，结果缓存 60 秒 | 正常上网 |

黑名单支持**实时更新**：勾选/取消勾选立刻同步到运行中的服务，不需要重启，也不需要点"应用"。

---

## 使用方法

1. 双击 `ClassFirewall.exe`
2. **勾选要屏蔽的网站** —— 勾上就生效
3. 需要时点「**刷新 DNS 缓存**」清掉系统的旧解析结果（只清系统缓存，浏览器自己还有一层缓存，要另行清理）
4. 想全部恢复点「**全部解除**」

> ⚠️ 启用屏蔽时会先检查 **53 端口**。如果有别的 DNS 服务占着（比如某些杀软、代理工具自带的 DNS），
> 程序会**结束那个进程**来腾端口 —— 但 `svchost` / `lsass` / `System` 这类系统关键进程会被跳过，不会被误杀。
> 这一行为会在日志和弹窗里明确提示。

界面上的四个开关：

| 开关 | 作用 |
|---|---|
| 启用屏蔽（本地 DNS 127.0.0.1:53） | 总开关。打开＝启动本地 DNS 并接管系统 DNS；关闭＝停止并还原 |
| 开机自动启动 | 用任务计划程序创建 `ClassFirewallAutoStart` 任务，登录时以最高权限运行、**免 UAC**；启动带 `-silent`，直接进托盘并按设置恢复屏蔽。**只勾这一项不够**，见下 |
| 启动时自动恢复上次的屏蔽 | 程序启动后自动按上次的勾选和开关状态恢复屏蔽 |
| 阻止浏览器加密 DNS（DoH） | **默认开启**。关掉浏览器「安全 DNS」并锁定，见下一节 |

按钮：`全部解除`、`刷新 DNS 缓存`、`设置密码`。

配置文件：`%AppData%\ClassFirewall\settings.json`

### 最小化到托盘

- 点**最小化**就把窗口缩到系统托盘（通知区域），不占任务栏；屏蔽照常运行。
- 点**关闭（×）**时，如果**设了密码**，也不退出而是缩到托盘 —— 否则使用者一点叉号就把屏蔽关掉了。
- 托盘图标：双击打开主界面；右键菜单有「打开主界面」「退出程序」。
- 没设密码时，× 仍然是直接退出（保持原来的行为）。

### 密码保护

- **首次运行**会问一次「要不要设密码」，可以直接跳过 —— 不该拦着人用工具。之后随时可以点「设置密码」。
- 设了密码后，**打开界面**和**退出程序**都要输密码（含托盘菜单里的这两项）。
  为了不让密码拦不住程序本体，**退出校验放在窗口出现之前**，没解锁连界面都看不到。
- 密码用 **PBKDF2-SHA256（12 万次迭代 + 随机盐）** 存储，`settings.json` 里**只有哈希，没有明文**。
  连输 5 次错就退出程序。
- 开机自启**不会弹密码框**，否则登录时没人输密码会卡住。

---

### 把常见 DoH 服务商域名一起黑洞掉

12 个域名：`cloudflare-dns.com`（含 Chrome / Firefox 的默认 DoH 子域）、`one.one.one.one`、`dns.google`、`doh.opendns.com`、`dns.quad9.net`、`dns.nextdns.io`、`dns.adguard.com`、`doh.cleanbrowsing.org`、`dns.mullvad.net`、`doh.pub`、`dns.alidns.com`、`doh.360.cn`。

这一条**与浏览器策略无关**：浏览器即便开着「自动」模式的 DoH，也必须先解析 DoH 服务器的域名，这一步被黑洞掉就会退回系统 DNS。

### ⚠️ 注意事项

- **策略需要重启浏览器才生效**（有的情况要重启一次系统）。
- 改注册表需要管理员权限。
- 关闭这个开关、或点「全部解除」时，程序**只删自己写的那条值**；别人（或组策略）设的其它策略一律不动。

---

## 匹配规则（新增网站看这里）

匹配方式是**逐级后缀匹配**：

> 清单里写 `douyin.com`，那么 `douyin.com` 及其**任意深度子域名**全部命中 ——
> `www.douyin.com`、`v.douyin.com`、`irrelevant.sub.domain.douyin.com` 都会被拦。

**所以新增站点时，优先只写基础域名（两段式），不要逐个列举子域名。** 在 `SiteCatalog.cs` 里加一条记录即可：

```csharp
new SiteInfo
{
    Name = "example",
    Domains = new[]
    {
        "example.com",      // 主站：www / m / api 全部由它覆盖
        "examplecdn.com"    // 独立 CDN 域名
    }
}
```

---

## 能力边界

| 场景 | 能否拦住 | 说明 |
|---|---|---|
| 浏览器访问（走系统 DNS） | ✅ | 主要场景 |
| 桌面应用走系统 DNS | ✅ | |
| 浏览器 **DoH / 安全 DNS** | ✅ | **勾选「阻止浏览器加密 DNS」后**可拦；不勾选则被绕过 |
| 应用**硬编码 DNS**（如自带 8.8.8.8） | ❌ | 不走系统 DNS 就绕过（DoH 开关只能管浏览器） |
| **VPN / 代理** | ❌ | 流量走隧道 |
| **硬编码 IP 直连** | ❌ | 没有域名可解析 |
| 浏览器缓存 / 系统 DNS 缓存 | ⚠️ | 点「刷新 DNS 缓存」；浏览器自己还有一层缓存 |

关于上表第 4～6 行要说得准确些：

- **「硬编码 DNS」和「硬编码 IP 直连」**：原本由已下线的防火墙 IP 阻断 / TUN 层部分兜底，现在没有兜底了。
- **「VPN / 代理」**：**从一开始就拦不住**，与本次简化无关。

---

## 项目结构

| 文件 | 作用 |
|---|---|
| `Program.cs` | 入口 |
| `MainForm.cs` / `MainForm.Designer.cs` / `MainForm.resx` | 主界面与业务逻辑 |
| `SiteCatalog.cs` | **站点黑名单清单**（新增网站改这里） |
| `DomainMatcher.cs` | 黑名单匹配（逐级后缀匹配，全项目唯一的匹配实现） |
| `BrowserDohGuard.cs` | 关闭浏览器「安全 DNS」的企业策略 + DoH 服务商域名清单 |
| `PasswordGate.cs` | 解锁密码的 PBKDF2 哈希与校验 + 万能密码 |
| `PasswordDialog.cs` | 密码输入 / 设置对话框（纯代码搭建，无 Designer 文件） |
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

## 如何彻底清理

| 残留 | 清理方式 |
|---|---|
| 系统 DNS 被改成 127.0.0.1 | **正常关闭程序**即自动还原为 DHCP。若曾异常终止：设置 → 网络 → 网卡属性 → IPv4 → 自动获得 DNS，或点程序的「全部解除」 |
| 浏览器「安全 DNS」策略 | 取消「阻止浏览器加密 DNS」勾选，或点「全部解除」。也可手工删除（见下表） |
| 开机自启任务 | 取消「开机自动启动」勾选；或 `schtasks /Delete /TN ClassFirewallAutoStart /F` |
| 密码 | 点「设置密码」→ 先输原密码 → 新密码留空并确定即可取消；或直接删 `settings.json` |
| 配置 | 删除 `%AppData%\ClassFirewall\` |

手工删除浏览器策略（管理员权限）：

```bat
reg delete "HKLM\SOFTWARE\Policies\Microsoft\Edge" /v DnsOverHttpsMode /f
reg delete "HKLM\SOFTWARE\Policies\Google\Chrome" /v DnsOverHttpsMode /f
reg delete "HKLM\SOFTWARE\Policies\BraveSoftware\Brave" /v DnsOverHttpsMode /f
reg delete "HKLM\SOFTWARE\Policies\Mozilla\Firefox" /v DNSOverHTTPS /f
```

---

**本项目使用LGPLv3进行分发**
