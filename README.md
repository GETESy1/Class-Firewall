# Class Firewall (CFW)

一个基于 C# / WinForms 的班级/家庭网站屏蔽工具。通过 **hosts + 本地 DNS + 深度包检查（DPI）+ Windows 防火墙** 四层防御，屏蔽指定娱乐网站。

---

## 📖 目录

- [功能特性](#-功能特性)
- [四层防御原理](#-四层防御原理)
- [项目结构](#-项目结构)
- [核心文件说明](#-核心文件说明)
- [快速开始](#-快速开始)
- [已知限制](#-已知限制)

---

## ✨ 功能特性

| 功能 | 说明 |
|------|------|
| **预设站点黑名单** | 内置抖音、快手、B站、爱奇艺、腾讯视频、网易云音乐等 20+ 国内主流娱乐网站 |
| **hosts 屏蔽** | 把黑名单域名指向 `127.0.0.1`，无需驱动即可生效 |
| **本地 DNS 接管** | 监听 `127.0.0.1:53`，命中黑名单返回 `127.0.0.1`，其余转发上游 |
| **深度包检查（DPI）** | HTTP 80 解析 `Host:` 头，HTTPS 443 解析 TLS ClientHello 的 SNI |
| **防火墙 IP 阻断** | 通过外部 DNS 解析真实 IP，写入 Windows 防火墙出站规则，**内核级阻断，任何应用都拦得住** |
| **自动结束端口占用** | 启动 DPI/DNS 前，自动结束占用 80/443/53 端口的非关键进程 |
| **一键解除** | 清空 hosts + 删除防火墙规则 + 还原系统 DNS |
| **状态持久化** | 勾选、开关状态自动保存到 `%AppData%\ClassFirewall\settings.json` |
| **开机自启** | 通过任务计划程序以最高权限静默启动，免 UAC |
| **自动恢复屏蔽** | 程序启动后自动重新应用上次的屏蔽配置 |

---

## 🛡 四层防御原理

```
┌─────────────────────────────────────────────────────────┐
│  浏览器 / 应用                                            │
└────────────┬────────────────────────────────────────────┘
             │
     ① hosts 文件  ────► 命中 → 127.0.0.1
             │
     ② 本地 DNS 127.0.0.1:53  ────► 命中 → 127.0.0.1
             │
     ③ DPI 代理 127.0.0.1:80/443  ────► 命中 → 断连/403
             │
     ④ Windows 防火墙  ────► 命中真实 IP → 内核层阻断
             │
             ▼
         目标网站
```

| 层级 | 拦截粒度 | 绕过难度 |
|------|---------|---------|
| ① **hosts** | 域名 → IP | 低（应用用私有 DNS 可绕） |
| ② **本地 DNS** | 域名 → IP | 中（DoH/DoT 可绕） |
| ③ **DPI** | TLS SNI / HTTP Host | 中（QUIC/ECH 可绕） |
| ④ **防火墙 IP 阻断** | 真实 IP | **高（除 VPN 外基本无解）** |

四层叠加，能拦住绝大多数场景。

---

## 📂 项目结构

```
ClassFirewall/
├── ClassFirewall.csproj      # 项目配置（含单文件发布）
├── app.manifest              # 要求管理员权限
├── Program.cs                # 入口（支持 -silent 参数）
├── MainForm.cs               # 主窗体业务逻辑
├── MainForm.Designer.cs      # 主窗体 UI 定义
├── MainForm.resx             # 主窗体资源
│
├── SiteCatalog.cs            # 内置站点域名清单
├── HostsManager.cs           # hosts 文件读写（含标记块管理）
├── DnsServer.cs              # 本地 DNS 服务器（UDP 53）
├── DnsConfigurator.cs        # 系统 DNS 自动切换/还原
├── DeepInspector.cs          # DPI：HTTP Host + TLS SNI 解析
├── ExternalDns.cs            # 用外部 DNS 解析真实 IP（绕过本机）
├── FirewallBlocker.cs        # Windows 防火墙规则同步
├── PortHelper.cs             # 查找/结束占用端口的进程
│
├── SettingsStore.cs          # 配置持久化（JSON）
└── AutoStartManager.cs       # 任务计划程序开机自启
```

---

## 🔑 核心文件说明

### `HostsManager.cs`
- hosts 路径：`C:\Windows\System32\drivers\etc\hosts`
- 用标记块 `# ==== Class Firewall Start ====` / `# ==== Class Firewall End ====` 包裹自写规则，不破坏用户原有内容
- 首次修改前自动备份到 `hosts.classfirewall.bak`
- 提供 `FlushDns()` 清空系统 DNS 缓存

### `DnsServer.cs`
- 监听 `127.0.0.1:53`（UDP）
- 黑名单 → 返回 `127.0.0.1`
- 白名单 → 转发上游（223.5.5.5 / 114.114.114.114 / 8.8.8.8）
- 内置 60 秒缓存，加速重复查询
- 设置 `SIO_UDP_CONNRESET` 避免 ICMP 错误中断监听

### `DeepInspector.cs`
- HTTP 80：读取 `Host:` 请求头，命中返回漂亮的 403 页面
- HTTPS 443：解析 TLS ClientHello 里的 SNI 字段，命中直接断开
- 仅当 hosts / DNS 把流量引到 `127.0.0.1` 时才收得到连接

### `ExternalDns.cs`
- 独立实现 DNS 查询报文，直接问外部 DNS
- **绕开本机 DNS 服务器**（否则黑名单域名会被解析成 127.0.0.1）

### `FirewallBlocker.cs`
- 用 PowerShell `New-NetFirewallRule` 创建出站阻断规则
- 规则统一挂在 `ClassFirewall` 组，方便批量清理
- 每 100 个 IP 一批，避免命令行过长

### `PortHelper.cs`
- 用 `netstat -ano` 找占用端口的进程
- 自动结束非关键进程（保护 `svchost`、`lsass`、`explorer` 等系统进程）

---

## 🚀 快速开始

### 环境要求

- Windows 10 / 11 (x64)
- .NET 8 SDK（开发）
- 管理员权限（运行）

### 开发调试

```bash
git clone <repo>
cd ClassFirewall
dotnet build
dotnet run
```

> 首次启动会弹 UAC，点击"是"。

### 使用流程

1. **勾选要屏蔽的网站**（如"抖音"、"快手"）
2. 点 **"应用屏蔽"** → 写入 hosts 文件
3. 勾选 **"启用深度包检查"** → 启动 HTTP/HTTPS 代理
4. 勾选 **"接管系统 DNS"** → 系统 DNS 切换为 `127.0.0.1`
5. （可选）勾选 **"开机自动启动"** 和 **"启动时自动启用屏蔽"**
6. 完成后可在日志框看到：
   ```
   ▶ 深度包检查已启动：HTTP 80, HTTPS 443
   ▶ DNS 服务器已启动 (127.0.0.1:53)，系统 DNS 已切换
   🔍 正在用外部 DNS 解析 N 个域名...
   ✅ 防火墙已阻断 XX 个 IP
   ```

### 解除屏蔽

点 **"全部解除"**：
- 清空 hosts 中的 CFW 标记块
- 删除所有 `ClassFirewall` 组防火墙规则
- 还原系统 DNS 为 DHCP 自动获取

---


## ⚠️ 已知限制

| 场景 | 能否拦住 | 说明 |
|------|---------|------|
| 浏览器访问黑名单 HTTPS | ✅ | hosts + DPI 双保险 |
| 应用（走系统 DNS） | ✅ | 防火墙 IP 阻断 |
| 应用（用 8.8.8.8 硬编码 DNS） | ✅ | 防火墙 IP 阻断 |
| 应用（用 DoH / DoT） | ✅ | 防火墙 IP 阻断 |
| **浏览器走 QUIC（UDP 443）** | ⚠ | 需关闭浏览器 QUIC，或用防火墙封 UDP 443 |
| **走 VPN / 代理** | ❌ | 流量走隧道，无法拦截 |
| **硬编码 IP 直连** | ❌ | 无域名可解析，无 SNI 可看 |
| **TLS 1.3 + ECH（加密 SNI）** | ❌ | SNI 被加密，需要 MITM 才能看到 |
| **CDN IP 变动** | ⚠ | 需定期重新"应用屏蔽"刷新 IP |

---

## 📄 许可证

仅用于**班级/家庭教育场景**，请勿用于商业用途。使用本工具造成的任何后果由使用者自行承担。

---

## 🤝 贡献

欢迎提交 Issue 和 PR。推荐先看 `SiteCatalog.cs`，添加新站点只需照格式加一条 `SiteInfo`：

```csharp
new SiteInfo
{
    Name = "站点名称",
    Domains = new[] { "example.com", "www.example.com" }
}
```

---

**本项目使用了AI辅助**
