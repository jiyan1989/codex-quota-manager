# Codex Quota Manager

**[简体中文](README.md) · English**

[![最新稳定版](https://img.shields.io/github/v/release/jiyan1989/codex-quota-manager?style=flat&logo=github&logoColor=white&label=version&color=2563eb)](https://github.com/jiyan1989/codex-quota-manager/releases/latest)
[![GitHub 收藏数量](https://img.shields.io/github/stars/jiyan1989/codex-quota-manager?style=social&logo=github&label=Stars)](https://github.com/jiyan1989/codex-quota-manager/stargazers)
[![MIT 开源许可证](https://img.shields.io/github/license/jiyan1989/codex-quota-manager?style=flat&label=license&color=22c55e)](LICENSE)
[![累计下载量](https://img.shields.io/github/downloads/jiyan1989/codex-quota-manager/total?style=flat&logo=github&logoColor=white&label=downloads&color=0891b2)](https://github.com/jiyan1989/codex-quota-manager/releases)
[![Windows 64位平台](https://img.shields.io/badge/Windows-x64-0078D4?style=flat)](https://github.com/jiyan1989/codex-quota-manager/releases/latest)
[![C# 与 .NET Framework 4.5及以上](https://img.shields.io/badge/C%23-.NET_Framework_4.5%2B-512BD4?style=flat&logo=dotnet&logoColor=white)](#developer)
[![PowerShell 7及以上运行环境](https://img.shields.io/badge/PowerShell-7%2B-5391FE?style=flat)](#quick-start)
[![Python 3.10及以上可选集成](https://img.shields.io/badge/Python-3.10%2B-3776AB?style=flat&logo=python&logoColor=white)](#developer)
[![MCP 本地标准输入输出接口](https://img.shields.io/badge/MCP-stdio-16a34a?style=flat)](#developer)

**Pause selected Codex chats before their quota runs low, resume them when quota recovers, and preserve a weekly quota reserve.**

A local Windows companion for Codex Desktop. Select existing chats to manage; no new chat or separate “start monitoring” action is required. Current release: **v1.5.3**. The application interface is currently in Chinese; this is the English documentation.

[Download](https://github.com/jiyan1989/codex-quota-manager/releases/latest) · [Quick start](#quick-start) · [Quota rules](#quota-rules) · [Release notes](https://github.com/jiyan1989/codex-quota-manager/releases)

## What it does

| Feature | Behavior |
| --- | --- |
| Quota protection | Pauses managed chats when five-hour remaining quota reaches the pause threshold, or weekly remaining quota reaches the reserve floor |
| Automatic resume | Resumes eligible original chats after five-hour quota reaches the recovery threshold and weekly quota is above its floor; optional |
| Multiple chats | Search and select existing chats; only explicitly selected chats are managed |
| Manual control | Manual pauses stay in place until explicitly released; manual resume still checks quota |
| Status and countdowns | Shows both quotas, reset countdowns, connection status, and reasons a chat cannot resume |
| Original-chat loading | Attempts to open an eligible unloaded original chat and verifies it before continuing; does not create a replacement chat |
| Existing reset cards | Optional use of existing account reset cards; disabled by default; does not buy cards or additional quota |
| Tray and startup | Optional Windows sign-in startup and background tray operation |
| Updates | Checks for new releases and installs only when requested, preserving local settings |

## Quick start

1. Download the Windows x64 ZIP from [Releases](https://github.com/jiyan1989/codex-quota-manager/releases/latest), extract it, and run `CodexQuotaMonitor.exe`. Keep Codex Desktop open and signed in.
2. Search for an existing chat and check it to enable management. A fresh installation does not select historical chats automatically.
3. Open settings in the upper-right corner. Set the five-hour pause threshold, automatic recovery threshold, and weekly reserve floor. Enable automatic resume if desired.

Normal startup enables quota protection. The main window provides task pause/resume without a duplicate monitoring toggle.

**Requirements:** Windows x64, .NET Framework 4.5+, PowerShell 7, and a signed-in Codex Desktop installation. The app searches for PowerShell 7 and reports a missing runtime component if it cannot find one.

## Quota rules

Fresh-install defaults: pause at **≤10% five-hour remaining**, allow automatic recovery at **≥20%**, and pause at **≤10% weekly remaining**. All three values are configurable; the recovery threshold must exceed the pause threshold.

| Condition | Result |
| --- | --- |
| Five-hour remaining ≤ pause threshold **OR** weekly remaining ≤ reserve floor | Pause managed chats |
| Five-hour remaining reaches recovery threshold **AND** weekly remaining is above its floor | Resume if automatic resume is enabled and the original-chat checks pass |
| Manual resume | Requires five-hour quota above the pause threshold and weekly quota above its floor; the automatic recovery threshold is not required |
| Five-hour quota resets to 100%, but weekly quota is at its floor | Stay paused |
| Required quota is unknown, account use is blocked, or input/approval is pending | Wait; do not assume the chat can resume |
| A chat was manually paused | Keep it paused until explicitly resumed |

Example: pause at 10%, recover at 20%, weekly floor 30%. Automatic resume requires at least 20% five-hour quota and **more than 30% weekly quota**. A countdown reaching zero is not proof of a quota reset; actual quota must be confirmed.

## Screenshots

These images contain fictional chats and quota values, with no real account data. They illustrate the v1.5.0 layout; later versions simplify some controls.

![Main window: managed chats, both quotas, and waiting reasons](docs/images/main-window.png)

![Settings: quota policy, startup, and updates](docs/images/settings-window.png)

## Pause, unmanage, and exit

- **Pause task:** pauses the selected chat and keeps it manually paused; other managed chats remain protected.
- **Resume task:** requests continuation of the original chat after quota and task checks. Queued does not mean executed.
- **Uncheck a chat:** stops new automatic pause/resume/loading requests based on its previous management record. Requests already sent to Codex cannot be recalled.
- **Exit from the tray:** stops quota protection; it does not automatically pause chats that are running.
- **Close the window:** when tray background operation is enabled, the X hides the window while protection continues.

Disabling automatic resume does not disable protective pausing. Manual resume remains available. Read-only observation mode displays status without controlling chats.

## New in v1.5.3

- Expanded PowerShell 7 runtime discovery and visible startup errors.
- Cached state reads and incremental task-row updates reduce repeated disk reads and UI rebuilding.
- Hidden-window UI refresh is reduced without changing background quota polling frequency.
- Bounded, batched logs prevent indefinite log growth.
- Consecutive connection failures use progressively longer retry intervals; manual actions trigger immediate checks.
- Old worker process objects and event subscriptions are released; rapid repeated clicks on the same action are coalesced.

Compilation, isolated policy checks, and log rotation checks passed. No measured memory-reduction percentage is claimed. Full real-client reset-cycle behavior and the latest UI still require manual acceptance. See [known issues (Chinese)](docs/known-issues.md).

## Updates and common questions

**How do existing users update?** v1.5.0+ offers one settings button that changes from checking to installing an available update. Automatic checks notify only; installation requires a click. v1.4.1 and earlier need one manual upgrade. Updates validate the download, preserve settings, management records, and manual pauses, and support rollback on installation/startup failure.

**A chat is missing:** enable the all-tasks filter and search its original title or working directory, then select it. Entering a path does not create or start a chat.

**A chat does not resume:** read its waiting reason. Weekly reserve, unknown quota, manual pause, disabled automatic resume, pending approval, and a disconnected client can block recovery.

**Does it sync across computers?** No. Install and configure it separately on each computer. Signing into the same account does not sync management records. Avoid controlling the same chat from multiple computers.

**Can it guarantee no purchased credits are spent?** No. Polling and quota synchronization introduce delay. Early pausing reduces risk but cannot guarantee zero additional charges. The app does not purchase quota.

**Will it resume while the computer sleeps?** The computer must be awake and both applications available. This version does not force the system to remain awake.

## Privacy and limitations

Settings, chat IDs, titles, working directories, and logs stay in `%LOCALAPPDATA%/CodexQuotaManager`. Do not submit that directory to public repositories or issue reports. Release packages exclude user settings, chats, and runtime records.

There is no application telemetry service. Quota and task operations use the locally signed-in Codex; update checks access this project's public GitHub resources. This is an unofficial product. Codex Desktop updates may affect compatibility.

<a id="developer"></a>

<details>
<summary>Developers: build and optional chat integration</summary>

Use PowerShell 7:

```powershell
pwsh -NoProfile -File ./build.ps1
pwsh -NoProfile -File ./test.ps1
```

Output: `dist/CodexQuotaMonitor.exe`. Uses the Windows .NET Framework C# compiler with no third-party NuGet dependencies. `--no-start` opens the window without starting protection; `--tray` starts in the tray.

The optional local MCP bridge requires Python 3.10+. Basic desktop operation does not require it:

```toml
[mcp_servers.codex_quota_manager]
command = "python"
args = ["-X", "utf8", "/replace/with/absolute/path/integration/manager_mcp.py"]
```

The bridge supports status queries, explicit management, manual controls, and waking an already-open app whose monitoring has stopped. Verify actual chat state after submitting a queued command. See [integration](integration).

</details>

## Uninstall

Disable Windows startup, exit through the tray, and remove the application files. Remove any optional MCP configuration. Delete the local data directory only if you also want to erase settings and records.

## Star history

[![Star History](https://api.star-history.com/svg?repos=jiyan1989/codex-quota-manager&type=Date)](https://www.star-history.com/#jiyan1989/codex-quota-manager&Date)

## Attribution and license

Modified from [cat2mao/codex-quota-monitor](https://github.com/cat2mao/codex-quota-monitor) under the MIT license, retaining upstream copyright and license notices. This repository records independent commits from a public source snapshot; contributor statistics do not imply sole authorship of the original code. See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md).
