# Windows 笔记本测试日志采集工具

给笔记本测试人员用的一键日志采集工具。勾选需要的内容，自动收集系统日志、硬件信息、
崩溃转储、电源与性能数据，生成一份可直接交给研发的 HTML 报告。

- 单文件 exe，免安装，不依赖 .NET / Python / 运行库
- 采集过程只读，不清空日志、不改系统设置、不装任何东西
- 无网络环境可完整运行

## 分支与版本

每个发布版本单独一条分支，`main` 始终是最新开发版。

| 分支 | 版本 | 说明 |
|---|---|---|
| `main` | 开发版 | 最新代码，可能包含未发布的改动 |
| `v1.1` | v1.1 | 增加进程内存转储；修复"没采到却报成功"；转储设置诊断 |
| `v1.0` | v1.0 | 初版。源码在接入 git 前已被迭代覆盖，未保留该分支 |

新版本发布流程：在 `main` 上改完后，新建 `vX.Y` 分支并打同名的 tag。

## 功能

| 分组 | 采集项 |
|---|---|
| 基础信息 | 系统事件日志 + 关键事件分析、应用程序事件日志、系统信息与硬件明细、驱动与设备清单、进程/服务/启动项快照、补丁与已安装软件、msinfo32（可选） |
| 崩溃与转储 | 蓝屏 Minidump、WER 应用崩溃报告、MEMORY.DMP、进程内存转储（GB 级） |
| 电源与电池 | 电池健康报告、电源设置与唤醒源、能耗诊断（60s）、睡眠诊断 |
| 性能 | 性能采样（30s）、dxdiag 显卡信息 |
| 网络 | 网络配置快照、无线连接历史报告 |
| 存储 | 磁盘与 SMART 健康 |
| 其他 | 附加日志目录（自定义路径） |

## 目录结构

```
src/                  C# 源码（.NET Framework 4.x，WinForms）
  Core.cs             基础设施：进程调用、机器信息、事件目录
  Jobs.cs             采集任务实现
  Report.cs           HTML 报告生成
  MainForm.cs         界面
  Program.cs          入口、命令行、任务调度
  AssemblyInfo.cs     程序集信息
  app.manifest        应用清单（asInvoker + DPI）
build.ps1             一键编译脚本
dist/                 构建产物（exe、使用说明、界面预览、发布包）
docs/                 需求分析报告
```

## 编译

无需 Visual Studio，用系统自带的 .NET Framework 编译器即可：

```powershell
.\build.ps1
```

输出 `build\LogCollector.exe`。

## 使用

双击 `dist\LogCollector.exe`，勾选采集项，点"开始采集"。

建议以管理员身份运行，否则 WER、能耗诊断、睡眠诊断、无线历史报告会被跳过。

详细说明见 [dist/使用说明.txt](dist/使用说明.txt)。

## 命令行

```
LogCollector.exe /cli /out:D:\logs /jobs:events-system,sysinfo,minidump
LogCollector.exe /cli /all /out:D:\logs /tag:SN1234567
LogCollector.exe /cli /out:D:\logs /jobs:pdump /proc:chrome.exe
```

参数：`/cli` 无界面、`/out` 输出目录、`/jobs` 采集项、`/all` 全部、
`/tag` 包名前缀、`/case` 用例编号、`/symptom` 现象描述、`/proc` 进程名。

## 已知限制

- 温度、风扇转速、适配器识别等 EC 数据没有标准接口，需厂商 WMI/EC 支持
- 蓝屏转储由系统产生，工具只能收集；系统设为"小内存转储"时不会生成 MEMORY.DMP
- msinfo32 在部分机器上会卡住，故降级为默认不勾选

## 说明

采集包可能包含个人信息（用户名、文件路径、SSID 等），请按公司数据分级要求流转。
