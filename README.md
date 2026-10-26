# Codex 任务托管

Windows 本地 Codex 额度监控与任务暂停、续跑工具。支持多任务托管、自定义阈值、重置倒计时和托盘运行。

## 下载与使用

1. 从 [Releases（发布版本）](https://github.com/jiyan1989/codex-quota-manager/releases/latest) 下载 ZIP，解压后双击 `CodexQuotaMonitor.exe`。
2. 保持 Codex 桌面客户端已登录。首次打开会启动监控，默认不托管任何任务。
3. 搜索原任务名称或工作目录，勾选需要托管的任务。取消勾选即可取消托管。
4. 右侧设置暂停和恢复阈值；默认剩余 ≤10% 暂停，≥20% 允许自动恢复，恢复阈值必须高于暂停阈值。

需要 Windows x64、.NET Framework 4.x 和 PowerShell 7（安装到 PATH，或使用程序可发现的 Codex 内置运行环境）。

## 功能

- 左侧托管目标：搜索、多任务选择、显示全部、刷新、暂停任务、继续任务及开始／停止监控。
- 右侧额度卡片：五小时与周额度、重置时刻及倒计时。倒计时到点后仍等待真实额度确认。
- 自动策略：暂停／恢复阈值、额度恢复后自动续跑、自动使用已有重置卡。
- 重置卡默认关闭，明确开启后才按设置中的周剩余阈值使用已有卡；不购买卡或额度。
- 右上角设置：随 Windows 登录启动并监控、最小化到托盘后后台运行、完整日志入口。
- 底部连接与运行状态：连接状态、监控状态、数据新鲜度和等待原因。
- 人工暂停保持暂停；自动续跑关闭后仍保护暂停。未加载任务可能会在 Codex 中打开原聊天。

关闭窗口是否进入托盘由设置决定；从托盘退出可停止后台。停止监控不会取消现有任务，暂停任务和停止监控是两个独立操作。

## 本地数据与隐私

数据保存在 `%LOCALAPPDATA%/CodexQuotaManager`，包括设置、托管任务标识、任务标题、工作目录及运行日志。这些文件可能包含隐私，请勿提交到仓库或直接附到公开 issue（问题反馈）。

本仓库不附带任何用户聊天、账户配置、运行记录或真实任务截图。程序不设置遥测服务，MCP（模型上下文协议）桥接使用本地标准输入输出，无网络监听端口；额度查询与控制通过已登录的本机 Codex 完成。

本工具仅在当前电脑运行；换电脑需单独部署，登录同一账号不会同步设置。不要在多台电脑同时接管同一任务。

## 限制

- 非 OpenAI 官方产品；使用 Codex 本机 app-server（应用服务）及桌面 IPC（进程间通信），客户端更新可能导致不兼容。
- 提前暂停存在轮询与同步延迟，**不能保证购买积分零消耗**。
- 恢复操作可能继续原聊天并消耗正常额度。只勾选你明确希望自动继续的任务。
- 测试覆盖模拟额度、暂停／恢复状态机与隔离的 MCP 操作；不将模拟测试等同于所有客户端版本的真实额度重置验收。

## 从源码构建

在项目目录用 PowerShell 7 执行：

```powershell
pwsh -NoProfile -File ./build.ps1
pwsh -NoProfile -File ./test.ps1
pwsh -NoProfile -File ./monitor/check-manager.ps1
python -X utf8 ./integration/test_mcp.py
```

产物位于 `dist/CodexQuotaMonitor.exe`。构建使用 Windows 自带的 .NET Framework C# 编译器，不需要第三方 NuGet 包。

手动启动：双击 EXE；或执行 `./dist/CodexQuotaMonitor.exe --no-start` 仅打开界面，点击“开始监控”再启动。`--tray` 启动后进入托盘。

## 可选 Codex 集成

Python 3.10+ 可运行 `integration/manager_mcp.py`，在 Codex 的 MCP 配置中使用 Python 可执行文件和该脚本的**实际绝对路径**。基础窗口功能不依赖此集成。

```toml
[mcp_servers.codex_quota_manager]
command = "python"
args = ["-X", "utf8", "/replace/with/absolute/path/integration/manager_mcp.py"]
```

提供状态查询、任务查询、显式托管操作及全局控制。工具返回排队不代表已经恢复，请重新查询实际状态。不要复制他人的本地运行配置。

## 卸载

先在设置中关闭随 Windows 启动，再从托盘退出并移除程序文件；若已配置 MCP，请移除对应配置。需要清除记录时可自行删除上述本地数据目录。

## 来源与许可证

本项目基于 [cat2mao/codex-quota-monitor](https://github.com/cat2mao/codex-quota-monitor) 的 MIT 许可代码进行修改，保留上游版权及许可。新增任务托管界面、策略设置、显式任务控制和本地 MCP 集成。

本仓库从当前公开源码快照开始独立记录提交，上游历史未导入。GitHub Contributors（贡献者）列表仅反映此仓库提交记录，不表示原有代码全部由本仓库维护者创作。

许可证见 [LICENSE](LICENSE)。
