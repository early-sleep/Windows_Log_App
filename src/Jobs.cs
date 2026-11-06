using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace LogCollector
{
    public static class Catalog
    {
        public static List<JobDef> All()
        {
            List<JobDef> list = new List<JobDef>();

            list.Add(new JobDef { Id = "events-system", Category = "基础信息", Name = "系统事件日志 + 关键事件分析", Hint = "含蓝屏/异常关机/驱动/硬件错误，约 10 秒", DefaultOn = true, Run = Jobs.SystemEvents });
            list.Add(new JobDef { Id = "events-app", Category = "基础信息", Name = "应用程序事件日志", Hint = "应用崩溃与无响应记录，约 10 秒", DefaultOn = true, Run = Jobs.AppEvents });
            list.Add(new JobDef { Id = "sysinfo", Category = "基础信息", Name = "系统信息与硬件明细", Hint = "机型/BIOS/CPU/内存/电池/异常设备，约 15 秒", DefaultOn = true, Run = Jobs.SysInfo });
            list.Add(new JobDef { Id = "drivers", Category = "基础信息", Name = "驱动与设备清单", Hint = "驱动版本、签名、异常设备，约 10 秒", DefaultOn = true, Run = Jobs.Drivers });
            list.Add(new JobDef { Id = "snapshot", Category = "基础信息", Name = "进程/服务/启动项快照", Hint = "当前运行状态，约 5 秒", DefaultOn = true, Run = Jobs.Snapshot });
            list.Add(new JobDef { Id = "software", Category = "基础信息", Name = "补丁与已安装软件", Hint = "环境变更追溯，约 5 秒", DefaultOn = true, Run = Jobs.Software });
            list.Add(new JobDef { Id = "msinfo32", Category = "基础信息", Name = "msinfo32 完整报告（较慢）", Hint = "设备与资源信息更全，但部分机器会卡住，默认不勾选", DefaultOn = false, Run = Jobs.Msinfo32 });

            list.Add(new JobDef { Id = "minidump", Category = "崩溃与转储", Name = "蓝屏转储（Minidump）", Hint = "蓝屏必抓，约 3 秒", DefaultOn = true, Run = Jobs.MiniDump });
            list.Add(new JobDef { Id = "wer", Category = "崩溃与转储", Name = "应用崩溃报告（WER）", Hint = "最近 25 份，约 5 秒", DefaultOn = true, Run = Jobs.Wer });
            list.Add(new JobDef { Id = "memdump", Category = "崩溃与转储", Name = "完整内存转储（MEMORY.DMP）", Hint = "体积可达数 GB，需系统已生成该文件", DefaultOn = false, Run = Jobs.MemDump });
            list.Add(new JobDef { Id = "pdump", Category = "崩溃与转储", Name = "进程内存转储（GB 级，可指定进程）", Hint = "内存泄漏/卡死时用，在右侧填进程名，约 10–60 秒", DefaultOn = false, Run = Jobs.ProcessDump });

            list.Add(new JobDef { Id = "battery", Category = "电源与电池", Name = "电池健康报告", Hint = "容量/循环/健康度，约 5 秒", DefaultOn = true, Run = Jobs.Battery });
            list.Add(new JobDef { Id = "powercfg", Category = "电源与电池", Name = "电源设置与唤醒源", Hint = "睡眠、唤醒、阻止睡眠，约 5 秒", DefaultOn = true, Run = Jobs.PowerCfg });
            list.Add(new JobDef { Id = "energy", Category = "电源与电池", Name = "能耗诊断（60 秒采样）", Hint = "需管理员，约 60 秒", DefaultOn = false, NeedAdmin = true, Run = Jobs.Energy });
            list.Add(new JobDef { Id = "sleepstudy", Category = "电源与电池", Name = "睡眠诊断（Modern Standby）", Hint = "需管理员，约 20 秒", DefaultOn = false, NeedAdmin = true, Run = Jobs.SleepStudy });

            list.Add(new JobDef { Id = "perf", Category = "性能", Name = "性能采样（30 秒：CPU/内存/磁盘）", Hint = "约 35 秒，采样期间请勿操作", DefaultOn = false, Run = Jobs.Perf });
            list.Add(new JobDef { Id = "gpu", Category = "性能", Name = "显卡与显示信息（dxdiag）", Hint = "含 TDR 记录，约 20 秒", DefaultOn = true, Run = Jobs.Gpu });

            list.Add(new JobDef { Id = "netcfg", Category = "网络", Name = "网络配置快照", Hint = "IP/路由/Wi-Fi/蓝牙，约 5 秒", DefaultOn = true, Run = Jobs.NetCfg });
            list.Add(new JobDef { Id = "wlan", Category = "网络", Name = "无线连接历史报告", Hint = "连接/断开/失败时间线，需管理员，约 25 秒", DefaultOn = true, NeedAdmin = true, Run = Jobs.WlanReport });

            list.Add(new JobDef { Id = "disk", Category = "存储", Name = "磁盘与 SMART 健康", Hint = "健康度/温度/通电时间，约 5 秒", DefaultOn = true, Run = Jobs.Disk });

            list.Add(new JobDef { Id = "appdir", Category = "其他", Name = "附加日志目录（可指定路径）", Hint = "被测软件自己的日志", DefaultOn = false, Run = Jobs.AppDir });

            return list;
        }
    }

    public static class Jobs
    {
        // ---------------- 基础 ----------------
        public static void SystemEvents(Session s)
        {
            string dir = s.Dir("10_系统日志");
            string o;
            Tool.Run("wevtutil.exe", "epl System " + Tool.Q(Path.Combine(dir, "System.evtx")), 180000, s.Token, out o, Encoding.Default);

            string script = @"
$ids = 41,1001,6008,1074,42,107,506,507,17,18,19,47,219,411,7000,7001,7009,7011,7031,7034,4101,37,7,11,51,129,55,98,140,10016,36
$start = (Get-Date).AddDays(-7)
$ev = $null
try { $ev = Get-WinEvent -FilterHashtable @{LogName='System'; Id=$ids; StartTime=$start} -MaxEvents 400 -ErrorAction SilentlyContinue } catch { }
if($null -eq $ev){
  try { $ev = Get-WinEvent -FilterHashtable @{LogName='System'; Level=1,2,3; StartTime=$start} -MaxEvents 150 -ErrorAction SilentlyContinue } catch { }
}
$culture = [System.Globalization.CultureInfo]::InvariantCulture
if($null -eq $ev){ Write-Output ('# 最近 7 天未找到关键事件（系统日志可能已被清空或滚动覆盖）') }
else {
$ev | Sort-Object TimeCreated -Descending | ForEach-Object {
  $m = [string]$_.Message
  if($m){ $m = $m -replace ""[`r`n]+"",' ' }
  if($m.Length -gt 400){ $m = $m.Substring(0,400) }
  $t = $_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss',$culture)
  Write-Output ($t + ""`t"" + $_.Id + ""`t"" + $_.ProviderName + ""`t"" + $_.LevelDisplayName + ""`t"" + $m)
}
}
";
            string outp = Tool.Ps(script, 180000, s.Token);
            File.WriteAllText(Path.Combine(dir, "关键事件.csv"), outp, new UTF8Encoding(true));
            ParseEvents(outp, "System", s);
        }

        public static void AppEvents(Session s)
        {
            string dir = s.Dir("11_应用程序日志");
            string o;
            Tool.Run("wevtutil.exe", "epl Application " + Tool.Q(Path.Combine(dir, "Application.evtx")), 180000, s.Token, out o, Encoding.Default);

            string script = @"
$ids = 1000,1001,1002,1026,11707
$start = (Get-Date).AddDays(-7)
$ev = $null
try { $ev = Get-WinEvent -FilterHashtable @{LogName='Application'; Id=$ids; StartTime=$start} -MaxEvents 150 -ErrorAction SilentlyContinue } catch { }
$culture = [System.Globalization.CultureInfo]::InvariantCulture
if($null -eq $ev){ Write-Output ('# 最近 7 天没有应用崩溃记录') }
else {
$ev | Sort-Object TimeCreated -Descending | ForEach-Object {
  $m = [string]$_.Message
  if($m){ $m = $m -replace ""[`r`n]+"",' ' }
  if($m.Length -gt 400){ $m = $m.Substring(0,400) }
  $t = $_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss',$culture)
  Write-Output ($t + ""`t"" + $_.Id + ""`t"" + $_.ProviderName + ""`t"" + $_.LevelDisplayName + ""`t"" + $m)
}
}
";
            string outp = Tool.Ps(script, 180000, s.Token);
            File.WriteAllText(Path.Combine(dir, "应用崩溃事件.csv"), outp, new UTF8Encoding(true));
            ParseEvents(outp, "Application", s);
        }

        public static void SysInfo(Session s)
        {
            string dir = s.Dir("20_系统信息");
            string o;
            string sys = Path.Combine(dir, "systeminfo.txt");
            Tool.Run("cmd.exe", "/c systeminfo > " + Tool.Q(sys), 60000, s.Token, out o, Encoding.Default);

            string probe = @"$result = New-Object System.Collections.ArrayList
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
function E($s,$k,$v){
  if($null -eq $v){ return }
  $t = [string]$v
  if($t.Trim() -eq ''){ return }
  $t = $t -replace ""`t"",' '
  $t = $t -replace ""[`r`n]+"",' '
  Write-Output ($s + ""`t"" + $k + ""`t"" + $t)
}
$cs = Get-CimInstance Win32_ComputerSystem
E 'SYS' 'Manufacturer' $cs.Manufacturer
E 'SYS' 'Model' $cs.Model
E 'SYS' 'MemoryGB' ([math]::Round($cs.TotalPhysicalMemory/1GB,1))
E 'SYS' 'Threads' $cs.NumberOfLogicalProcessors
$bios = Get-CimInstance Win32_BIOS
E 'BIOS' 'Serial' $bios.SerialNumber
E 'BIOS' 'Version' $bios.SMBIOSBIOSVersion
E 'BIOS' 'ReleaseDate' $bios.ReleaseDate
E 'BIOS' 'Vendor' $bios.Manufacturer
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
E 'CPU' 'Name' $cpu.Name
E 'CPU' 'Cores' $cpu.NumberOfCores
E 'CPU' 'Threads' $cpu.NumberOfLogicalProcessors
E 'CPU' 'MaxMHz' $cpu.MaxClockSpeed
E 'CPU' 'CurrentMHz' $cpu.CurrentClockSpeed
Get-CimInstance Win32_PhysicalMemory | ForEach-Object {
  $t = '{0} GB / {1} MHz / {2} / {3} / {4}' -f [math]::Round($_.Capacity/1GB,0), $_.Speed, $_.Manufacturer, $_.PartNumber, $_.DeviceLocator
  E 'MEM' ('Slot' + $_.DeviceLocator) $t
}
Get-CimInstance Win32_VideoController | ForEach-Object {
  $t = '{0} / 驱动 {1} / {2}x{3} @{4}Hz' -f $_.Name, $_.DriverVersion, $_.CurrentHorizontalResolution, $_.CurrentVerticalResolution, $_.CurrentRefreshRate
  E 'GPU' $_.Name $t
}
Get-CimInstance Win32_DiskDrive | ForEach-Object {
  $t = '{0} GB / {1} / 固件 {2}' -f [math]::Round($_.Size/1GB,0), $_.InterfaceType, $_.FirmwareRevision
  E 'DISK' $_.Model $t
}
$bat = Get-CimInstance Win32_Battery | Select-Object -First 1
E 'BAT' 'Name' $bat.Name
E 'BAT' 'Charge' $bat.EstimatedChargeRemaining
E 'BAT' 'Status' $bat.BatteryStatus
$des = (Get-CimInstance -Namespace root\wmi -ClassName BatteryStaticData | Select-Object -First 1).DesignedCapacity
$full = (Get-CimInstance -Namespace root\wmi -ClassName BatteryFullChargedCapacity | Select-Object -First 1).FullChargedCapacity
$cyc = (Get-CimInstance -Namespace root\wmi -ClassName BatteryCycleCount | Select-Object -First 1).CycleCount
E 'BAT' 'DesignedCapacity' $des
E 'BAT' 'FullChargedCapacity' $full
E 'BAT' 'CycleCount' $cyc
Get-CimInstance Win32_PnPEntity | Where-Object { $_.ConfigManagerErrorCode -ne 0 } | Select-Object -First 40 | ForEach-Object {
  E 'PNP' $_.Name ('错误码 ' + $_.ConfigManagerErrorCode)
}
$os = Get-CimInstance Win32_OperatingSystem
E 'OS' 'Caption' $os.Caption
E 'OS' 'Version' $os.Version
E 'OS' 'Build' $os.BuildNumber
E 'OS' 'Install' $os.InstallDate
E 'OS' 'LastBoot' $os.LastBootUpTime
";
            string probeOut = Tool.Ps(probe, 120000, s.Token);
            File.WriteAllText(Path.Combine(dir, "硬件信息.csv"), probeOut, new UTF8Encoding(true));
            ParseProbe(probeOut, s);
            File.WriteAllText(Path.Combine(dir, "系统摘要.txt"), ProbeSummary(s), new UTF8Encoding(true));
        }

        public static void Msinfo32(Session s)
        {
            string dir = s.Dir("24_msinfo32");
            string f = Path.Combine(dir, "msinfo32.txt");
            string o;
            Tool.Run("msinfo32.exe", "/report " + Tool.Q(f), 45000, s.Token, out o, Encoding.Default);
            WaitFile(f, 40000, s.Token);
            if (!File.Exists(f))
                throw new Exception("msinfo32 未能在限定时间内生成文件（该工具在部分机器上会卡住），请改用其它项目");
        }

        private static string ProbeSummary(Session s)
        {
            StringBuilder b = new StringBuilder();
            string[][] groups = new string[][] {
                new string[] { "SYS", "计算机" },
                new string[] { "BIOS", "BIOS 与序列号" },
                new string[] { "CPU", "处理器" },
                new string[] { "MEM", "内存" },
                new string[] { "GPU", "显卡" },
                new string[] { "DISK", "磁盘" },
                new string[] { "BAT", "电池" },
                new string[] { "PNP", "异常设备" },
                new string[] { "OS", "操作系统" }
            };
            foreach (string[] g in groups)
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in s.Probe)
                {
                    if (kv.Key.StartsWith(g[0] + "."))
                        lines.Add("  " + kv.Key.Substring(g[0].Length + 1) + " : " + kv.Value);
                }
                if (lines.Count > 0)
                {
                    b.AppendLine("=== " + g[1] + " ===");
                    foreach (string l in lines) b.AppendLine(l);
                    b.AppendLine();
                }
            }
            b.AppendLine("=== 采集上下文 ===");
            b.AppendLine("  计算机名 : " + s.Info.ComputerName);
            b.AppendLine("  登录用户 : " + s.Info.CurrentUser);
            b.AppendLine("  运行权限 : " + (s.IsAdmin ? "管理员" : "普通用户"));
            b.AppendLine("  系统启动 : " + s.Info.BootTime + "（已运行 " + s.Info.Uptime + "）");
            return b.ToString();
        }

        public static void Drivers(Session s)
        {
            string dir = s.Dir("21_驱动设备");
            string o;
            Tool.Run("cmd.exe", "/c driverquery /v /fo csv > " + Tool.Q(Path.Combine(dir, "驱动清单.csv")),
                     120000, s.Token, out o, Encoding.Default);
            Tool.Run("cmd.exe", "/c pnputil /enum-drivers > " + Tool.Q(Path.Combine(dir, "驱动库.txt")),
                     120000, s.Token, out o, Encoding.Default);
            string ps = @"Get-PnpDevice | Where-Object { $_.Status -ne 'OK' } | Select-Object Status,Class,FriendlyName,InstanceId | Format-Table -AutoSize | Out-String -Width 300";
            string outp = Tool.Ps(ps, 90000, s.Token);
            File.WriteAllText(Path.Combine(dir, "异常设备.txt"), outp, new UTF8Encoding(true));
        }

        public static void Snapshot(Session s)
        {
            string dir = s.Dir("22_运行状态");
            string ps = @"
'=== 进程 TOP 30（按内存） ==='
Get-Process | Sort-Object WS -Descending | Select-Object -First 30 Name,Id,@{n='CPU秒';e={[math]::Round($_.CPU,1)}},@{n='内存MB';e={[math]::Round($_.WS/1MB,0)}},Handles,Threads | Format-Table -AutoSize | Out-String -Width 300
'=== 非运行状态的服务 ==='
Get-Service | Where-Object { $_.Status -ne 'Running' } | Select-Object Status,StartType,Name,DisplayName | Format-Table -AutoSize | Out-String -Width 300
'=== 启动项 ==='
Get-CimInstance Win32_StartupCommand | Select-Object Name,Command,Location | Format-Table -AutoSize | Out-String -Width 300
'=== 计划任务（非微软） ==='
Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\Microsoft\*' } | Select-Object TaskName,State | Format-Table -AutoSize | Out-String -Width 300
";
            string outp = Tool.Ps(ps, 120000, s.Token);
            File.WriteAllText(Path.Combine(dir, "进程服务启动项.txt"), outp, new UTF8Encoding(true));
        }

        public static void Software(Session s)
        {
            string dir = s.Dir("23_补丁与软件");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== 已安装补丁 ===");
            string ps = @"Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 30 HotFixID,Description,InstalledOn | Format-Table -AutoSize | Out-String -Width 200";
            sb.AppendLine(Tool.Ps(ps, 60000, s.Token));
            sb.AppendLine();
            sb.AppendLine("=== 已安装软件 ===");
            sb.AppendLine(ReadInstalledApps());
            File.WriteAllText(Path.Combine(dir, "补丁与软件.txt"), sb.ToString(), new UTF8Encoding(true));
        }

        private static string ReadInstalledApps()
        {
            StringBuilder sb = new StringBuilder();
            string[] roots = new string[] {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            List<string> rows = new List<string>();
            foreach (string root in roots)
            {
                try
                {
                    using (RegistryKey k = Registry.LocalMachine.OpenSubKey(root))
                    {
                        if (k == null) continue;
                        foreach (string sub in k.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey app = k.OpenSubKey(sub))
                                {
                                    if (app == null) continue;
                                    string name = app.GetValue("DisplayName") as string;
                                    if (string.IsNullOrEmpty(name)) continue;
                                    string ver = app.GetValue("DisplayVersion") as string;
                                    string pub = app.GetValue("Publisher") as string;
                                    string date = app.GetValue("InstallDate") as string;
                                    rows.Add((name + " | " + ver + " | " + pub + " | " + date).Replace("\r", " ").Replace("\n", " "));
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            rows.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string r in rows) sb.AppendLine(r);
            return sb.ToString();
        }

        // ---------------- 崩溃 ----------------
        public static void MiniDump(Session s)
        {
            string dir = s.Dir("30_崩溃转储");
            string src = @"C:\Windows\Minidump";
            int n = 0;
            long bytes = 0;
            List<string> skipped = new List<string>();
            long free = 0;
            try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))).AvailableFreeSpace; }
            catch { }
            const long Reserve = 2L * 1024 * 1024 * 1024;   // 目标盘至少留 2GB

            if (Directory.Exists(src))
            {
                List<FileInfo> files = new List<FileInfo>();
                foreach (string f in Directory.GetFiles(src, "*.dmp"))
                {
                    try { files.Add(new FileInfo(f)); } catch { }
                }
                files.Sort(delegate(FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                int max = Math.Min(files.Count, 10);
                for (int i = 0; i < max; i++)
                {
                    if (s.Token.IsCancellationRequested) return;
                    FileInfo fi = files[i];
                    if (free > 0 && fi.Length + Reserve > free)
                    {
                        skipped.Add(fi.FullName + "（" + Tool.FmtSize(fi.Length) + "，目标盘剩余空间不足）");
                        s.Log("  · 空间不足，未复制 " + fi.Name + "（" + Tool.FmtSize(fi.Length) + "）");
                        continue;
                    }
                    if (fi.Length > 2L * 1024 * 1024 * 1024)
                        s.Log("  · 正在复制大文件 " + fi.Name + "（" + Tool.FmtSize(fi.Length) + "），可能需要几分钟…");
                    try
                    {
                        string dst = Path.Combine(dir, fi.Name);
                        File.Copy(fi.FullName, dst, true);
                        bytes += fi.Length;
                        free -= fi.Length;
                        n++;
                    }
                    catch (Exception ex)
                    {
                        skipped.Add(fi.FullName + "（复制失败：" + ex.Message + "）");
                    }
                }
            }

            if (skipped.Count > 0)
            {
                StringBuilder note = new StringBuilder();
                note.AppendLine("以下转储文件没有复制到采集包中，原始文件仍在系统里，需要时可手工复制：");
                foreach (string t in skipped) note.AppendLine("  " + t);
                File.WriteAllText(Path.Combine(dir, "未复制的大文件.txt"), note.ToString(), new UTF8Encoding(true));
            }

            if (n == 0)
            {
                string reason = skipped.Count > 0
                    ? "转储文件过大或复制失败，未复制（详见 30_崩溃转储\\未复制的大文件.txt）"
                    : "系统里没有蓝屏转储文件（" + DumpModeName() + "，近 30 天无蓝屏记录）";
                if (skipped.Count == 0) WriteNothing(dir, "本次没有找到蓝屏小型转储文件。\r\n\r\n" + DumpPolicy());
                s.Log("  · 未复制任何转储文件");
                throw new SkippedException(reason);
            }
            s.Log("  · 已复制 " + n + " 个转储文件，" + Tool.FmtSize(bytes)
                  + (skipped.Count > 0 ? "（另有 " + skipped.Count + " 个未复制）" : ""));
        }

        /// <summary>读取当前系统的转储设置，返回可读说明。</summary>
        public static string DumpPolicy()
        {
            StringBuilder b = new StringBuilder();
            int mode = -1;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl"))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("CrashDumpEnabled");
                        if (v != null) mode = Convert.ToInt32(v);
                        b.AppendLine("当前转储设置：" + ModeName(mode));
                        b.AppendLine("MEMORY.DMP 路径：" + k.GetValue("DumpFile"));
                        b.AppendLine("Minidump 路径：" + k.GetValue("MinidumpDir"));
                    }
                }
            }
            catch { }
            b.AppendLine("物理内存：" + s_Ram + " GB");
            try
            {
                foreach (CimLite p in PageFiles())
                    b.AppendLine("页面文件：" + p.Name + " " + p.SizeMB + " MB");
            }
            catch { }
            b.AppendLine();
            b.AppendLine("判断：" + ModeAdvice(mode));
            return b.ToString();
        }

        private static string s_Ram = "?";

        public static void DumpPolicyRam(double gb) { s_Ram = gb.ToString("0.0"); }

        private struct CimLite { public string Name; public long SizeMB; }

        private static List<CimLite> PageFiles()
        {
            List<CimLite> list = new List<CimLite>();
            string outp = Tool.Ps("Get-CimInstance Win32_PageFileUsage | ForEach-Object { $_.Name + '|' + $_.AllocatedBaseSize }",
                                  30000, CancellationToken.None);
            foreach (string line in outp.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = line.Split('|');
                if (p.Length < 2) continue;
                CimLite c = new CimLite();
                c.Name = p[0].Trim();
                long mb;
                long.TryParse(p[1].Trim(), out mb);
                c.SizeMB = mb;
                if (c.Name.Length > 0) list.Add(c);
            }
            return list;
        }

        public static string ModeName(int mode)
        {
            switch (mode)
            {
                case 0: return "无（蓝屏后不写任何转储）";
                case 1: return "完全内存转储（MEMORY.DMP，约等于物理内存大小）";
                case 2: return "内核内存转储（MEMORY.DMP，通常 1–4 GB）";
                case 3: return "小内存转储（Minidump，每个约 256 KB）";
                case 7: return "自动内存转储（MEMORY.DMP，由系统决定大小）";
                default: return "未知（" + mode + "）";
            }
        }

        private static string DumpModeName()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CrashControl"))
                {
                    object v = k.GetValue("CrashDumpEnabled");
                    if (v != null) return ModeName(Convert.ToInt32(v));
                }
            }
            catch { }
            return "转储设置未知";
        }

        private static string ModeAdvice(int mode)
        {
            if (mode == 3)
                return "当前是「小内存转储」，每次蓝屏只生成约 256 KB 的 Minidump，系统永远不会产生 MEMORY.DMP。"
                     + "如果你需要 GB 级的完整内存转储，必须把「写入调试信息」改成「内核内存转储」或「完全内存转储」。";
            if (mode == 0)
                return "当前未开启转储，蓝屏后不会留下任何文件。请把「写入调试信息」改为「小内存转储」或更高。";
            if (mode == 1 || mode == 2 || mode == 7)
                return "转储设置正确，但本机还没有产生转储文件：要么还没蓝屏过，要么转储写入失败（页面文件不够大时常见）。";
            return "无法识别当前转储设置。";
        }

        public static void MemDump(Session s)
        {
            string dir = s.Dir("32_内存转储");
            string src = @"C:\Windows\MEMORY.DMP";
            if (!File.Exists(src))
            {
                WriteNothing(dir, "本次没有找到完整内存转储 MEMORY.DMP。\r\n\r\n" + DumpPolicy());
                s.Log("  · 没有 MEMORY.DMP（" + DumpModeName() + "）");
                throw new SkippedException("系统里没有 MEMORY.DMP 文件：" + DumpModeName()
                    + "。详见 32_内存转储\\说明.txt 中的判断与修改建议");
            }
            FileInfo fi = new FileInfo(src);
            if (fi.Length > 12L * 1024 * 1024 * 1024)
            {
                WriteNothing(dir, "MEMORY.DMP 超过 12 GB，已跳过以避免磁盘写满。需要时请手工复制：" + src);
                throw new SkippedException("MEMORY.DMP 超过 12 GB，已跳过（需手工复制）");
            }
            try
            {
                File.Copy(src, Path.Combine(dir, "MEMORY.DMP"), true);
                s.Log("  · 已复制 MEMORY.DMP，" + Tool.FmtSize(fi.Length));
            }
            catch (Exception ex) { throw new Exception("复制 MEMORY.DMP 失败：" + ex.Message); }
        }

        /// <summary>对指定进程生成完整内存转储（GB 级），用于内存泄漏 / 卡死 / 挂起类问题。</summary>
        public static void ProcessDump(Session s)
        {
            string dir = s.Dir("33_进程转储");
            string name = (s.ProcessName ?? "").Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);

            Process target = null;
            if (name.Length == 0)
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        if (target == null || p.WorkingSet64 > target.WorkingSet64) target = p;
                    }
                    catch { }
                }
                if (target == null) throw new SkippedException("没有找到可以转储的进程");
                s.Log("  · 未指定进程名，取内存占用最大的：" + target.ProcessName + " (" + target.Id + ")");
            }
            else
            {
                Process[] arr = Process.GetProcessesByName(name);
                if (arr.Length == 0) throw new SkippedException("未找到进程：" + name + "（请确认进程名，例如 chrome）");
                target = arr[0];
                if (arr.Length > 1) s.Log("  · 找到 " + arr.Length + " 个同名进程，转储第一个（PID " + target.Id + "）");
            }

            long ws = 0;
            try { ws = target.WorkingSet64; } catch { }
            if (ws > 12L * 1024 * 1024 * 1024)
                throw new SkippedException("目标进程占用 " + Tool.FmtSize(ws) + "，超出 12 GB 上限，已跳过");
            if (ws > 1L * 1024 * 1024 * 1024)
                s.Log("  · 目标进程占用 " + Tool.FmtSize(ws) + "，转储可能持续数十秒");

            string file = Path.Combine(dir, Runner.SafeName(target.ProcessName) + "_" + target.Id + "_"
                                      + DateTime.Now.ToString("HHmmss") + ".dmp");
            bool ok = false;
            int err = 0;
            try
            {
                using (FileStream fs = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    IntPtr hProc = target.Handle;
                    ok = MiniDumpWriteDump(hProc, (uint)target.Id, fs.SafeFileHandle.DangerousGetHandle(),
                                           2 /*MiniDumpWithFullMemory*/, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (!ok) err = Marshal.GetLastWin32Error();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("生成进程转储失败：" + ex.Message + "（通常需要管理员权限，或进程受保护）");
            }
            if (!ok)
            {
                try { File.Delete(file); } catch { }
                throw new Exception("写入进程转储失败，错误码 " + err + "（进程受保护或权限不足，请以管理员运行）");
            }
            long size = 0;
            try { size = new FileInfo(file).Length; } catch { }
            if (size == 0) throw new Exception("转储文件为空，请以管理员身份重试");
            s.Log("  · 已生成 " + Path.GetFileName(file) + "，" + Tool.FmtSize(size));
        }

        [System.Runtime.InteropServices.DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, IntPtr hFile,
            uint dumpType, IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);

        public static void Wer(Session s)
        {
            string dir = s.Dir("31_应用崩溃报告");
            string[] roots = new string[] {
                @"C:\ProgramData\Microsoft\Windows\WER\ReportArchive",
                @"C:\ProgramData\Microsoft\Windows\WER\ReportQueue"
            };
            int copied = 0;
            long bytes = 0;
            foreach (string root in roots)
            {
                if (!Directory.Exists(root)) continue;
                List<DirectoryInfo> dirs = new List<DirectoryInfo>();
                try { dirs.AddRange(new DirectoryInfo(root).GetDirectories()); } catch { }
                dirs.Sort(delegate(DirectoryInfo a, DirectoryInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
                int max = Math.Min(dirs.Count, 25);
                for (int i = 0; i < max; i++)
                {
                    if (s.Token.IsCancellationRequested) return;
                    string target = Path.Combine(dir, Path.GetFileName(root) + "_" + dirs[i].Name);
                    long got = CopyTree(dirs[i].FullName, target, 200L * 1024 * 1024);
                    if (got > 0) { bytes += got; copied++; }
                }
            }
            if (copied == 0)
            {
                if (!s.IsAdmin)
                {
                    WriteNothing(dir, "未能复制 WER 崩溃报告。该目录（C:\\ProgramData\\Microsoft\\Windows\\WER）通常需要管理员权限，请以管理员身份重新运行。");
                    s.Log("  · 未能读取 WER 目录（需要管理员权限）");
                    throw new SkippedException("需要管理员权限才能读取 WER 目录");
                }
                WriteNothing(dir, "WER 目录中没有找到崩溃报告（该机器近期没有应用崩溃）。");
                s.Log("  · WER 目录为空");
                throw new SkippedException("WER 目录中没有崩溃报告（近期没有应用崩溃）");
            }
            s.Log("  · 已复制 " + copied + " 份崩溃报告，" + Tool.FmtSize(bytes));
        }

        // ---------------- 电源 ----------------
        public static void Battery(Session s)
        {
            string dir = s.Dir("40_电源电池");
            string o;
            string f = Path.Combine(dir, "电池报告.html");
            int rc = Tool.Run("powercfg.exe", "/batteryreport /output " + Tool.Q(f) + " /duration 14",
                              120000, s.Token, out o, Encoding.Default);
            WaitFile(f, 20000, s.Token);
            if (!File.Exists(f)) throw new Exception("电池报告生成失败：" + Trim(o, 200));
            ParseBatteryReport(f, s);
        }

        private static void ParseBatteryReport(string file, Session s)
        {
            try
            {
                string t = File.ReadAllText(file);
                Match m = Regex.Match(t, @"DESIGN CAPACITY.{0,300}?([\d,]+)\s*mWh",
                                      RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (m.Success) s.Probe["BAT.DesignedCapacity"] = m.Groups[1].Value.Replace(",", "");
                m = Regex.Match(t, @"FULL CHARGE CAPACITY.{0,300}?([\d,]+)\s*mWh",
                                RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (m.Success) s.Probe["BAT.FullChargedCapacity"] = m.Groups[1].Value.Replace(",", "");
                m = Regex.Match(t, @"CYCLE COUNT.{0,200}?<td>\s*(\d+)",
                                RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (m.Success) s.Probe["BAT.CycleCount"] = m.Groups[1].Value;
            }
            catch { }
        }

        public static void PowerCfg(Session s)
        {
            string dir = s.Dir("41_电源设置");
            StringBuilder sb = new StringBuilder();
            string[][] cmds = new string[][] {
                new string[] { "可用睡眠状态", "/a" },
                new string[] { "当前电源方案", "/getactivescheme" },
                new string[] { "阻止睡眠的请求", "/requests" },
                new string[] { "上次唤醒源", "/lastwake" },
                new string[] { "唤醒定时器", "/waketimers" },
                new string[] { "可唤醒设备", "/devicequery wake_armed" }
            };
            foreach (string[] c in cmds)
            {
                if (s.Token.IsCancellationRequested) return;
                string outp;
                Tool.Run("powercfg.exe", c[1], 60000, s.Token, out outp, Encoding.Default);
                sb.AppendLine("=== " + c[0] + "（powercfg " + c[1] + "）===");
                sb.AppendLine(outp);
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(dir, "电源与唤醒.txt"), sb.ToString(), new UTF8Encoding(true));
            CollectPowerSettings(s, dir);
        }

        /// <summary>需要抓取的电源设置：键、中文名、子组、设置、取值类型。</summary>
        public static readonly string[][] PowerItems = new string[][] {
            new string[] { "sleep",     "睡眠超时（无操作后进入睡眠）", "SUB_SLEEP",   "STANDBYIDLE",   "time" },
            new string[] { "hibernate", "休眠超时（无操作后进入休眠）", "SUB_SLEEP",   "HIBERNATEIDLE", "time" },
            new string[] { "monitor",   "关闭显示器",                 "SUB_VIDEO",   "VIDEOIDLE",     "time" },
            new string[] { "disk",      "关闭硬盘",                   "SUB_DISK",    "DISKIDLE",      "time" },
            new string[] { "unattend",  "无人参与系统睡眠",           "SUB_SLEEP",   "UNATTENDSLEEP", "time" },
            new string[] { "lid",       "合上盖子时",                 "SUB_BUTTONS", "LIDACTION",     "action" },
            new string[] { "powerbtn",  "按电源按钮时",               "SUB_BUTTONS", "PBUTTONACTION", "action" },
            new string[] { "hybrid",    "混合睡眠",                   "SUB_SLEEP",   "HYBRIDSLEEP",   "bool" }
        };

        private static void CollectPowerSettings(Session s, string dir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== 电源与睡眠超时设置 ===");
            sb.AppendLine("（交流 = 插电源，直流 = 电池供电；数值为系统当前生效值）");
            sb.AppendLine();
            sb.AppendLine(Pad("项目", 30) + Pad("交流(AC)", 24) + Pad("电池(DC)", 24) + "原始值");
            sb.AppendLine(new string('-', 92));

            StringBuilder csv = new StringBuilder();
            csv.AppendLine("项目,交流AC,电池DC,交流原始值,电池原始值");

            foreach (string[] item in PowerItems)
            {
                if (s.Token.IsCancellationRequested) return;
                string key = item[0], name = item[1], sub = item[2], setting = item[3], kind = item[4];
                string outp;
                Tool.Run("powercfg.exe", "/qh SCHEME_CURRENT " + sub + " " + setting, 30000, s.Token, out outp, Encoding.Default);
                long ac, dc;
                if (!ParseIndices(outp, out ac, out dc)) continue;

                string sa = FormatValue(ac, kind);
                string sd = FormatValue(dc, kind);
                s.Probe["PWR." + key + "_ac"] = sa;
                s.Probe["PWR." + key + "_dc"] = sd;
                s.Probe["PWR." + key + "_acraw"] = ac.ToString();
                s.Probe["PWR." + key + "_dcraw"] = dc.ToString();

                sb.AppendLine(Pad(name, 30) + Pad(sa, 24) + Pad(sd, 24) + ac + "/" + dc);
                csv.AppendLine(name + "," + sa + "," + sd + "," + ac + "," + dc);
            }

            sb.AppendLine();
            sb.AppendLine("原始值说明：时间类单位为秒，0 或 4294967295 表示“从不”；");
            sb.AppendLine("动作类 0=不采取任何操作 1=睡眠 2=休眠 3=关机 4=关闭显示器；布尔类 0=关闭 1=开启。");

            File.WriteAllText(Path.Combine(dir, "电源设置.txt"), sb.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(dir, "电源设置.csv"), csv.ToString(), new UTF8Encoding(true));

            StringBuilder sum = new StringBuilder();
            sum.AppendLine("=== 电源与睡眠超时（当前电源方案） ===");
            foreach (string[] item in PowerItems)
            {
                string k = "PWR." + item[0] + "_ac";
                if (!s.Probe.ContainsKey(k)) continue;
                sum.AppendLine("  " + item[1] + "：交流 " + s.Probe[k] + " / 电池 " + s.Probe["PWR." + item[0] + "_dc"]);
            }
            s.Log(sum.ToString().TrimEnd());
        }

        /// <summary>从 powercfg /query 输出中取出最后两个十六进制值（交流、直流）。</summary>
        private static bool ParseIndices(string text, out long ac, out long dc)
        {
            ac = dc = 0;
            MatchCollection ms = Regex.Matches(text, "0x([0-9A-Fa-f]{8})");
            if (ms.Count < 2) return false;
            long a, d;
            if (!long.TryParse(ms[ms.Count - 2].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a)) return false;
            if (!long.TryParse(ms[ms.Count - 1].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out d)) return false;
            ac = a; dc = d;
            return true;
        }

        /// <summary>按显示宽度补空格（中文算 2 列），保证 txt 里的表格对齐。</summary>
        private static string Pad(string text, int width)
        {
            int w = 0;
            foreach (char c in text) w += (c >= 0x2E80) ? 2 : 1;
            if (w >= width) return text;
            return text + new string(' ', width - w);
        }

        private static string FormatValue(long v, string kind)
        {
            if (kind == "action")
            {
                switch ((int)v)
                {
                    case 0: return "不采取任何操作";
                    case 1: return "睡眠";
                    case 2: return "休眠";
                    case 3: return "关机";
                    case 4: return "关闭显示器";
                    default: return "未知（" + v + "）";
                }
            }
            if (kind == "bool") return v == 0 ? "关闭" : "开启";
            if (v == 0 || v == 4294967295L) return "从不";
            if (v < 60) return v + " 秒";
            if (v < 3600)
            {
                long m = v / 60, r = v % 60;
                return r == 0 ? m + " 分钟" : m + " 分 " + r + " 秒";
            }
            long h = v / 3600, mm = (v % 3600) / 60;
            return mm == 0 ? h + " 小时" : h + " 小时 " + mm + " 分";
        }

        public static void Energy(Session s)
        {
            string dir = s.Dir("42_能耗诊断");
            string f = Path.Combine(dir, "能耗诊断.html");
            string o;
            int rc = Tool.Run("powercfg.exe", "/energy /output " + Tool.Q(f) + " /duration 60",
                              180000, s.Token, out o, Encoding.Default);
            WaitFile(f, 30000, s.Token);
            if (!File.Exists(f)) throw new Exception("能耗诊断失败（需管理员权限）：" + Trim(o, 200));
        }

        public static void SleepStudy(Session s)
        {
            string dir = s.Dir("43_睡眠诊断");
            string f = Path.Combine(dir, "睡眠诊断.html");
            string o;
            Tool.Run("powercfg.exe", "/sleepstudy /output " + Tool.Q(f), 180000, s.Token, out o, Encoding.Default);
            WaitFile(f, 30000, s.Token);
            if (!File.Exists(f)) throw new Exception("睡眠诊断失败（需管理员权限，且系统需支持 Modern Standby）：" + Trim(o, 200));
        }

        // ---------------- 性能 ----------------
        public static void Perf(Session s)
        {
            string dir = s.Dir("50_性能");
            string ps = @"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
Write-Output '时间,CPU使用率%,可用内存MB,提交内存MB,磁盘忙%,CPU当前频率MHz,电池电量'
for($i=0; $i -lt 20; $i++){
  $cpu = ''
  try { $cpu = [string](Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter ""Name='_Total'"").PercentProcessorTime } catch {}
  $availM = ''; $commitM = ''
  try { $m = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory; $availM = [string][math]::Round($m.AvailableMBytes,0); $commitM = [string][math]::Round($m.CommittedBytes/1MB,0) } catch {}
  $disk = ''
  try { $disk = [string](Get-CimInstance Win32_PerfFormattedData_PerfDisk_LogicalDisk -Filter ""Name='_Total'"").PercentDiskTime } catch {}
  $freq = ''
  try { $freq = [string](Get-CimInstance Win32_Processor | Select-Object -First 1).CurrentClockSpeed } catch {}
  $bat = ''
  try { $bat = [string](Get-CimInstance Win32_Battery | Select-Object -First 1).EstimatedChargeRemaining } catch {}
  $t = (Get-Date).ToString('HH:mm:ss')
  Write-Output ($t + ',' + $cpu + ',' + $availM + ',' + $commitM + ',' + $disk + ',' + $freq + ',' + $bat)
  Start-Sleep -Seconds 1
}
";
            string outp = Tool.Ps(ps, 240000, s.Token);
            if (outp.Trim().Length == 0) throw new Exception("性能采样未返回数据");
            File.WriteAllText(Path.Combine(dir, "性能采样.csv"), outp, new UTF8Encoding(true));
        }

        public static void Gpu(Session s)
        {
            string dir = s.Dir("60_显示显卡");
            string f = Path.Combine(dir, "dxdiag.txt");
            string o;
            Tool.Run("dxdiag.exe", "/t " + Tool.Q(f), 120000, s.Token, out o, Encoding.Default);
            WaitFile(f, 60000, s.Token);
            if (!File.Exists(f)) WriteNothing(dir, "dxdiag 未生成报告（可能被安全软件拦截）。");
        }

        // ---------------- 网络 ----------------
        public static void NetCfg(Session s)
        {
            string dir = s.Dir("70_网络");
            StringBuilder sb = new StringBuilder();
            string[][] cmds = new string[][] {
                new string[] { "IP 配置", "ipconfig /all" },
                new string[] { "路由表", "route print" },
                new string[] { "ARP 表", "arp -a" },
                new string[] { "无线接口", "netsh wlan show interfaces" },
                new string[] { "无线驱动能力", "netsh wlan show drivers" },
                new string[] { "TCP 全局参数", "netsh int tcp show global" },
                new string[] { "代理设置", "netsh winhttp show proxy" }
            };
            foreach (string[] c in cmds)
            {
                if (s.Token.IsCancellationRequested) return;
                string outp;
                Tool.Run("cmd.exe", "/c " + c[1], 60000, s.Token, out outp, Encoding.Default);
                sb.AppendLine("=== " + c[0] + " ===");
                sb.AppendLine(outp);
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(dir, "网络配置.txt"), sb.ToString(), new UTF8Encoding(true));
        }

        public static void WlanReport(Session s)
        {
            string dir = s.Dir("71_无线报告");
            string o;
            Tool.Run("netsh.exe", "wlan show wlanreport", 120000, s.Token, out o, Encoding.Default);
            string src = @"C:\ProgramData\Microsoft\Windows\WlanReport\wlan-report-latest.html";
            WaitFile(src, 30000, s.Token);
            if (File.Exists(src))
            {
                File.Copy(src, Path.Combine(dir, "无线历史报告.html"), true);
                string side = @"C:\ProgramData\Microsoft\Windows\WlanReport\wlan-report-latest.xml";
                if (File.Exists(side))
                {
                    try { File.Copy(side, Path.Combine(dir, "无线历史报告.xml"), true); } catch { }
                }
            }
            else
            {
                WriteNothing(dir, "无线历史报告生成失败（可能没有无线网卡，或需要管理员权限）。");
            }
        }

        // ---------------- 存储 ----------------
        public static void Disk(Session s)
        {
            string dir = s.Dir("80_存储");
            string ps = @"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
'=== 物理磁盘 ==='
Get-PhysicalDisk | Select-Object DeviceId,FriendlyName,MediaType,BusType,HealthStatus,OperationalStatus,@{n='SizeGB';e={[math]::Round($_.Size/1GB,0)}} | Format-Table -AutoSize | Out-String -Width 200
'=== 可靠性计数器（SMART） ==='
Get-PhysicalDisk | ForEach-Object {
  $d = $_
  $r = $null
  try { $r = $d | Get-StorageReliabilityCounter } catch {}
  if($r){
    '磁盘: ' + $d.FriendlyName
    '  温度(C): ' + $r.Temperature + '   通电小时: ' + $r.PowerOnHours + '   磨损: ' + $r.Wear + '   读错误: ' + $r.ReadErrorsTotal + '   写错误: ' + $r.WriteErrorsTotal
  }
}
'=== 卷 ==='
Get-Volume | Select-Object DriveLetter,FileSystemLabel,FileSystem,HealthStatus,@{n='SizeGB';e={[math]::Round($_.Size/1GB,0)}},@{n='FreeGB';e={[math]::Round($_.SizeRemaining/1GB,0)}} | Format-Table -AutoSize | Out-String -Width 200
'=== 磁盘 ==='
Get-Disk | Select-Object Number,FriendlyName,PartitionStyle,@{n='SizeGB';e={[math]::Round($_.Size/1GB,0)}},HealthStatus,OperationalStatus | Format-Table -AutoSize | Out-String -Width 200
";
            string outp = Tool.Ps(ps, 120000, s.Token);
            File.WriteAllText(Path.Combine(dir, "磁盘健康.txt"), outp, new UTF8Encoding(true));
        }

        // ---------------- 附加 ----------------
        public static void AppDir(Session s)
        {
            if (string.IsNullOrEmpty(s.AppDir) || !Directory.Exists(s.AppDir))
                throw new Exception("未指定有效的附加日志目录");
            string dir = s.Dir("90_附加日志");
            long bytes = CopyTree(s.AppDir, Path.Combine(dir, new DirectoryInfo(s.AppDir).Name), 1500L * 1024 * 1024);
            if (bytes == 0) WriteNothing(dir, "目标目录为空或不可读：" + s.AppDir);
        }

        // ---------------- 工具方法 ----------------
        private static void ParseEvents(string tsv, string logName, Session s)
        {
            if (string.IsNullOrEmpty(tsv)) return;
            foreach (string line in tsv.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = line.Split('\t');
                if (p.Length < 4) continue;
                EventRow e = new EventRow();
                DateTime t;
                if (!DateTime.TryParseExact(p[0].Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                                            DateTimeStyles.None, out t)) continue;
                e.Time = t;
                int id;
                if (!int.TryParse(p[1].Trim(), out id)) continue;
                e.Id = id;
                e.Provider = p[2].Trim();
                e.Level = p[3].Trim();
                e.Message = p.Length > 4 ? p[4].Trim() : "";
                e.Log = logName;
                s.Events.Add(e);
                if (t < s.EventMin) s.EventMin = t;
                if (t > s.EventMax) s.EventMax = t;
            }
        }

        private static void ParseProbe(string tsv, Session s)
        {
            if (string.IsNullOrEmpty(tsv)) return;
            foreach (string line in tsv.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = line.Split('\t');
                if (p.Length < 3) continue;
                string key = p[0].Trim() + "." + p[1].Trim();
                string val = p[2].Trim();
                if (!s.Probe.ContainsKey(key)) s.Probe[key] = val;
            }
        }

        public static void WaitFile(string path, int timeoutMs, CancellationToken token)
        {
            int waited = 0;
            long last = -1;
            int stable = 0;
            while (waited < timeoutMs)
            {
                if (File.Exists(path))
                {
                    long len = 0;
                    try { len = new FileInfo(path).Length; } catch { }
                    if (len > 0 && len == last) stable++;
                    else stable = 0;
                    last = len;
                    if (stable >= 2) return;
                }
                Thread.Sleep(250);
                waited += 250;
                if (token.IsCancellationRequested) return;
            }
        }

        private static void WriteNothing(string dir, string text)
        {
            try { File.WriteAllText(Path.Combine(dir, "说明.txt"), text, new UTF8Encoding(true)); }
            catch { }
        }

        private static string Trim(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > n ? s.Substring(0, n) : s;
        }

        public static long CopyTree(string src, string dst, long maxBytes)
        {
            long total = 0;
            Queue<string> dirs = new Queue<string>();
            dirs.Enqueue(src);
            Directory.CreateDirectory(dst);
            while (dirs.Count > 0)
            {
                if (total > maxBytes) break;
                string cur = dirs.Dequeue();
                try
                {
                    foreach (string d in Directory.GetDirectories(cur)) dirs.Enqueue(d);
                }
                catch { }
                try
                {
                    foreach (string f in Directory.GetFiles(cur))
                    {
                        if (total > maxBytes) break;
                        try
                        {
                            FileInfo fi = new FileInfo(f);
                            string rel = f.Substring(src.Length).TrimStart('\\');
                            string target = Path.Combine(dst, rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            File.Copy(f, target, true);
                            total += fi.Length;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return total;
        }
    }
}
