using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace LogCollector
{
    public enum JobStatus { Pending, Running, Success, Failed, Skipped, Cancelled }

    /// <summary>Job ran fine but the data does not exist or the OS does not support it: "nothing to collect", not a failure.</summary>
    public class SkippedException : Exception
    {
        public SkippedException(string message) : base(message) { }
    }

    public class MachineInfo
    {
        public string Manufacturer = "";
        public string Model = "";
        public string Serial = "";
        public string Bios = "";
        public string BiosDate = "";
        public string Board = "";
        public string Cpu = "";
        public string CpuCores = "";
        public double RamGB;
        public string OsName = "";
        public string OsVersion = "";
        public string OsBuild = "";
        public string OsArch = "";
        public string OsInstall = "";
        public string CurrentUser = "";
        public string ComputerName = "";
        public string BootTime = "";
        public string Uptime = "";

        public string Display()
        {
            string s = Model;
            if (Manufacturer.Length > 0 && Model.IndexOf(Manufacturer, StringComparison.OrdinalIgnoreCase) < 0)
                s = Manufacturer + " " + Model;
            if (s.Trim().Length == 0) s = "Unknown model";
            return s;
        }
    }

    public class EventRow
    {
        public DateTime Time;
        public int Id;
        public string Provider = "";
        public string Level = "";
        public string Message = "";
        public string Log = "";

        public string Describe()
        {
            return EventCatalog.Describe(Id, Provider, Log);
        }
    }

    public class Finding
    {
        public string Level;   // bad / warn / info / good
        public string Text;
        public Finding(string level, string text) { Level = level; Text = text; }
    }

    public class JobResult
    {
        public string Id = "";
        public string Category = "";
        public string Name = "";
        public JobStatus Status = JobStatus.Pending;
        public string Message = "";
        public long Bytes;
        public double Seconds;
        public string Path = "";
    }

    public class JobDef
    {
        public string Id;
        public string Category;
        public string Name;
        public string Hint;
        public bool DefaultOn;
        public bool NeedAdmin;
        public Action<Session> Run;
    }

    public class Session
    {
        public string RootDir;
        public string OutDir;
        public bool IsAdmin;
        public bool Verbose;
        public MachineInfo Info = new MachineInfo();
        public List<EventRow> Events = new List<EventRow>();
        public List<Finding> Findings = new List<Finding>();
        public List<JobResult> Results = new List<JobResult>();
        public Dictionary<string, string> Probe = new Dictionary<string, string>();
        public DateTime EventMin = DateTime.MaxValue;
        public DateTime EventMax = DateTime.MinValue;

        public string RangeText()
        {
            if (EventMax <= EventMin) return "Not available";
            return EventMin.ToString("yyyy-MM-dd HH:mm") + " to " + EventMax.ToString("yyyy-MM-dd HH:mm");
        }
        public CancellationToken Token;
        public Action<string> OnLog;
        public Action<int, int, string> OnProgress;
        public string CaseId = "";
        public string Symptom = "";
        public string OperatorName = "";
        public string AppDir = "";
        public string ProcessName = "";

        private int _done;

        public void Log(string msg)
        {
            if (OnLog != null) OnLog(msg);
        }

        public void LogVerbose(string msg)
        {
            if (Verbose && OnLog != null) OnLog(msg);
        }

        public void Step(int total, string name)
        {
            if (OnProgress != null) OnProgress(_done, total, name);
        }

        public void StepDone(int total)
        {
            _done++;
            if (OnProgress != null) OnProgress(_done, total, "");
        }

        public string Dir(string name)
        {
            string p = System.IO.Path.Combine(OutDir, name);
            if (!Directory.Exists(p)) Directory.CreateDirectory(p);
            return p;
        }
    }

    public static class Tool
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        public static bool IsElevated()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static string FmtSize(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.0") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1024.0 / 1024.0).ToString("0.0") + " MB";
            return (b / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
        }

        public static long DirSize(string path)
        {
            long total = 0;
            try
            {
                foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        public static int Run(string exe, string args, int timeoutMs, CancellationToken token,
                              out string output, Encoding enc)
        {
            output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                if (enc != null)
                {
                    psi.StandardOutputEncoding = enc;
                    psi.StandardErrorEncoding = enc;
                }
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    StringBuilder sb = new StringBuilder();
                    object gate = new object();
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (gate) sb.AppendLine(e.Data); }
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (gate) sb.AppendLine(e.Data); }
                    };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    int waited = 0;
                    while (!p.WaitForExit(200))
                    {
                        waited += 200;
                        if (token.IsCancellationRequested)
                        {
                            try { p.Kill(); } catch { }
                            return -1;
                        }
                        if (timeoutMs > 0 && waited > timeoutMs)
                        {
                            try { p.Kill(); } catch { }
                            return -2;
                        }
                    }
                    p.WaitForExit();
                    lock (gate) output = sb.ToString();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -3;
            }
        }

        public static string Ps(string script, int timeoutMs, CancellationToken token)
        {
            string full = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8\r\n"
                        + "$ErrorActionPreference='SilentlyContinue'\r\n"
                        + "$ProgressPreference='SilentlyContinue'\r\n"
                        + script;
            string b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
            string outp;
            Run("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + b64,
                timeoutMs, token, out outp, Encoding.UTF8);
            return outp;
        }

        public static string Q(string s)
        {
            return "\"" + (s == null ? "" : s) + "\"";
        }

        public static void WriteText(string path, string content, Encoding enc)
        {
            if (enc == null) enc = new UTF8Encoding(false);
            File.WriteAllText(path, content, enc);
        }

        public static MachineInfo ReadMachine()
        {
            MachineInfo m = new MachineInfo();
            m.ComputerName = Environment.MachineName;
            m.CurrentUser = Environment.UserName;
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                {
                    if (k != null)
                    {
                        m.Manufacturer = S(k.GetValue("SystemManufacturer"));
                        m.Model = S(k.GetValue("SystemProductName"));
                        m.Bios = S(k.GetValue("BIOSVersion"));
                        m.BiosDate = S(k.GetValue("BIOSReleaseDate"));
                        m.Board = S(k.GetValue("BaseBoardProduct"));
                    }
                }
            }
            catch { }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (k != null)
                    {
                        m.Cpu = S(k.GetValue("ProcessorNameString"));
                        m.CpuCores = Environment.ProcessorCount + " threads";
                    }
                }
            }
            catch { }
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        string pn = S(k.GetValue("ProductName"));
                        string dv = S(k.GetValue("DisplayVersion"));
                        string build = S(k.GetValue("CurrentBuildNumber"));
                        object ubr = k.GetValue("UBR");
                        m.OsName = pn;
                        m.OsVersion = dv;
                        m.OsBuild = build + (ubr == null ? "" : "." + ubr);
                        int bn;
                        if (int.TryParse(build, out bn) && bn >= 22000 && m.OsName.IndexOf("Windows 10") >= 0)
                            m.OsName = m.OsName.Replace("Windows 10", "Windows 11");
                        object inst = k.GetValue("InstallDate");
                        if (inst is int)
                            m.OsInstall = DateTime.FromFileTimeUtc(((long)(int)inst) * 10000000L + 116444736000000000L)
                                .ToLocalTime().ToString("yyyy-MM-dd");
                    }
                }
            }
            catch { }
            m.OsArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
            try
            {
                MEMORYSTATUSEX st = new MEMORYSTATUSEX();
                st.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref st))
                    m.RamGB = Math.Round(st.ullTotalPhys / 1024.0 / 1024.0 / 1024.0, 1);
            }
            catch { }
            try
            {
                TimeSpan up = TimeSpan.FromMilliseconds((uint)Environment.TickCount);
                m.Uptime = up.Days > 0
                    ? up.Days + " d " + up.Hours + " h"
                    : up.Hours + " h " + up.Minutes + " min";
                m.BootTime = DateTime.Now.Subtract(up).ToString("yyyy-MM-dd HH:mm");
            }
            catch { }
            return m;
        }

        private static string S(object o)
        {
            if (o == null) return "";
            return o.ToString().Trim();
        }

        /// <summary>读取 BIOS 序列号；取不到或明显是占位值时返回空串。</summary>
        public static string FetchSerial(CancellationToken token)
        {
            string sn = CleanSerial(Ps("(Get-CimInstance Win32_BIOS).SerialNumber", 20000, token));
            if (IsBadSerial(sn))
            {
                string w = CleanSerial(Ps("(Get-WmiObject Win32_BIOS).SerialNumber", 20000, token));
                if (!IsBadSerial(w)) sn = w;
            }
            if (IsBadSerial(sn)) return "";
            return sn;
        }

        private static string CleanSerial(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", "").Replace("\n", "").Replace("\t", "").Trim();
            return s;
        }

        private static bool IsBadSerial(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            string t = s.Trim().ToLowerInvariant();
            if (t.Length < 3) return true;
            string[] bad = { "default string", "to be filled by o.e.m.", "to be filled by oem",
                             "none", "n/a", "system serial number", "serial number",
                             "unknown", "not specified", "invalid", "0", "oem" };
            foreach (string b in bad) if (t == b) return true;
            return false;
        }
    }

    public static class EventCatalog
    {
        private static Dictionary<int, string> _map = Build();

        private static Dictionary<int, string> Build()
        {
            Dictionary<int, string> d = new Dictionary<int, string>();
            d[41] = "Unexpected shutdown (power loss / hang / power button)";
            d[1001] = "Blue screen (BugCheck) recorded";
            d[6008] = "Previous shutdown was unexpected";
            d[1074] = "Shutdown or restart initiated by a program or user";
            d[42] = "Entered sleep";
            d[107] = "Resumed from sleep";
            d[506] = "Entered Modern Standby (S0ix)";
            d[507] = "Exited Modern Standby";
            d[17] = "WHEA: corrected hardware error";
            d[18] = "WHEA: fatal hardware error";
            d[19] = "WHEA: corrected hardware error (memory/PCIe)";
            d[47] = "WHEA: corrected hardware error";
            d[219] = "Driver failed to load";
            d[411] = "Device failed to start";
            d[7000] = "Service failed to start";
            d[7001] = "Dependent service or group failed to start";
            d[7009] = "Service start timed out";
            d[7011] = "Service transaction timed out";
            d[7031] = "Service terminated unexpectedly";
            d[7034] = "Service terminated unexpectedly";
            d[4101] = "Display driver stopped responding and recovered (TDR)";
            d[37] = "Processor performance limited by firmware (throttling)";
            d[7] = "Disk has bad blocks";
            d[11] = "Disk controller error";
            d[51] = "Paging operation error";
            d[129] = "Storage device was reset";
            d[55] = "File system structure corruption";
            d[98] = "File system corruption";
            d[140] = "File system corruption";
            d[1000] = "Application crash";
            d[1002] = "Application hang";
            d[1026] = ".NET unhandled exception";
            d[10016] = "DCOM permission denied";
            d[36] = "Time synchronization failed";
            return d;
        }

        public static string Describe(int id, string provider, string log)
        {
            if (log == "Application")
            {
                if (id == 1001) return "Application error report (WER)";
                if (id == 1000) return "Application crash";
                if (id == 1002) return "Application hang";
                if (id == 1026) return ".NET unhandled exception";
            }
            string s;
            if (_map.TryGetValue(id, out s) && s != null && s.Length > 0) return s;
            if (provider != null && provider.IndexOf("Display", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Display-related event";
            return "";
        }

        public static string LevelText(string level)
        {
            if (level == null) return "";
            if (level.IndexOf("错误", StringComparison.Ordinal) >= 0 || level.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0) return "Error";
            if (level.IndexOf("警告", StringComparison.Ordinal) >= 0 || level.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0) return "Warning";
            if (level.IndexOf("信息", StringComparison.Ordinal) >= 0 || level.IndexOf("Information", StringComparison.OrdinalIgnoreCase) >= 0) return "Information";
            return level;
        }

        public static int[] CriticalIds = new int[] {
            41, 1001, 6008, 1074, 42, 107, 506, 507, 17, 18, 19, 47,
            219, 411, 7000, 7001, 7009, 7011, 7031, 7034,
            4101, 37, 7, 11, 51, 129, 55, 98, 140, 10016, 36
        };
    }
}
