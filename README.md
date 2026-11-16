# Laptop Log Collector

A one-click log collection tool for Windows laptop testers. Tick the items you need; the tool
copies the logs, system information, crash dumps, power and performance data into one folder and
generates an HTML report that can be handed straight to engineering.

- Single portable exe, no .NET / Python / runtime required
- Read-only: does not clear logs, change settings or install anything
- Works offline; interface, reports and folder names are in English

![UI](dist/UI_Preview.png)

## Branches

Every release gets its own branch; `main` always tracks the latest development state.

| Branch | Version | Notes |
|---|---|---|
| `main` | dev | latest code, may contain unreleased changes |
| `v2.1` | v2.1 | collector name field in the top bar written to `Collected_By.txt`; CSV-per-channel, process dump, energy report, performance sampling and extra-folder items removed; folders renumbered 1-21 |
| `v2.0` | v2.0 | folders renumbered from 1, WLAN report and per-folder browse/SOURCE files removed, trimmed items (systeminfo only, driver store + yellow-mark devices, all running processes, power settings only), DXDiag, "Installed updates and software"; the same number is shown in front of every item in the UI |
| `v1.8` | v1.8 | added Log Analyzer: local web UI to browse a package and have AI analyse selected logs |
| `v1.9` | v1.9 | System/Application logs exported to CSV with every event and full messages, plus an errors-only CSV and an optional all-channels conversion |
| `v1.7` | v1.7 | `C:\Windows\Panther`, `C:\Windows\Logs`, `C:\Windows\INF` mirrored with their folder structure |
| `v1.6` | v1.6 | WER and LiveKernelReports mirrored with their folder structure; `powercfg /spr` sleep-study report; SOURCE.txt in every folder |
| `v1.5` | v1.5 | new "All event log files (.evtx)" item: full mirror of `winevt\Logs` including empty folders (channel export fallback without admin); package folder named after the BIOS serial |
| `v1.4` | v1.4 | quick links to System.evtx / Application.evtx on the report; output path layout fix |
| `v1.3` | v1.3 | English UI, reports and folder names; all messages localised |
| `v1.2` | v1.2 | power/sleep timeout collection; HTML viewer (All_Content.html + browse.html) |
| `v1.1` | v1.1 | process memory dump; "skipped" instead of false success; dump-config diagnostics |
| `v1.0` | v1.0 | initial release; sources were overwritten before git was introduced |

New release: work on `main`, then `git branch vX.Y` and tag it with the same name.

## What it collects

| Group | Items |
|---|---|
The number in front of each item is the number of its output folder (item 5 writes to
`5_Drivers_Devices`) and they run in that order.

| # | Group | Items |
|---|---|---|
| 1-8 | Basics | System event log, application event log, all .evtx files, systeminfo, driver store + problem devices, running processes, installed updates and software, msinfo32 (optional) |
| 9-12 | Crashes and dumps | Blue screen dumps (Minidump), WER crash reports (full `C:\ProgramData\Microsoft\Windows\WER` mirror), Live kernel reports (`C:\Windows\LiveKernelReports` mirror), MEMORY.DMP |
| 13-15 | Setup and component logs | Panther setup logs, `C:\Windows\Logs` (CBS/DISM/WindowsUpdate), `C:\Windows\INF` driver INFs and setupapi logs |
| 16-18 | Power and battery | Battery health report, power settings, sleep study + `powercfg /spr` system power report |
| 19 | Performance | DXDiag |
| 20 | Network | Network configuration snapshot |
| 21 | Storage | Disk and SMART health |

## Output

Each run creates `<serial>_<timestamp>/` containing:

- `report.html` — key findings, event timeline, hardware, battery and power settings
- `All_Content.html` — every text file in one browsable page (sidebar + collapsible blocks)
- `Collection_Info.txt` — what was collected, status and how to read it
- `Collected_By.txt` — collector name, package, machine and start time
- Raw logs: `System.evtx`, `Application.evtx`, `*.dmp`, CSV/TXT exports
- `System_events.csv` / `Application_events.csv` - every event in the log with the full message text
- `System_errors.csv` / `Application_errors.csv` - errors and warnings only, small enough to hand to an AI

Text files are decoded as UTF-8, UTF-16 or GBK automatically, so Chinese output of system tools
still renders correctly.

## Build

No Visual Studio required — the .NET Framework compiler shipped with Windows is used:

```powershell
.\build.ps1
```

Output goes to `build\LogCollector.exe`.

The log analyser is a separate application in its own repository:
https://github.com/early-sleep/BSOD_analyse

## Command line

```
LogCollector.exe /cli /out:D:\logs /jobs:events-system,sysinfo,minidump
LogCollector.exe /cli /all /out:D:\logs /tag:SN1234567
LogCollector.exe /cli /out:D:\logs /jobs:pdump /proc:chrome.exe
```

Options: `/cli` headless, `/out` output folder, `/jobs` comma separated item ids, `/all` every item,
`/tag` package name prefix, `/case` case id, `/symptom` symptom text, `/proc` target process.

## Log Analyzer

The log analyser moved to its own repository and is now a desktop app:

**https://github.com/early-sleep/BSOD_analyse**

It reads the packages produced here, lets you tick the logs you want and asks an
OpenAI-compatible model to analyse them.

## Notes

- Temperature, fan speed and adapter identification need vendor WMI/EC interfaces — not available
  through standard Windows APIs.
- Blue screen dumps are produced by Windows, not by this tool. With "Small memory dump" configured
  no MEMORY.DMP is ever created; see `dist/User_Guide.txt` section 6.
- Process dumps can be several GB; the tool checks free space before copying large dumps.
- Collected packages may contain personal data (user names, paths, SSIDs). Handle them according to
  your data classification policy.
