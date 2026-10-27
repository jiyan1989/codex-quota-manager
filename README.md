# Codex 任务托管

**简体中文 · [English](README.en.md)**

[![最新稳定版](https://img.shields.io/github/v/release/jiyan1989/codex-quota-manager?style=flat&logo=github&logoColor=white&label=version&color=2563eb)](https://github.com/jiyan1989/codex-quota-manager/releases/latest)
[![GitHub 收藏数量](https://img.shields.io/github/stars/jiyan1989/codex-quota-manager?style=social&logo=github&label=Stars)](https://github.com/jiyan1989/codex-quota-manager/stargazers)
[![MIT 开源许可证](https://img.shields.io/github/license/jiyan1989/codex-quota-manager?style=flat&label=license&color=22c55e)](LICENSE)
[![累计下载量](https://img.shields.io/github/downloads/jiyan1989/codex-quota-manager/total?style=flat&logo=github&logoColor=white&label=downloads&color=0891b2)](https://github.com/jiyan1989/codex-quota-manager/releases)
[![Windows 64位平台](https://img.shields.io/badge/Windows-x64-0078D4?style=flat)](https://github.com/jiyan1989/codex-quota-manager/releases/latest)
[![C# 与 .NET Framework 4.5及以上](https://img.shields.io/badge/C%23-.NET_Framework_4.5%2B-512BD4?style=flat&logo=dotnet&logoColor=white)](#developer)
[![PowerShell 7及以上运行环境](https://img.shields.io/badge/PowerShell-7%2B-5391FE?style=flat)](#三步开始)
[![Python 3.10及以上可选集成](https://img.shields.io/badge/Python-3.10%2B-3776AB?style=flat&logo=python&logoColor=white)](#developer)
[![MCP 本地标准输入输出接口](https://img.shields.io/badge/MCP-stdio-16a34a?style=flat)](#developer)

**让 Codex 所选任务在额度不足前暂停，额度恢复后自动继续，同时保留你设定的周额度。**

适用于 Windows 上的 Codex 桌面客户端。勾选已有聊天即可托管，不需要新建任务，也不需要反复点“开始监控”。当前版本：**v1.5.3**。

[下载最新版](https://github.com/jiyan1989/codex-quota-manager/releases/latest) · [三步开始](#三步开始) · [额度规则](#额度如何判断) · [更新说明](docs/updates.md)

## 能帮你做什么

| 功能 | 你会看到的效果 |
| --- | --- |
| 额度保护 | 五小时剩余额度到暂停线，或周剩余额度到保留底线，暂停托管任务 |
| 自动续跑 | 五小时达到恢复线、周额度高于底线，且原任务核实通过后继续；可关闭 |
| 多任务托管 | 搜索已有聊天并勾选，只管理明确选择的任务；取消勾选解除后续自动控制 |
| 手动暂停与继续 | 人工暂停会保持，不因额度恢复自动解除；手动继续也要通过额度检查 |
| 额度与等待原因 | 同时显示五小时、周额度、重置倒计时、连接状态和未继续的原因 |
| 原聊天恢复 | 符合条件但未加载的原聊天会尝试打开，核实后继续，不另建聊天 |
| 已有重置卡 | 可选择自动使用账号已有卡，默认关闭，不自动购买卡或额外额度 |
| 托盘与开机启动 | 可随 Windows 登录启动，在托盘后台保护；设置中可调整 |
| 版本更新 | 自动检查并提醒，点击同一个更新按钮下载安装，保留本地设置 |

## 三步开始

1. 从[发布页](https://github.com/jiyan1989/codex-quota-manager/releases/latest)下载 Windows x64 压缩包，解压后运行 `CodexQuotaMonitor.exe`，保持 Codex 已登录并打开。
2. 搜索原来的聊天，勾选希望托管的任务。首次安装不会默认接管历史聊天。
3. 打开右上角设置，调整暂停线、自动恢复线及周额度保留底线，按需开启自动续跑。

常规打开程序即开始额度保护。主界面只保留“暂停任务／继续任务”，没有重复的监控开关。

**运行要求：** Windows x64、.NET Framework 4.5+、PowerShell 7、已登录的 Codex 桌面客户端。程序自动寻找 PowerShell 7；找不到时会提示安装运行组件。软件界面目前为中文，本页另提供英文文档。

## 额度如何判断

新安装默认：**五小时剩余 ≤10% 暂停，≥20% 允许自动恢复；周剩余 ≤10% 暂停。** 三项都可以设置，自动恢复线必须高于暂停线。

| 情况 | 处理方式 |
| --- | --- |
| 五小时剩余 ≤ 暂停线，**或**周剩余 ≤ 周底线 | 暂停托管任务 |
| 五小时达到自动恢复线，**且**周剩余高于底线 | 开启自动续跑并通过原任务检查后恢复 |
| 点击“继续任务” | 五小时高于暂停线、周剩余高于底线即可；不必达到自动恢复线 |
| 五小时刷新为 100%，但周剩余已经到达底线 | 保持暂停，不继续消耗保留的周额度 |
| 所需额度未知、账号不可用、任务待输入或审批 | 等待，不直接放行恢复 |
| 人工暂停任务 | 保持暂停，需明确点击继续 |

例如：暂停线 10%、恢复线 20%、周底线 30%，表示自动继续要求五小时剩余至少 20%，同时周剩余**大于 30%**。重置倒计时结束不代表额度已经恢复，程序仍等待实际额度确认。

## 软件界面

以下图片使用虚构任务和额度，不含真实账号信息。图片为 v1.5.0 布局参考，当前版本已精简部分按钮，以实际程序为准。

![主界面：托管任务、双额度及等待原因](docs/images/main-window.png)

![设置界面：额度策略、启动与版本更新](docs/images/settings-window.png)

## 暂停、退出与取消托管

- **暂停任务：** 暂停选中任务并保持人工暂停，后台继续保护其他托管任务。
- **继续任务：** 请求继续选中的原聊天；先核实额度和任务，登记成功不等于已经执行。
- **取消托管：** 取消勾选，不再据旧托管记录发起新的暂停、续跑或自动加载；已经发送给 Codex 的请求无法撤回。
- **退出程序：** 使用托盘菜单“退出管家”。退出停止额度保护，不会自动暂停正在运行的聊天。
- **关闭窗口：** 开启托盘后台运行时，右上角 X 将窗口收进托盘，保护继续运行。

自动续跑关闭时，额度不足仍会保护暂停；人工继续仍可单独使用。只读观察模式只展示状态，不操作任务。

## v1.5.3 有什么变化

- 自动寻找并检查 PowerShell 7 运行组件，启动失败显示具体原因。
- 状态文件按变化读取、任务列表增量更新，减少重复读盘和界面重建。
- 收进托盘后降低界面刷新频率，后台额度检查频率保持原样。
- 日志限量保留并批量刷新，避免长期运行时无限增长。
- 连续失败逐步延长重试间隔；手动操作立即触发检查。
- 清理旧后台进程对象与事件订阅，合并同一按钮的短时间重复点击。

已完成编译、隔离策略检查及日志轮转检查；未测量具体内存降幅，真实完整跨额度周期和新版界面仍需人工验收。更多见[版本说明](docs/updates.md)和[已知问题](docs/known-issues.md)。

## 更新与常见问题

**已有用户怎么更新？** v1.5.0 及以上可在设置里点击“检查更新”，发现新版后同一按钮变为“一键更新”；自动检查只提醒，点击才安装。v1.4.1 及以前需要手动下载一次。更新保留设置、托管记录和人工暂停，并校验下载文件；安装启动失败支持回退旧版。

**任务没出现？** 开启“显示全部任务”，搜索原聊天名称或工作目录，再勾选托管。输入路径不会自动新建或启动聊天。

**为什么没继续？** 查看界面等待原因：可能是周底线、额度未知、人工暂停、自动续跑关闭、任务待审批或客户端未连接。不要用重复点击绕过等待。

**换电脑会自动同步吗？** 不会。它是本地独立程序，需在另一台电脑单独部署；同一账号登录不会同步托管记录。避免多台电脑同时控制同一任务。

**能保证不扣购买积分吗？** 不能。轮询与额度同步存在延迟，提前暂停能降低风险，但不能保证零额外扣费。程序不会主动购买额度。

**睡眠后还能续跑吗？** 电脑必须处于唤醒状态、程序和 Codex 可运行；本版不会强制阻止系统休眠。

## 隐私与限制

设置、任务标识、标题、工作目录及日志保存在本机 `%LOCALAPPDATA%/CodexQuotaManager`。不要把此目录打包提交到 GitHub 或问题反馈。发布包不包含用户设置、聊天或运行记录。

程序不设遥测服务；额度与任务控制通过本机已登录的 Codex 完成，更新检查访问本项目公共 GitHub 资源。它不是 OpenAI 官方产品，Codex 客户端更新可能影响兼容性。

<a id="developer"></a>

<details>
<summary>开发者：构建与可选聊天集成</summary>

使用 PowerShell 7：

```powershell
pwsh -NoProfile -File ./build.ps1
pwsh -NoProfile -File ./test.ps1
```

产物：`dist/CodexQuotaMonitor.exe`。使用 Windows .NET Framework C# 编译器，无第三方 NuGet 依赖。`--no-start` 只打开窗口，`--tray` 启动并进入托盘。

可选 MCP（模型上下文协议）桥接需要 Python 3.10+，基础窗口功能不依赖此集成：

```toml
[mcp_servers.codex_quota_manager]
command = "python"
args = ["-X", "utf8", "/replace/with/absolute/path/integration/manager_mcp.py"]
```

支持查询状态、显式托管及人工控制，可唤醒已经打开但停止监控的程序。操作排队后仍须核对实际任务状态。相关说明见 [integration](integration)。

</details>

## 卸载

关闭随 Windows 启动，从托盘退出后删除程序文件；配置过 MCP 的用户同时移除相关配置。需要清除记录时再删除上述本地数据目录。

## 收藏增长趋势

[![Star History（收藏增长趋势）](https://api.star-history.com/svg?repos=jiyan1989/codex-quota-manager&type=Date)](https://www.star-history.com/#jiyan1989/codex-quota-manager&Date)

## 来源与许可证

基于 [cat2mao/codex-quota-monitor](https://github.com/cat2mao/codex-quota-monitor) 的 MIT 许可代码修改，保留上游版权与许可。本仓库从公开源码快照独立记录提交，贡献者统计不代表原有代码全部由本仓库维护者创作。见 [LICENSE](LICENSE) 和[第三方声明](THIRD_PARTY_NOTICES.md)。
