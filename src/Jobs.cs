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

            list.Add(new JobDef { Id = "events-system", Category = "Basics", Name = "System event log", Hint = "BSOD, unexpected shutdown, driver and hardware errors (~15 s)", DefaultOn = true, Run = Jobs.SystemEvents });
            list.Add(new JobDef { Id = "events-app", Category = "Basics", Name = "Application event log", Hint = "App crashes and hangs (~10 s)", DefaultOn = true, Run = Jobs.AppEvents });
            list.Add(new JobDef { Id = "evtx-all", Category = "Basics", Name = "All event log files (.evtx)", Hint = "Mirrors winevt\\Logs incl. empty folders (~30 s as admin); ~2 min without", DefaultOn = true, Run = Jobs.AllEventLogs });
            list.Add(new JobDef { Id = "sysinfo", Category = "Basics", Name = "System info (systeminfo)", Hint = "Full systeminfo report (~10 s)", DefaultOn = true, Run = Jobs.SysInfo });
            list.Add(new JobDef { Id = "drivers", Category = "Basics", Name = "Driver store and problem devices", Hint = "Driver store listing + devices with a yellow mark (~10 s)", DefaultOn = true, Run = Jobs.Drivers });
            list.Add(new JobDef { Id = "snapshot", Category = "Basics", Name = "Running processes", Hint = "Every process running at collection time (~5 s)", DefaultOn = true, Run = Jobs.Snapshot });
            list.Add(new JobDef { Id = "software", Category = "Basics", Name = "Installed updates and software", Hint = "Track environment changes (~5 s)", DefaultOn = true, Run = Jobs.Software });
            list.Add(new JobDef { Id = "msinfo32", Category = "Basics", Name = "msinfo32 full report (slow)", Hint = "More detail, but may hang on some machines (~45 s)", DefaultOn = true, Run = Jobs.Msinfo32 });

            list.Add(new JobDef { Id = "minidump", Category = "Crashes and dumps", Name = "Blue screen dumps (Minidump)", Hint = "Essential for BSOD (~3 s)", DefaultOn = true, Run = Jobs.MiniDump });
            list.Add(new JobDef { Id = "wer", Category = "Crashes and dumps", Name = "WER crash reports (full folder)", Hint = "Mirrors C:\\ProgramData\\Microsoft\\Windows\\WER; needs admin", DefaultOn = true, NeedAdmin = true, Run = Jobs.Wer });
            list.Add(new JobDef { Id = "livekernel", Category = "Crashes and dumps", Name = "Live kernel reports", Hint = "Mirrors C:\\Windows\\LiveKernelReports; needs admin", DefaultOn = true, NeedAdmin = true, Run = Jobs.LiveKernel });
            list.Add(new JobDef { Id = "memdump", Category = "Crashes and dumps", Name = "Full memory dump (MEMORY.DMP)", Hint = "Any size; only if the system already created it", DefaultOn = true, Run = Jobs.MemDump });

            list.Add(new JobDef { Id = "panther", Category = "Setup and component logs", Name = "Windows setup logs (Panther)", Hint = "Mirrors C:\\Windows\\Panther; can be ~2 GB; needs admin", DefaultOn = true, NeedAdmin = true, Run = Jobs.PantherLogs });
            list.Add(new JobDef { Id = "winlogs", Category = "Setup and component logs", Name = "Windows component logs", Hint = "Mirrors C:\\Windows\\Logs (CBS, DISM, WindowsUpdate, WinRE)", DefaultOn = true, Run = Jobs.WindowsLogs });
            list.Add(new JobDef { Id = "inf", Category = "Setup and component logs", Name = "Driver INF and setup logs", Hint = "Mirrors C:\\Windows\\INF (setupapi.dev.log and friends)", DefaultOn = true, Run = Jobs.InfFolder });

            list.Add(new JobDef { Id = "battery", Category = "Power and battery", Name = "Battery health report", Hint = "Capacity, cycles, health (~5 s)", DefaultOn = true, Run = Jobs.Battery });
            list.Add(new JobDef { Id = "powercfg", Category = "Power and battery", Name = "Power settings", Hint = "Sleep and display timeouts, lid action (~5 s)", DefaultOn = true, Run = Jobs.PowerCfg });
            list.Add(new JobDef { Id = "sleepstudy", Category = "Power and battery", Name = "Sleep study + system power report", Hint = "powercfg /sleepstudy and /spr; requires admin (~60 s)", DefaultOn = true, NeedAdmin = true, Run = Jobs.SleepStudy });

            list.Add(new JobDef { Id = "gpu", Category = "Performance", Name = "DXDiag (graphics and display)", Hint = "Includes TDR records (~20 s)", DefaultOn = true, Run = Jobs.Gpu });

            list.Add(new JobDef { Id = "netcfg", Category = "Network", Name = "Network configuration snapshot", Hint = "IP, routes, Wi-Fi, Bluetooth (~5 s)", DefaultOn = true, Run = Jobs.NetCfg });

            list.Add(new JobDef { Id = "disk", Category = "Storage", Name = "Disk and SMART health", Hint = "Health, temperature, power-on hours (~5 s)", DefaultOn = true, Run = Jobs.Disk });


            return list;
        }
    }

    public static class Jobs
    {
        // ---------------- basics ----------------
        public static void SystemEvents(Session s)
        {
            string dir = s.Dir("1_System_Logs");
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
            ParseEvents(outp, "System", s);

            ExportLogCsv(s, "System", dir, "System_events.csv", 30, null);
            ExportLogCsv(s, "System", dir, "System_errors.csv", 30, "1,2,3");
        }

        /// <summary>
        /// 用同一份数据生成带颜色的 HTML 表格（CSV 本身存不了颜色）：
        /// 1001 / 41 / 6008 标红，506 / 507 标黄。
        /// </summary>
        private static void WriteEventHtml(string csvPath, string htmlPath, string logName)
        {
            string[] lines;
            try { lines = File.ReadAllLines(csvPath, Encoding.UTF8); }
            catch { return; }
            if (lines.Length < 2) return;

            List<string> head = SplitCsvLine(lines[0]);
            int markCol = 0, msgCol = head.Count - 1, timeCol = -1;
            for (int i = 0; i < head.Count; i++)
            {
                if (head[i] == "TimeCreated") timeCol = i;
                if (head[i] == "Message") msgCol = i;
            }
            if (timeCol < 0) timeCol = 1;

            StringBuilder b = new StringBuilder();
            b.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>")
             .Append(Html(logName)).Append(" events</title><style>");
            b.Append("body{margin:0;background:#f4f6fa;font:13px/1.5 'Microsoft YaHei','Segoe UI',sans-serif;color:#1f2937}");
            b.Append("h1{margin:16px 18px 6px;font-size:18px;color:#1f3864}");
            b.Append(".lg{margin:0 18px 12px;font-size:12px;color:#4b5563}");
            b.Append(".lg b{display:inline-block;padding:1px 8px;border-radius:4px;margin-right:6px}");
            b.Append("table{border-collapse:collapse;width:calc(100% - 36px);margin:0 18px 40px;background:#fff}");
            b.Append("th{position:sticky;top:0;background:#1f3864;color:#fff;font-size:12px;text-align:left;padding:7px 8px}");
            b.Append("td{border-bottom:1px solid #eef1f6;padding:5px 8px;vertical-align:top;font-size:12px}");
            b.Append("td.m{white-space:nowrap;color:#6b7280;width:150px}");
            b.Append("td.n{width:70px;text-align:right;color:#6b7280}");
            b.Append(".red{background:#fee2e2}.yellow{background:#fef3c7}");
            b.Append("</style></head><body>");
            b.Append("<h1>").Append(Html(logName)).Append(" events (").Append(lines.Length - 1).Append(")</h1>");
            b.Append("<div class=\"lg\">");
            b.Append("<b class=\"red\">RED</b>1001 / 41 / 6008 &nbsp; ");
            b.Append("<b class=\"yellow\">YELLOW</b>506 (enter Modern Standby) / 507 (leave Modern Standby)");
            b.Append("</div>");

            b.Append("<table><thead><tr><th style=\"width:150px\">Time</th><th style=\"width:70px\">Event ID</th><th>Message</th></tr></thead><tbody>");
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0) continue;
                List<string> f = SplitCsvLine(line);
                string mark = f.Count > markCol ? f[markCol] : "";
                string time = f.Count > timeCol ? f[timeCol] : "";
                string msg = f.Count > msgCol ? f[msgCol] : "";
                string id = "";
                for (int c = 0; c < head.Count && c < f.Count; c++) if (head[c] == "EventID") { id = f[c]; break; }
                if (msg.Length > 400) msg = msg.Substring(0, 400) + "…";

                string cls = mark == "RED" ? " class=\"red\"" : (mark == "YELLOW" ? " class=\"yellow\"" : "");
                b.Append("<tr").Append(cls).Append("><td class=\"m\">").Append(Html(time))
                 .Append("</td><td class=\"n\">").Append(Html(id))
                 .Append("</td><td>").Append(Html(msg)).Append("</td></tr>\n");
            }
            b.Append("</tbody></table></body></html>");

            File.WriteAllText(htmlPath, b.ToString(), new UTF8Encoding(false));
        }

        /// <summary>按 CSV 规则拆一行（支持双引号与转义的 ""）。</summary>
        private static List<string> SplitCsvLine(string line)
        {
            List<string> res = new List<string>();
            StringBuilder sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == '"') quoted = true;
                    else if (c == ',') { res.Add(sb.ToString()); sb.Length = 0; }
                    else sb.Append(c);
                }
            }
            res.Add(sb.ToString());
            return res;
        }

        private static string Html(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        /// <summary>把某个日志通道的全部事件导出成 CSV（完整消息，不截断）。</summary>
        private static void ExportLogCsv(Session s, string logName, string dir, string fileName, int days, string levels)
        {
            string target = Path.Combine(dir, fileName);
            string outPs = target.Replace("'", "''");
            string levelPart = string.IsNullOrEmpty(levels) ? "" : ("; Level=" + levels);
            string script = @"
$out = '" + outPs + @"'
$start = (Get-Date).AddDays(-" + days + @")
$ev = $null
try { $ev = Get-WinEvent -FilterHashtable @{LogName='" + logName + @"'; StartTime=$start" + levelPart + @"} -ErrorAction SilentlyContinue } catch { }
if($ev){
  $ev | Sort-Object TimeCreated |
    Select-Object @{n='Mark';e={
        if ($_.Id -eq 1001 -or $_.Id -eq 41 -or $_.Id -eq 6008) { 'RED' }
        elseif ($_.Id -eq 506 -or $_.Id -eq 507) { 'YELLOW' }
        else { '' }
      }},
                  @{n='TimeCreated';e={$_.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss')}},
                  @{n='LogName';e={$_.LogName}},
                  @{n='EventID';e={$_.Id}},
                  @{n='Level';e={$_.LevelDisplayName}},
                  @{n='Provider';e={$_.ProviderName}},
                  @{n='Task';e={$_.TaskDisplayName}},
                  @{n='RecordId';e={$_.RecordId}},
                  @{n='User';e={$_.UserId}},
                  @{n='Computer';e={$_.MachineName}},
                  @{n='Data';e={ (@($_.Properties) | ForEach-Object { ([string]$_.Value) -replace ""[`r`n]+"", ' ' }) -join ' | ' }},
                  @{n='Message';e={ ([string]$_.Message) -replace ""[`r`n]+"",' ' }} |
    Export-Csv -Path $out -NoTypeInformation -Encoding UTF8
  Write-Output ('rows=' + @($ev).Count)
} else {
  Write-Output 'rows=0'
}
";
            string outp = Tool.Ps(script, 600000, s.Token);
            string rows = "";
            foreach (string line in (outp ?? "").Split('\n'))
            {
                string l = line.Trim();
                if (l.StartsWith("rows=")) rows = l.Substring(5);
            }
            if (File.Exists(target))
            {
                long size = new FileInfo(target).Length;
                try { WriteEventHtml(target, Path.ChangeExtension(target, ".html"), logName); } catch { }
                s.Log("  · " + fileName + ": " + rows + " event(s), " + Tool.FmtSize(size));
                if (size < 100) File.Delete(target);
            }
            else
            {
                s.Log("  · " + fileName + ": no events in the last " + days + " days");
            }
        }

        /// <summary>把 winevt\Logs 下所有 .evtx 连目录结构（含空目录）整体镜像出来。</summary>
        public static void AllEventLogs(Session s)
        {
            string dir = s.Dir("3_All_Event_Logs");
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
            string dir = s.Dir("2_Application_Logs");
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
            ParseEvents(outp, "Application", s);

            ExportLogCsv(s, "Application", dir, "Application_events.csv", 30, null);
            ExportLogCsv(s, "Application", dir, "Application_errors.csv", 30, "1,2,3");
        }

        public static void SysInfo(Session s)
        {
            string dir = s.Dir("4_System_Info");
            s.Source(dir, @"WMI/CIM + systeminfo + powercfg report parsers", "queried from the running system");
            string o;
            string sys = Path.Combine(dir, "systeminfo.txt");
            Tool.RunAuto("cmd.exe", "/c systeminfo > " + Tool.Q(sys), 60000, s.Token, out o);
            Tool.NormalizeTextFile(sys);

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
            ParseProbe(probeOut, s);
        }

        public static void Msinfo32(Session s)
        {
            string dir = s.Dir("8_msinfo32");
            s.Source(dir, @"msinfo32.exe /report", "generated on this machine");
            string f = Path.Combine(dir, "msinfo32.txt");
            string o;
            Tool.Run("msinfo32.exe", "/report " + Tool.Q(f), 45000, s.Token, out o, Encoding.Default);
            WaitFile(f, 40000, s.Token);
            Tool.NormalizeTextFile(f);
            if (!File.Exists(f))
                throw new Exception("msinfo32 did not produce a file in time (it hangs on some machines); use another item instead");
        }

        public static void Drivers(Session s)
        {
            string dir = s.Dir("5_Drivers_Devices");
            s.Source(dir, @"pnputil /enum-drivers   +   Win32_PnPEntity problem devices", "queried from the running system");
            string o;
            Tool.RunAuto("cmd.exe", "/c pnputil /enum-drivers > " + Tool.Q(Path.Combine(dir, "driver_store.txt")),
                     120000, s.Token, out o);
            Tool.NormalizeTextFile(Path.Combine(dir, "driver_store.txt"));

            string ps = @"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$dev = @(Get-CimInstance Win32_PnPEntity | Where-Object { $_.ConfigManagerErrorCode -ne 0 -and $_.ConfigManagerErrorCode -ne 22 })
if ($dev.Count -eq 0) {
  Write-Output 'No device has a problem code (no yellow mark in Device Manager).'
} else {
  $dev | Select-Object @{n='ErrorCode';e={$_.ConfigManagerErrorCode}},Status,Class,@{n='Device';e={$_.Name}},DeviceID |
    Sort-Object ErrorCode | Format-Table -AutoSize | Out-String -Width 400
}
";
            string outp = Tool.Ps(ps, 90000, s.Token);
            File.WriteAllText(Path.Combine(dir, "problem_devices.txt"), outp, new UTF8Encoding(true));
        }

        public static void Snapshot(Session s)
        {
            string dir = s.Dir("6_Runtime_Snapshot");
            s.Source(dir, @"Get-Process (all running processes)", "queried from the running system");
            string ps = @"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
'=== Running processes ==='
Get-Process | Sort-Object WS -Descending | Select-Object Name,Id,@{n='CPU_s';e={[math]::Round($_.CPU,1)}},@{n='MemMB';e={[math]::Round($_.WS/1MB,0)}},Handles,@{n='Threads';e={$_.Threads.Count}} | Format-Table -AutoSize | Out-String -Width 300
";
            string outp = Tool.Ps(ps, 120000, s.Token);
            File.WriteAllText(Path.Combine(dir, "running_processes.txt"), outp, new UTF8Encoding(true));
        }

        public static void Software(Session s)
        {
            string dir = s.Dir("7_Installed_Updates_and_Software");
            s.Source(dir, @"Get-HotFix + HKLM uninstall registry keys", "queried from the running system");
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Installed updates ===");
            string ps = @"Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 30 HotFixID,Description,InstalledOn | Format-Table -AutoSize | Out-String -Width 200";
            sb.AppendLine(Tool.Ps(ps, 60000, s.Token));
            sb.AppendLine();
            sb.AppendLine("=== Installed software ===");
            sb.AppendLine(ReadInstalledApps());
            File.WriteAllText(Path.Combine(dir, "installed_updates_and_software.txt"), sb.ToString(), new UTF8Encoding(true));
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
            string dir = s.Dir("9_Mini_Dumps");
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
                    ? "Dump file too large or copy failed; see 9_Mini_Dumps\\skipped_large_files.txt"
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
            string dir = s.Dir("12_Memory_Dump");
            s.Source(dir, @"C:\Windows\MEMORY.DMP", "copied as-is");
            string src = @"C:\Windows\MEMORY.DMP";
            if (!File.Exists(src))
            {
                WriteNothing(dir, "No MEMORY.DMP found on this system.\r\n\r\n" + DumpPolicy());
                s.Log("  · No MEMORY.DMP (" + DumpModeName() + "）");
                throw new SkippedException("MEMORY.DMP does not exist: " + DumpModeName()
                    + "; see 11_Memory_Dump\\README.txt for details");
            }
            FileInfo fi = new FileInfo(src);
            s.Log("  · Copying MEMORY.DMP, " + Tool.FmtSize(fi.Length) + ", this may take several minutes...");

            string target = Path.Combine(dir, "MEMORY.DMP");
            try
            {
                long free = -1;
                try { free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))).AvailableFreeSpace; } catch { }
                if (free > 0 && free < fi.Length)
                    s.Log("  · Warning: target drive has " + Tool.FmtSize(free) + " free, the dump needs " + Tool.FmtSize(fi.Length));

                File.Copy(src, target, true);
                s.Log("  · Copied MEMORY.DMP, " + Tool.FmtSize(fi.Length));
            }
            catch (Exception ex)
            {
                // 复制失败时不要留下半个文件
                try { if (File.Exists(target)) File.Delete(target); } catch { }
                throw new Exception("Failed to copy MEMORY.DMP (" + Tool.FmtSize(fi.Length) + "): " + ex.Message);
            }
        }
        public static void Wer(Session s)
        {
            string dir = s.Dir("10_WER_Reports");
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
            string dir = s.Dir("11_LiveKernel_Reports");
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

﻿        /// <summary>通用整目录镜像任务。</summary>
        private static void MirrorJob(Session s, string folder, string root, string method, long capBytes)
        {
            string dir = s.Dir(folder);
            s.Source(dir, root, method);
            if (!Directory.Exists(root)) throw new SkippedException("Folder not found: " + root);

            int dirs, files, failed;
            long bytes;
            MirrorFolder(s, root, Path.Combine(dir, Path.GetFileName(root)), capBytes,
                         out dirs, out files, out bytes, out failed);

            if (files == 0)
                throw new SkippedException("Nothing could be copied from " + root + " (empty, or administrator rights required)");
            s.Log("  · " + files + " file(s) in " + dirs + " folder(s), " + Tool.FmtSize(bytes)
                  + (failed > 0 ? ", " + failed + " failed" : ""));
        }

        public static void PantherLogs(Session s)
        {
            MirrorJob(s, "13_Setup_Panther", @"C:\Windows\Panther",
                      "full folder mirror, structure and empty folders preserved (needs admin)", 2560L * 1024 * 1024);
        }

        /// <summary>C:\Windows\Logs —— CBS / DISM / WindowsUpdate 等组件日志。</summary>
        public static void WindowsLogs(Session s)
        {
            MirrorJob(s, "14_Windows_Logs", @"C:\Windows\Logs",
                      "full folder mirror, structure and empty folders preserved", 2048L * 1024 * 1024);
        }

        /// <summary>C:\Windows\INF —— 驱动 INF 与 setupapi 设备安装日志。</summary>
        public static void InfFolder(Session s)
        {
            MirrorJob(s, "15_Inf_Drivers", @"C:\Windows\INF",
                      "full folder mirror, structure and empty folders preserved", 1024L * 1024 * 1024);
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
            string dir = s.Dir("16_Battery");
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
            string dir = s.Dir("17_Power_Settings");
            s.Source(dir, @"powercfg /qh, /a, /getactivescheme, /requests, /lastwake, /waketimers", "queried from the running system");
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

            foreach (string[] item in PowerItems)
            {
                if (s.Token.IsCancellationRequested) return;
                string key = item[0], name = item[1], sub = item[2], setting = item[3], kind = item[4];
                string outp;
                Tool.RunAuto("powercfg.exe", "/qh SCHEME_CURRENT " + sub + " " + setting, 30000, s.Token, out outp);
                long ac, dc;
                if (!ParseIndices(outp, out ac, out dc)) continue;

                string sa = FormatValue(ac, kind);
                string sd = FormatValue(dc, kind);
                s.Probe["PWR." + key + "_ac"] = sa;
                s.Probe["PWR." + key + "_dc"] = sd;
                s.Probe["PWR." + key + "_acraw"] = ac.ToString();
                s.Probe["PWR." + key + "_dcraw"] = dc.ToString();

                sb.AppendLine(Pad(name, 44) + Pad(sa, 22) + Pad(sd, 22) + ac + "/" + dc);
            }

            sb.AppendLine();
            sb.AppendLine("Raw values: timeouts are in seconds; 0 or 4294967295 means never;");
            sb.AppendLine("Actions: 0=do nothing, 1=sleep, 2=hibernate, 3=shut down, 4=display off; boolean: 0=off, 1=on.");

            File.WriteAllText(Path.Combine(dir, "power_settings.txt"), sb.ToString(), new UTF8Encoding(true));

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

        public static void SleepStudy(Session s)
        {
            string dir = s.Dir("18_Sleep_Study");
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
        public static void Gpu(Session s)
        {
            string dir = s.Dir("19_DXDiag");
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
            string dir = s.Dir("20_Network");
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
                Tool.RunAuto("cmd.exe", "/c " + c[1], 60000, s.Token, out outp);
                sb.AppendLine("=== " + c[0] + " ===");
                sb.AppendLine(outp);
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(dir, "network_config.txt"), sb.ToString(), new UTF8Encoding(true));
        }

        // ---------------- storage ----------------
        public static void Disk(Session s)
        {
            string dir = s.Dir("21_Storage");
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
