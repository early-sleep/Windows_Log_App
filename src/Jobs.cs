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

            list.Add(new JobDef { Id = "events-system", Category = "Basics", Name = "System event log + key event analysis", Hint = "BSOD, unexpected shutdown, driver and hardware errors (~10 s)", DefaultOn = true, Run = Jobs.SystemEvents });
            list.Add(new JobDef { Id = "events-app", Category = "Basics", Name = "Application event log", Hint = "App crashes and hangs (~10 s)", DefaultOn = true, Run = Jobs.AppEvents });
            list.Add(new JobDef { Id = "evtx-all", Category = "Basics", Name = "All event log files (.evtx)", Hint = "Mirrors winevt\\Logs incl. empty folders (~30 s as admin); ~2 min without", DefaultOn = true, Run = Jobs.AllEventLogs });
            list.Add(new JobDef { Id = "sysinfo", Category = "Basics", Name = "System info and hardware details", Hint = "Model, BIOS, CPU, memory, battery, problem devices (~15 s)", DefaultOn = true, Run = Jobs.SysInfo });
            list.Add(new JobDef { Id = "drivers", Category = "Basics", Name = "Drivers and devices", Hint = "Driver versions, signing, problem devices (~10 s)", DefaultOn = true, Run = Jobs.Drivers });
            list.Add(new JobDef { Id = "snapshot", Category = "Basics", Name = "Processes, services and startup", Hint = "Current runtime state (~5 s)", DefaultOn = true, Run = Jobs.Snapshot });
            list.Add(new JobDef { Id = "software", Category = "Basics", Name = "Patches and installed software", Hint = "Track environment changes (~5 s)", DefaultOn = true, Run = Jobs.Software });
            list.Add(new JobDef { Id = "msinfo32", Category = "Basics", Name = "msinfo32 full report (slow)", Hint = "More detail, but may hang on some machines (off by default)", DefaultOn = false, Run = Jobs.Msinfo32 });

            list.Add(new JobDef { Id = "minidump", Category = "Crashes and dumps", Name = "Blue screen dumps (Minidump)", Hint = "Essential for BSOD (~3 s)", DefaultOn = true, Run = Jobs.MiniDump });
            list.Add(new JobDef { Id = "wer", Category = "Crashes and dumps", Name = "WER crash reports (full folder)", Hint = "Mirrors C:\\ProgramData\\Microsoft\\Windows\\WER; needs admin", DefaultOn = true, NeedAdmin = true, Run = Jobs.Wer });
            list.Add(new JobDef { Id = "livekernel", Category = "Crashes and dumps", Name = "Live kernel reports", Hint = "Mirrors C:\\Windows\\LiveKernelReports; needs admin", DefaultOn = true, NeedAdmin = true, Run = Jobs.LiveKernel });
            list.Add(new JobDef { Id = "memdump", Category = "Crashes and dumps", Name = "Full memory dump (MEMORY.DMP)", Hint = "Up to several GB; only if the system already created it", DefaultOn = false, Run = Jobs.MemDump });
            list.Add(new JobDef { Id = "pdump", Category = "Crashes and dumps", Name = "Process memory dump (GB, target process)", Hint = "For leaks/hangs; enter a process name on the right (~10-60 s)", DefaultOn = false, Run = Jobs.ProcessDump });

            list.Add(new JobDef { Id = "battery", Category = "Power and battery", Name = "Battery health report", Hint = "Capacity, cycles, health (~5 s)", DefaultOn = true, Run = Jobs.Battery });
            list.Add(new JobDef { Id = "powercfg", Category = "Power and battery", Name = "Power settings and wake sources", Hint = "Sleep, wake, sleep blockers (~5 s)", DefaultOn = true, Run = Jobs.PowerCfg });
            list.Add(new JobDef { Id = "energy", Category = "Power and battery", Name = "Energy report (60 s audit)", Hint = "Requires admin (~60 s)", DefaultOn = false, NeedAdmin = true, Run = Jobs.Energy });
            list.Add(new JobDef { Id = "sleepstudy", Category = "Power and battery", Name = "Sleep study + system power report", Hint = "powercfg /sleepstudy and /spr; requires admin (~60 s)", DefaultOn = true, NeedAdmin = true, Run = Jobs.SleepStudy });

            list.Add(new JobDef { Id = "perf", Category = "Performance", Name = "Performance sampling (30 s: CPU/memory/disk)", Hint = "~35 s; do not use the machine while sampling", DefaultOn = false, Run = Jobs.Perf });
            list.Add(new JobDef { Id = "gpu", Category = "Performance", Name = "Graphics and display (dxdiag)", Hint = "Includes TDR records (~20 s)", DefaultOn = true, Run = Jobs.Gpu });

            list.Add(new JobDef { Id = "netcfg", Category = "Network", Name = "Network configuration snapshot", Hint = "IP, routes, Wi-Fi, Bluetooth (~5 s)", DefaultOn = true, Run = Jobs.NetCfg });
            list.Add(new JobDef { Id = "wlan", Category = "Network", Name = "Wi-Fi connection history", Hint = "Connect/disconnect timeline; requires admin (~25 s)", DefaultOn = true, NeedAdmin = true, Run = Jobs.WlanReport });

            list.Add(new JobDef { Id = "disk", Category = "Storage", Name = "Disk and SMART health", Hint = "Health, temperature, power-on hours (~5 s)", DefaultOn = true, Run = Jobs.Disk });

            list.Add(new JobDef { Id = "appdir", Category = "Other", Name = "Extra log folder (custom path)", Hint = "Logs of the application under test", DefaultOn = false, Run = Jobs.AppDir });

            return list;
        }
    }

    public static class Jobs
    {
        // ---------------- basics ----------------
        public static void SystemEvents(Session s)
        {
            string dir = s.Dir("10_System_Logs");
            s.Source(dir, @"C:\Windows\System32\winevt\Logs\System.evtx", "exported from the System channel with wevtutil");
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
if($null -eq $ev){ Write-Output ('# # No key events found in the last 7 days (the system log may have been cleared or rolled over)') }
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
            File.WriteAllText(Path.Combine(dir, "key_events.csv"), outp, new UTF8Encoding(true));
            ParseEvents(outp, "System", s);
        }

        /// <summary>把 winevt\Logs 下所有 .evtx 连目录结构（含空目录）整体镜像出来。</summary>
        public static void AllEventLogs(Session s)
        {
            string dir = s.Dir("12_All_Event_Logs");
            string root = @"C:\Windows\System32\winevt\Logs";
            string dest = Path.Combine(dir, "Logs");
            if (!Directory.Exists(root)) throw new SkippedException("Event log folder not found: " + root);
            Directory.CreateDirectory(dest);

            StringBuilder index = new StringBuilder();
            index.AppendLine("RelativePath,Type,SizeBytes,LastWriteTime,Status");
            int dirCount = 0, fileCount = 0, failed = 0;
            long total = 0;

            long free = 0;
            try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))).AvailableFreeSpace; } catch { }
            const long Reserve = 500L * 1024 * 1024;

            // 1) 目录结构：空目录也照原样建立
            List<string> dirs = new List<string>();
            try { dirs.AddRange(Directory.GetDirectories(root, "*", SearchOption.AllDirectories)); }
            catch (Exception ex) { s.Log("  · Directory enumeration incomplete: " + ex.Message); }
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string d in dirs)
            {
                if (s.Token.IsCancellationRequested) return;
                string rel = d.Substring(root.Length).TrimStart('\\');
                try
                {
                    Directory.CreateDirectory(Path.Combine(dest, rel));
                    dirCount++;
                    bool hasEvtx = false;
                    try { hasEvtx = Directory.GetFiles(d, "*.evtx").Length > 0; } catch { }
                    index.AppendLine(Csv(rel) + ",directory,0,," + (hasEvtx ? "has-evtx" : "empty"));
                }
                catch (Exception ex)
                {
                    index.AppendLine(Csv(rel) + ",directory,0,,failed: " + CsvClean(ex.Message));
                }
            }

            // 2) .evtx 文件
            List<string> files = new List<string>();
            try { files.AddRange(Directory.GetFiles(root, "*.evtx", SearchOption.AllDirectories)); }
            catch (Exception ex) { s.Log("  · File enumeration incomplete: " + ex.Message); }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                if (s.Token.IsCancellationRequested) return;
                if (free > 0 && free < Reserve)
                {
                    index.AppendLine(Csv(f.Substring(root.Length).TrimStart('\\')) + ",evtx,0,,skipped: not enough free space");
                    failed++;
                    continue;
                }
                string rel = f.Substring(root.Length).TrimStart('\\');
                string target = Path.Combine(dest, rel);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    FileInfo fi = new FileInfo(f);
                    File.Copy(f, target, true);
                    long len = new FileInfo(target).Length;
                    total += len;
                    free -= len;
                    fileCount++;
                    index.AppendLine(Csv(rel) + ",evtx," + len + "," + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + ",copied");
                }
                catch (Exception ex)
                {
                    failed++;
                    index.AppendLine(Csv(rel) + ",evtx,0,,failed: " + CsvClean(ex.Message));
                }
            }

            File.WriteAllText(Path.Combine(dir, "EventLog_Index.csv"), index.ToString(), new UTF8Encoding(true));

            if (fileCount == 0)
            {
                int ch = ExportChannels(s, dir);
                if (ch > 0)
                {
                    try
                    {
                        if (Directory.Exists(dest) && Directory.GetFileSystemEntries(dest).Length == 0)
                            Directory.Delete(dest, true);
                    }
                    catch { }
                    s.Log("  · Folder mirror blocked (needs admin); exported " + ch + " channel(s) instead");
                    WriteNothing(dir, "The winevt\\Logs folder can only be read by administrators, so the original\r\n"
                                    + "folder structure was NOT mirrored (empty folders are also unavailable this way).\r\n\r\n"
                                    + "Instead, " + ch + " readable event channels were exported with wevtutil into the\r\n"
                                    + "Channels subfolder, one .evtx per channel.\r\n\r\n"
                                    + "Run the tool as administrator to get an exact mirror of winevt\\Logs,\r\n"
                                    + "including every subfolder and empty folder.");
                    return;
                }
                throw new SkippedException("No .evtx file could be copied (" + failed
                    + " failed, likely locked or access denied). Run as administrator to mirror winevt\\Logs.");
            }
            s.Log("  · " + fileCount + " .evtx file(s), " + dirCount + " folder(s), " + Tool.FmtSize(total)
                  + (failed > 0 ? ", " + failed + " failed" : ""));
        }

        /// <summary>备用路径：按通道逐个导出，标准用户也能拿到大部分日志。</summary>
        private static int ExportChannels(Session s, string dir)
        {
            string chDir = Path.Combine(dir, "Channels");
            Directory.CreateDirectory(chDir);
            string list;
            Tool.Run("wevtutil.exe", "el", 120000, s.Token, out list, Encoding.Default);
            if (string.IsNullOrEmpty(list)) return 0;

            StringBuilder idx = new StringBuilder();
            idx.AppendLine("Channel,File,SizeBytes,Status");
            int ok = 0;
            foreach (string raw in list.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (s.Token.IsCancellationRequested) break;
                string channel = raw.Trim();
                if (channel.Length == 0) continue;
                string safe = Runner.SafeName(channel);
                string f = Path.Combine(chDir, safe + ".evtx");
                int dup = 1;
                while (File.Exists(f))
                {
                    dup++;
                    f = Path.Combine(chDir, safe + "_" + dup + ".evtx");
                }
                string err;
                Tool.Run("wevtutil.exe", "epl " + Tool.Q(channel) + " " + Tool.Q(f), 60000, s.Token, out err, Encoding.Default);
                long len = 0;
                try { if (File.Exists(f)) len = new FileInfo(f).Length; } catch { }
                if (len > 0)
                {
                    ok++;
                    idx.AppendLine(Csv(channel) + "," + Csv(safe + ".evtx") + "," + len + ",exported");
                }
                else
                {
                    idx.AppendLine(Csv(channel) + ",,0,failed: " + CsvClean(err));
                }
            }
            File.WriteAllText(Path.Combine(dir, "Channel_Index.csv"), idx.ToString(), new UTF8Encoding(true));
            return ok;
        }

        private static string Csv(string v)
        {
            return "\"" + (v == null ? "" : v.Replace("\"", "\"\"")) + "\"";
        }

        private static string CsvClean(string v)
        {
            return v == null ? "" : v.Replace("\r", " ").Replace("\n", " ").Replace(",", ";");
        }

        public static void AppEvents(Session s)
        {
            string dir = s.Dir("11_Application_Logs");
            s.Source(dir, @"C:\Windows\System32\winevt\Logs\Application.evtx", "exported from the Application channel with wevtutil");
            string o;
            Tool.Run("wevtutil.exe", "epl Application " + Tool.Q(Path.Combine(dir, "Application.evtx")), 180000, s.Token, out o, Encoding.Default);

            string script = @"
$ids = 1000,1001,1002,1026,11707
$start = (Get-Date).AddDays(-7)
$ev = $null
try { $ev = Get-WinEvent -FilterHashtable @{LogName='Application'; Id=$ids; StartTime=$start} -MaxEvents 150 -ErrorAction SilentlyContinue } catch { }
$culture = [System.Globalization.CultureInfo]::InvariantCulture
if($null -eq $ev){ Write-Output ('# # No application crashes in the last 7 days') }
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
            File.WriteAllText(Path.Combine(dir, "app_crash_events.csv"), outp, new UTF8Encoding(true));
            ParseEvents(outp, "Application", s);
        }

        public static void SysInfo(Session s)
        {
            string dir = s.Dir("20_System_Info");
            s.Source(dir, @"WMI/CIM + systeminfo + powercfg report parsers", "queried from the running system");
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
  $t = '{0} / driver {1} / {2}x{3} @{4}Hz' -f $_.Name, $_.DriverVersion, $_.CurrentHorizontalResolution, $_.CurrentVerticalResolution, $_.CurrentRefreshRate
  E 'GPU' $_.Name $t
}
Get-CimInstance Win32_DiskDrive | ForEach-Object {
  $t = '{0} GB / {1} / Firmware {2}' -f [math]::Round($_.Size/1GB,0), $_.InterfaceType, $_.FirmwareRevision
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
  E 'PNP' $_.Name ('Error code ' + $_.ConfigManagerErrorCode)
}
$os = Get-CimInstance Win32_OperatingSystem
E 'OS' 'Caption' $os.Caption
E 'OS' 'Version' $os.Version
E 'OS' 'Build' $os.BuildNumber
E 'OS' 'Install' $os.InstallDate
E 'OS' 'LastBoot' $os.LastBootUpTime
";
            string probeOut = Tool.Ps(probe, 120000, s.Token);
            File.WriteAllText(Path.Combine(dir, "hardware_info.csv"), probeOut, new UTF8Encoding(true));
            ParseProbe(probeOut, s);
            File.WriteAllText(Path.Combine(dir, "system_summary.txt"), ProbeSummary(s), new UTF8Encoding(true));
        }

        public static void Msinfo32(Session s)
        {
            string dir = s.Dir("24_msinfo32");
            s.Source(dir, @"msinfo32.exe /report", "generated on this machine");
            string f = Path.Combine(dir, "msinfo32.txt");
            string o;
            Tool.Run("msinfo32.exe", "/report " + Tool.Q(f), 45000, s.Token, out o, Encoding.Default);
            WaitFile(f, 40000, s.Token);
            if (!File.Exists(f))
                throw new Exception("msinfo32 did not produce a file in time (it hangs on some machines); use another item instead");
        }

        private static string ProbeSummary(Session s)
        {
            StringBuilder b = new StringBuilder();
            string[][] groups = new string[][] {
                new string[] { "SYS", "Computer" },
                new string[] { "BIOS", "BIOS and serial" },
                new string[] { "CPU", "Processor" },
                new string[] { "MEM", "Memory" },
                new string[] { "GPU", "Graphics" },
                new string[] { "DISK", "Disk" },
                new string[] { "BAT", "Battery" },
                new string[] { "PNP", "Problem devices" },
                new string[] { "OS", "Operating system" }
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
            b.AppendLine("=== Collection context ===");
            b.AppendLine("  Computer name : " + s.Info.ComputerName);
            b.AppendLine("  User : " + s.Info.CurrentUser);
            b.AppendLine("  Privilege : " + (s.IsAdmin ? "Administrator" : "Standard user"));
            b.AppendLine("  Booted at : " + s.Info.BootTime + " (up " + s.Info.Uptime + ")");
            return b.ToString();
        }

        public static void Drivers(Session s)
        {
            string dir = s.Dir("21_Drivers_Devices");
            s.Source(dir, @"driverquery /v   +   pnputil /enum-drivers", "queried from the running system");
            string o;
            Tool.Run("cmd.exe", "/c driverquery /v /fo csv > " + Tool.Q(Path.Combine(dir, "drivers.csv")),
                     120000, s.Token, out o, Encoding.Default);
            Tool.Run("cmd.exe", "/c pnputil /enum-drivers > " + Tool.Q(Path.Combine(dir, "driver_store.txt")),
                     120000, s.Token, out o, Encoding.Default);
            string ps = @"Get-PnpDevice | Where-Object { $_.Status -ne 'OK' } | Select-Object Status,Class,FriendlyName,InstanceId | Format-Table -AutoSize | Out-String -Width 300";
            string outp = Tool.Ps(ps, 90000, s.Token);
            File.WriteAllText(Path.Combine(dir, "problem_devices.txt"), outp, new UTF8Encoding(true));
        }

        public static void Snapshot(Session s)
        {
            string dir = s.Dir("22_Runtime_Snapshot");
            s.Source(dir, @"Get-Process / Get-Service / startup items / scheduled tasks", "queried from the running system");
            string ps = @"
'=== Top 30 processes by memory ==='
Get-Process | Sort-Object WS -Descending | Select-Object -First 30 Name,Id,@{n='CPU_s';e={[math]::Round($_.CPU,1)}},@{n='MemMB';e={[math]::Round($_.WS/1MB,0)}},Handles,Threads | Format-Table -AutoSize | Out-String -Width 300
'=== Services not running ==='
Get-Service | Where-Object { $_.Status -ne 'Running' } | Select-Object Status,StartType,Name,DisplayName | Format-Table -AutoSize | Out-String -Width 300
'=== Startup items ==='
Get-CimInstance Win32_StartupCommand | Select-Object Name,Command,Location | Format-Table -AutoSize | Out-String -Width 300
'=== Scheduled tasks (non-Microsoft) ==='
Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\Microsoft\*' } | Select-Object TaskName,State | Format-Table -AutoSize | Out-String -Width 300
";
            string outp = Tool.Ps(ps, 120000, s.Token);
            File.WriteAllText(Path.Combine(dir, "processes_services_startup.txt"), outp, new UTF8Encoding(true));
        }

        public static void Software(Session s)
        {
            string dir = s.Dir("23_Patches_Software");
            s.Source(dir, @"Get-HotFix + HKLM uninstall registry keys", "queried from the running system");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Installed updates ===");
            string ps = @"Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 30 HotFixID,Description,InstalledOn | Format-Table -AutoSize | Out-String -Width 200";
            sb.AppendLine(Tool.Ps(ps, 60000, s.Token));
            sb.AppendLine();
            sb.AppendLine("=== Installed software ===");
            sb.AppendLine(ReadInstalledApps());
            File.WriteAllText(Path.Combine(dir, "patches_and_software.txt"), sb.ToString(), new UTF8Encoding(true));
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

        // ---------------- crashes ----------------
        public static void MiniDump(Session s)
        {
            string dir = s.Dir("30_Crash_Dumps");
            s.Source(dir, @"C:\Windows\Minidump", "copied as-is");
            string src = @"C:\Windows\Minidump";
            int n = 0;
            long bytes = 0;
            List<string> skipped = new List<string>();
            long free = 0;
            try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))).AvailableFreeSpace; }
            catch { }
            const long Reserve = 2L * 1024 * 1024 * 1024;   // keep at least 2 GB free on the target drive

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
                        skipped.Add(fi.FullName + "（" + Tool.FmtSize(fi.Length) + ", not enough free space on target)");
                        s.Log("  · Not enough space, skipped " + fi.Name + "（" + Tool.FmtSize(fi.Length) + "）");
                        continue;
                    }
                    if (fi.Length > 2L * 1024 * 1024 * 1024)
                        s.Log("  · Copying large file " + fi.Name + "（" + Tool.FmtSize(fi.Length) + "), this may take several minutes...");
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
                        skipped.Add(fi.FullName + " (copy failed: " + ex.Message + "）");
                    }
                }
            }

            if (skipped.Count > 0)
            {
                StringBuilder note = new StringBuilder();
                note.AppendLine("The following dump files were NOT copied. They still exist on the system; copy them manually if needed:");
                foreach (string t in skipped) note.AppendLine("  " + t);
                File.WriteAllText(Path.Combine(dir, "skipped_large_files.txt"), note.ToString(), new UTF8Encoding(true));
            }

            if (n == 0)
            {
                string reason = skipped.Count > 0
                    ? "Dump file too large or copy failed; see 30_Crash_Dumps\\skipped_large_files.txt"
                    : "No blue screen dump on this system (" + DumpModeName() + ", no BSOD in the last 30 days)";
                if (skipped.Count == 0) WriteNothing(dir, "No minidump files were found.\r\n\r\n" + DumpPolicy());
                s.Log("  · No dump file copied");
                throw new SkippedException(reason);
            }
            s.Log("  · Copied " + n + " dump file(s), " + Tool.FmtSize(bytes)
                  + (skipped.Count > 0 ? " (" + skipped.Count + " not copied)" : ""));
        }

        /// <summary>Reads the current dump configuration and returns a readable summary.</summary>
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
                        b.AppendLine("Current dump setting: " + ModeName(mode));
                        b.AppendLine("MEMORY.DMP path: " + k.GetValue("DumpFile"));
                        b.AppendLine("Minidump path: " + k.GetValue("MinidumpDir"));
                    }
                }
            }
            catch { }
            b.AppendLine("Physical memory: " + s_Ram + " GB");
            try
            {
                foreach (CimLite p in PageFiles())
                    b.AppendLine("Page file: " + p.Name + " " + p.SizeMB + " MB");
            }
            catch { }
            b.AppendLine();
            b.AppendLine("Assessment: " + ModeAdvice(mode));
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
                case 0: return "None (no dump is written on BSOD)";
                case 1: return "Complete memory dump (MEMORY.DMP, about the size of RAM)";
                case 2: return "Kernel memory dump (MEMORY.DMP, usually 1-4 GB)";
                case 3: return "Small memory dump (Minidump, about 256 KB each)";
                case 7: return "Automatic memory dump (MEMORY.DMP, size chosen by Windows)";
                default: return "Unknown (" + mode + ")";
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
            return "Unknown dump setting";
        }

        private static string ModeAdvice(int mode)
        {
            if (mode == 3)
                return "Currently set to Small memory dump: each BSOD produces only a ~256 KB Minidump, and MEMORY.DMP is never created."
                     + " To get a GB-sized full dump, change \"Write debugging information\" to Kernel or Complete memory dump.";
            if (mode == 0)
                return "Dumps are disabled; a BSOD leaves no file. Set \"Write debugging information\" to Small memory dump or higher.";
            if (mode == 1 || mode == 2 || mode == 7)
                return "The dump setting looks correct, but no dump exists yet: either no BSOD has occurred, or writing the dump failed (common when the page file is too small).";
            return "Unable to identify the current dump setting.";
        }

        public static void MemDump(Session s)
        {
            string dir = s.Dir("32_Memory_Dump");
            s.Source(dir, @"C:\Windows\MEMORY.DMP", "copied as-is");
            string src = @"C:\Windows\MEMORY.DMP";
            if (!File.Exists(src))
            {
                WriteNothing(dir, "No MEMORY.DMP found on this system.\r\n\r\n" + DumpPolicy());
                s.Log("  · No MEMORY.DMP (" + DumpModeName() + "）");
                throw new SkippedException("MEMORY.DMP does not exist: " + DumpModeName()
                    + "; see 32_Memory_Dump\\README.txt for details");
            }
            FileInfo fi = new FileInfo(src);
            if (fi.Length > 12L * 1024 * 1024 * 1024)
            {
                WriteNothing(dir, "MEMORY.DMP exceeds 12 GB; skipped to avoid filling the disk. Copy manually if needed: " + src);
                throw new SkippedException("MEMORY.DMP exceeds 12 GB; skipped (copy manually)");
            }
            try
            {
                File.Copy(src, Path.Combine(dir, "MEMORY.DMP"), true);
                s.Log("  · Copied MEMORY.DMP, " + Tool.FmtSize(fi.Length));
            }
            catch (Exception ex) { throw new Exception("Failed to copy MEMORY.DMP: " + ex.Message); }
        }

        /// <summary>Full memory dump of a process (GB sized) for leaks, hangs and freezes.</summary>
        public static void ProcessDump(Session s)
        {
            string dir = s.Dir("33_Process_Dump");
            s.Source(dir, @"live process memory (MiniDumpWriteDump)", "dumped from the running process");
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
                if (target == null) throw new SkippedException("No process available to dump");
                s.Log("  · No process name given; using the largest one: " + target.ProcessName + " (" + target.Id + ")");
            }
            else
            {
                Process[] arr = Process.GetProcessesByName(name);
                if (arr.Length == 0) throw new SkippedException("Process not found: " + name + " (check the name, e.g. chrome)");
                target = arr[0];
                if (arr.Length > 1) s.Log("  · Found " + arr.Length + " processes with that name; dumping the first (PID " + target.Id + "）");
            }

            long ws = 0;
            try { ws = target.WorkingSet64; } catch { }
            if (ws > 12L * 1024 * 1024 * 1024)
                throw new SkippedException("Target process uses " + Tool.FmtSize(ws) + ", above the 12 GB limit; skipped");
            if (ws > 1L * 1024 * 1024 * 1024)
                s.Log("  · Target process uses " + Tool.FmtSize(ws) + "; dumping may take tens of seconds");

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
                throw new Exception("Failed to create process dump: " + ex.Message + " (usually needs admin rights, or the process is protected)");
            }
            if (!ok)
            {
                try { File.Delete(file); } catch { }
                throw new Exception("Failed to write process dump, error " + err + " (protected process or insufficient rights; run as administrator)");
            }
            long size = 0;
            try { size = new FileInfo(file).Length; } catch { }
            if (size == 0) throw new Exception("Dump file is empty; retry as administrator");
            s.Log("  · Created " + Path.GetFileName(file) + "，" + Tool.FmtSize(size));
        }

        [System.Runtime.InteropServices.DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr hProcess, uint processId, IntPtr hFile,
            uint dumpType, IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);

        public static void Wer(Session s)
        {
            string dir = s.Dir("31_WER_Reports");
            string root = @"C:\ProgramData\Microsoft\Windows\WER";
            s.Source(dir, root, "full folder mirror, structure and empty folders preserved (needs admin)");
            if (!Directory.Exists(root)) throw new SkippedException("WER folder not found: " + root);

            int dirs, files, failed;
            long bytes;
            MirrorFolder(s, root, Path.Combine(dir, "WER"), 3L * 1024 * 1024 * 1024, out dirs, out files, out bytes, out failed);

            if (files == 0)
            {
                if (!s.IsAdmin)
                {
                    WriteNothing(dir, "Could not read " + root + ". This folder usually requires administrator rights.");
                    throw new SkippedException("Administrator rights are required to read " + root);
                }
                WriteNothing(dir, "The WER folder contains no files.");
                throw new SkippedException("The WER folder contains no files");
            }
            s.Log("  · " + files + " file(s) in " + dirs + " folder(s), " + Tool.FmtSize(bytes)
                  + (failed > 0 ? ", " + failed + " failed" : ""));
        }

        /// <summary>LiveKernelReports（无蓝屏的 GPU 挂起/超时等）整目录镜像。</summary>
        public static void LiveKernel(Session s)
        {
            string dir = s.Dir("34_LiveKernel_Reports");
            string root = @"C:\Windows\LiveKernelReports";
            s.Source(dir, root, "full folder mirror, structure and empty folders preserved (needs admin)");
            if (!Directory.Exists(root)) throw new SkippedException("Live kernel report folder not found: " + root);

            int dirs, files, failed;
            long bytes;
            MirrorFolder(s, root, Path.Combine(dir, "LiveKernelReports"), 4L * 1024 * 1024 * 1024, out dirs, out files, out bytes, out failed);

            if (files == 0)
                throw new SkippedException("No live kernel report could be copied (empty, or administrator rights required)");
            s.Log("  · " + files + " file(s) in " + dirs + " folder(s), " + Tool.FmtSize(bytes)
                  + (failed > 0 ? ", " + failed + " failed" : ""));
        }

        /// <summary>整目录镜像：保留子目录（含空目录），限制总体积，失败/跳过项写入 Skipped_Files.txt。</summary>
        private static void MirrorFolder(Session s, string source, string dest, long capBytes,
                                         out int dirs, out int files, out long bytes, out int failed)
        {
            dirs = 0; files = 0; bytes = 0; failed = 0;
            Directory.CreateDirectory(dest);
            StringBuilder skipped = new StringBuilder();
            long free = 0;
            try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dest))).AvailableFreeSpace; } catch { }

            List<string> subDirs = new List<string>();
            try { subDirs.AddRange(Directory.GetDirectories(source, "*", SearchOption.AllDirectories)); }
            catch (Exception ex) { s.Log("  · Directory enumeration incomplete: " + ex.Message); }
            subDirs.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string d in subDirs)
            {
                if (s.Token.IsCancellationRequested) return;
                string rel = d.Substring(source.Length).TrimStart('\\');
                try { Directory.CreateDirectory(Path.Combine(dest, rel)); dirs++; }
                catch { failed++; }
            }

            List<string> all = new List<string>();
            try { all.AddRange(Directory.GetFiles(source, "*", SearchOption.AllDirectories)); }
            catch (Exception ex) { s.Log("  · File enumeration incomplete: " + ex.Message); }
            all.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string f in all)
            {
                if (s.Token.IsCancellationRequested) return;
                if (bytes > capBytes)
                {
                    skipped.AppendLine(f + "  (size limit reached)");
                    failed++;
                    continue;
                }
                long len = 0;
                try { len = new FileInfo(f).Length; } catch { }
                if (free > 0 && len + 512L * 1024 * 1024 > free)
                {
                    skipped.AppendLine(f + "  (not enough free space)");
                    failed++;
                    continue;
                }
                string rel = f.Substring(source.Length).TrimStart('\\');
                try
                {
                    string target = Path.Combine(dest, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(f, target, true);
                    bytes += len;
                    free -= len;
                    files++;
                }
                catch (Exception ex)
                {
                    failed++;
                    skipped.AppendLine(f + "  (" + ex.Message.Replace("\r", " ").Replace("\n", " ") + ")");
                }
            }
            if (skipped.Length > 0)
            {
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(dest), "Skipped_Files.txt"),
                                      skipped.ToString(), new UTF8Encoding(true));
                }
                catch { }
            }
        }

        // ---------------- power ----------------
        public static void Battery(Session s)
        {
            string dir = s.Dir("40_Battery");
            s.Source(dir, @"powercfg /batteryreport", "generated on this machine");
            string o;
            string f = Path.Combine(dir, "battery_report.html");
            int rc = Tool.Run("powercfg.exe", "/batteryreport /output " + Tool.Q(f) + " /duration 14",
                              120000, s.Token, out o, Encoding.Default);
            WaitFile(f, 20000, s.Token);
            if (!File.Exists(f)) throw new Exception("Failed to generate the battery report: " + Trim(o, 200));
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
            string dir = s.Dir("41_Power_Settings");
            s.Source(dir, @"powercfg /qh, /a, /getactivescheme, /requests, /lastwake, /waketimers", "queried from the running system");
            StringBuilder sb = new StringBuilder();
            string[][] cmds = new string[][] {
                new string[] { "Available sleep states", "/a" },
                new string[] { "Active power scheme", "/getactivescheme" },
                new string[] { "Sleep blockers", "/requests" },
                new string[] { "Last wake source", "/lastwake" },
                new string[] { "Wake timers", "/waketimers" },
                new string[] { "Wake-capable devices", "/devicequery wake_armed" }
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
            File.WriteAllText(Path.Combine(dir, "power_wake.txt"), sb.ToString(), new UTF8Encoding(true));
            CollectPowerSettings(s, dir);
        }

        /// <summary>Power settings to collect: key, label, subgroup, setting, value kind.</summary>
        public static readonly string[][] PowerItems = new string[][] {
            new string[] { "sleep",     "Sleep timeout (idle before sleep)", "SUB_SLEEP",   "STANDBYIDLE",   "time" },
            new string[] { "hibernate", "Hibernate timeout (idle before hibernate)", "SUB_SLEEP",   "HIBERNATEIDLE", "time" },
            new string[] { "monitor",   "Turn off display",                 "SUB_VIDEO",   "VIDEOIDLE",     "time" },
            new string[] { "disk",      "Turn off hard disk",                   "SUB_DISK",    "DISKIDLE",      "time" },
            new string[] { "unattend",  "Unattended sleep",           "SUB_SLEEP",   "UNATTENDSLEEP", "time" },
            new string[] { "lid",       "When lid closes",                 "SUB_BUTTONS", "LIDACTION",     "action" },
            new string[] { "powerbtn",  "Power button action",               "SUB_BUTTONS", "PBUTTONACTION", "action" },
            new string[] { "hybrid",    "Hybrid sleep",                   "SUB_SLEEP",   "HYBRIDSLEEP",   "bool" }
        };

        private static void CollectPowerSettings(Session s, string dir)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Power and sleep timeouts ===");
            sb.AppendLine("(AC = plugged in, DC = on battery; values are currently in effect)");
            sb.AppendLine();
            sb.AppendLine(Pad("Item", 44) + Pad("AC", 22) + Pad("DC", 22) + "Raw");
            sb.AppendLine(new string('-', 92));

            StringBuilder csv = new StringBuilder();
            csv.AppendLine("Item,AC,DC,AC_raw,DC_raw");

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

                sb.AppendLine(Pad(name, 44) + Pad(sa, 22) + Pad(sd, 22) + ac + "/" + dc);
                csv.AppendLine(name + "," + sa + "," + sd + "," + ac + "," + dc);
            }

            sb.AppendLine();
            sb.AppendLine("Raw values: timeouts are in seconds; 0 or 4294967295 means never;");
            sb.AppendLine("Actions: 0=do nothing, 1=sleep, 2=hibernate, 3=shut down, 4=display off; boolean: 0=off, 1=on.");

            File.WriteAllText(Path.Combine(dir, "power_settings.txt"), sb.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(dir, "power_settings.csv"), csv.ToString(), new UTF8Encoding(true));

            StringBuilder sum = new StringBuilder();
            sum.AppendLine("=== Power and sleep timeouts (active scheme) ===");
            foreach (string[] item in PowerItems)
            {
                string k = "PWR." + item[0] + "_ac";
                if (!s.Probe.ContainsKey(k)) continue;
                sum.AppendLine("  " + item[1] + ": AC " + s.Probe[k] + " / DC " + s.Probe["PWR." + item[0] + "_dc"]);
            }
            s.Log(sum.ToString().TrimEnd());
        }

        /// <summary>Take the last two hex values from powercfg output (AC, DC).</summary>
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

        /// <summary>Pad by display width (CJK counts as 2 columns) so txt tables line up.</summary>
        private static string Pad(string text, int width)
        {
            int w = 0;
            foreach (char c in text) w += (c >= 0x2E80) ? 2 : 1;
            if (w >= width) return text + "  ";
            return text + new string(' ', width - w);
        }

        private static string FormatValue(long v, string kind)
        {
            if (kind == "action")
            {
                switch ((int)v)
                {
                    case 0: return "Do nothing";
                    case 1: return "Sleep";
                    case 2: return "Hibernate";
                    case 3: return "Shut down";
                    case 4: return "Turn off display";
                    default: return "Unknown (" + v + ")";
                }
            }
            if (kind == "bool") return v == 0 ? "Off" : "On";
            if (v == 0 || v == 4294967295L) return "Never";
            if (v < 60) return v + " s";
            if (v < 3600)
            {
                long m = v / 60, r = v % 60;
                return r == 0 ? m + " min" : m + " min " + r + " s";
            }
            long h = v / 3600, mm = (v % 3600) / 60;
            return mm == 0 ? h + " h" : h + " h " + mm + " min";
        }

        public static void Energy(Session s)
        {
            string dir = s.Dir("42_Energy_Report");
            s.Source(dir, @"powercfg /energy /duration 60", "generated on this machine");
            string f = Path.Combine(dir, "energy_report.html");
            string o;
            int rc = Tool.Run("powercfg.exe", "/energy /output " + Tool.Q(f) + " /duration 60",
                              180000, s.Token, out o, Encoding.Default);
            WaitFile(f, 30000, s.Token);
            if (!File.Exists(f)) throw new Exception("Energy report failed (requires admin): " + Trim(o, 200));
        }

        public static void SleepStudy(Session s)
        {
            string dir = s.Dir("43_Sleep_Study");
            s.Source(dir, @"powercfg /sleepstudy  +  powercfg /spr  ->  C:\Windows\System32\sleepstudy-report.html",
                     "reports generated on this machine (needs administrator)");
            string f = Path.Combine(dir, "sleep_study.html");
            string o;
            Tool.Run("powercfg.exe", "/sleepstudy /output " + Tool.Q(f), 180000, s.Token, out o, Encoding.Default);
            WaitFile(f, 30000, s.Token);

            // powercfg /spr 生成睡眠研究报告（默认落在 System32 下），需要管理员
            string sprSrc = @"C:\Windows\System32\sleepstudy-report.html";
            try
            {
                Tool.Run("powercfg.exe", "/spr", 300000, s.Token, out o, Encoding.Default);
                WaitFile(sprSrc, 60000, s.Token);
                if (File.Exists(sprSrc))
                {
                    File.Copy(sprSrc, Path.Combine(dir, "sleepstudy-report.html"), true);
                    s.Log("  · sleep study report from powercfg /spr copied");
                }
            }
            catch { }

            bool ok1 = File.Exists(f);
            bool ok2 = File.Exists(Path.Combine(dir, "sleepstudy-report.html"));
            if (!ok1 && !ok2)
                throw new Exception("Sleep study failed (requires admin and Modern Standby support): " + Trim(o, 200));
            if (!ok2) s.Log("  · powercfg /spr produced no report (Sleep Study may be unsupported here)");
        }

        // ---------------- performance ----------------
        public static void Perf(Session s)
        {
            string dir = s.Dir("50_Performance");
            s.Source(dir, @"performance counters (Win32_PerfFormattedData)", "sampled for 20 seconds");
            string ps = @"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
Write-Output 'Time,CPU%,FreeMemMB,CommittedMB,DiskBusy%,CPUMHz,Battery%'
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
            if (outp.Trim().Length == 0) throw new Exception("Performance sampling returned no data");
            File.WriteAllText(Path.Combine(dir, "perf_sample.csv"), outp, new UTF8Encoding(true));
        }

        public static void Gpu(Session s)
        {
            string dir = s.Dir("60_Display_GPU");
            s.Source(dir, @"dxdiag /t", "generated on this machine");
            string f = Path.Combine(dir, "dxdiag.txt");
            string o;
            Tool.Run("dxdiag.exe", "/t " + Tool.Q(f), 120000, s.Token, out o, Encoding.Default);
            WaitFile(f, 60000, s.Token);
            if (!File.Exists(f)) WriteNothing(dir, "dxdiag produced no report (possibly blocked by security software).");
        }

        // ---------------- network ----------------
        public static void NetCfg(Session s)
        {
            string dir = s.Dir("70_Network");
            s.Source(dir, @"ipconfig /all, route print, arp -a, netsh wlan / int tcp", "queried from the running system");
            StringBuilder sb = new StringBuilder();
            string[][] cmds = new string[][] {
                new string[] { "IP configuration", "ipconfig /all" },
                new string[] { "Routing table", "route print" },
                new string[] { "ARP table", "arp -a" },
                new string[] { "Wi-Fi interfaces", "netsh wlan show interfaces" },
                new string[] { "Wi-Fi driver capabilities", "netsh wlan show drivers" },
                new string[] { "TCP global parameters", "netsh int tcp show global" },
                new string[] { "Proxy settings", "netsh winhttp show proxy" }
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
            File.WriteAllText(Path.Combine(dir, "network_config.txt"), sb.ToString(), new UTF8Encoding(true));
        }

        public static void WlanReport(Session s)
        {
            string dir = s.Dir("71_WLAN_Report");
            s.Source(dir, @"netsh wlan show wlanreport -> C:\ProgramData\Microsoft\Windows\WlanReport", "generated on this machine");
            string o;
            Tool.Run("netsh.exe", "wlan show wlanreport", 120000, s.Token, out o, Encoding.Default);
            string src = @"C:\ProgramData\Microsoft\Windows\WlanReport\wlan-report-latest.html";
            WaitFile(src, 30000, s.Token);
            if (File.Exists(src))
            {
                File.Copy(src, Path.Combine(dir, "wlan_history.html"), true);
                string side = @"C:\ProgramData\Microsoft\Windows\WlanReport\wlan-report-latest.xml";
                if (File.Exists(side))
                {
                    try { File.Copy(side, Path.Combine(dir, "wlan_history.xml"), true); } catch { }
                }
            }
            else
            {
                WriteNothing(dir, "Wi-Fi history report failed (no wireless adapter, or admin required).");
            }
        }

        // ---------------- storage ----------------
        public static void Disk(Session s)
        {
            string dir = s.Dir("80_Storage");
            s.Source(dir, @"Get-PhysicalDisk / Get-Volume / Get-StorageReliabilityCounter", "queried from the running system");
            string ps = @"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
'=== Physical disks ==='
Get-PhysicalDisk | Select-Object DeviceId,FriendlyName,MediaType,BusType,HealthStatus,OperationalStatus,@{n='SizeGB';e={[math]::Round($_.Size/1GB,0)}} | Format-Table -AutoSize | Out-String -Width 200
'=== Reliability counters (SMART) ==='
Get-PhysicalDisk | ForEach-Object {
  $d = $_
  $r = $null
  try { $r = $d | Get-StorageReliabilityCounter } catch {}
  if($r){
    'Disk: ' + $d.FriendlyName
    '  Temp(C): ' + $r.Temperature + '   PowerOnHours: ' + $r.PowerOnHours + '   Wear: ' + $r.Wear + '   ReadErrors: ' + $r.ReadErrorsTotal + '   WriteErrors: ' + $r.WriteErrorsTotal
  }
}
'=== Volumes ==='
Get-Volume | Select-Object DriveLetter,FileSystemLabel,FileSystem,HealthStatus,@{n='SizeGB';e={[math]::Round($_.Size/1GB,0)}},@{n='FreeGB';e={[math]::Round($_.SizeRemaining/1GB,0)}} | Format-Table -AutoSize | Out-String -Width 200
'=== Disks ==='
Get-Disk | Select-Object Number,FriendlyName,PartitionStyle,@{n='SizeGB';e={[math]::Round($_.Size/1GB,0)}},HealthStatus,OperationalStatus | Format-Table -AutoSize | Out-String -Width 200
";
            string outp = Tool.Ps(ps, 120000, s.Token);
            File.WriteAllText(Path.Combine(dir, "disk_health.txt"), outp, new UTF8Encoding(true));
        }

        // ---------------- extra ----------------
        public static void AppDir(Session s)
        {
            if (string.IsNullOrEmpty(s.AppDir) || !Directory.Exists(s.AppDir))
                throw new Exception("No valid extra log folder specified");
            string dir = s.Dir("90_App_Logs");
            s.Source(dir, s.AppDir, "copied from the folder entered in the UI");
            long bytes = CopyTree(s.AppDir, Path.Combine(dir, new DirectoryInfo(s.AppDir).Name), 1500L * 1024 * 1024);
            if (bytes == 0) WriteNothing(dir, "Target folder is empty or unreadable: " + s.AppDir);
        }

        // ---------------- helpers ----------------
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
            try { File.WriteAllText(Path.Combine(dir, "README.txt"), text, new UTF8Encoding(true)); }
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
