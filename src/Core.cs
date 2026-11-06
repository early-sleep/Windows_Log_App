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

    /// <summary>采集项正常执行，但目标数据不存在或系统不支持，属于“没采到”而不是“采集失败”。</summary>
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
            if (s.Trim().Length == 0) s = "未知机型";
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
            if (EventMax <= EventMin) return "未获取到";
            return EventMin.ToString("yyyy-MM-dd HH:mm") + " 至 " + EventMax.ToString("yyyy-MM-dd HH:mm");
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
                        m.CpuCores = Environment.ProcessorCount + " 线程";
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
            m.OsArch = Environment.Is64BitOperatingSystem ? "64 位" : "32 位";
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
                    ? up.Days + " 天 " + up.Hours + " 小时"
                    : up.Hours + " 小时 " + up.Minutes + " 分";
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

        public static string FetchSerial(CancellationToken token)
        {
            string outp = Ps("(Get-CimInstance Win32_BIOS).SerialNumber", 15000, token);
            return outp.Trim();
        }
    }

    public static class EventCatalog
    {
        private static Dictionary<int, string> _map = Build();

        private static Dictionary<int, string> Build()
        {
            Dictionary<int, string> d = new Dictionary<int, string>();
            d[41] = "系统未正常关机（断电/死机/长按电源）";
            d[1001] = "蓝屏 BugCheck 记录";
            d[6008] = "上一次关机是意外的";
            d[1074] = "有程序或用户发起了关机/重启";
            d[42] = "系统进入睡眠";
            d[107] = "系统从睡眠恢复";
            d[506] = "进入现代待机（S0ix）";
            d[507] = "退出现代待机";
            d[17] = "WHEA：已纠正的硬件错误";
            d[18] = "WHEA：致命硬件错误";
            d[19] = "WHEA：已纠正的硬件错误（内存/PCIe）";
            d[47] = "WHEA：已纠正的硬件错误";
            d[219] = "驱动加载失败";
            d[411] = "设备未能启动";
            d[7000] = "服务启动失败";
            d[7001] = "依赖服务或组启动失败";
            d[7009] = "服务启动超时";
            d[7011] = "服务事务超时";
            d[7031] = "服务意外终止";
            d[7034] = "服务意外终止";
            d[4101] = "显示驱动停止响应并已恢复（TDR）";
            d[37] = "固件限制了处理器性能（降频）";
            d[7] = "磁盘存在坏块";
            d[11] = "磁盘控制器错误";
            d[51] = "分页操作错误";
            d[129] = "存储设备被重置";
            d[55] = "文件系统结构损坏";
            d[98] = "文件系统损坏";
            d[140] = "文件系统损坏";
            d[1000] = "应用崩溃";
            d[1002] = "应用无响应";
            d[1026] = ".NET 未处理异常";
            d[10016] = "DCOM 权限不足";
            d[36] = "时间同步失败";
            return d;
        }

        public static string Describe(int id, string provider, string log)
        {
            if (log == "Application")
            {
                if (id == 1001) return "应用错误报告（WER）";
                if (id == 1000) return "应用崩溃";
                if (id == 1002) return "应用无响应";
                if (id == 1026) return ".NET 未处理异常";
            }
            string s;
            if (_map.TryGetValue(id, out s) && s != null && s.Length > 0) return s;
            if (provider != null && provider.IndexOf("Display", StringComparison.OrdinalIgnoreCase) >= 0)
                return "显示相关事件";
            return "";
        }

        public static string LevelText(string level)
        {
            if (level == null) return "";
            if (level.IndexOf("错误", StringComparison.Ordinal) >= 0 || level.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0) return "错误";
            if (level.IndexOf("警告", StringComparison.Ordinal) >= 0 || level.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0) return "警告";
            if (level.IndexOf("信息", StringComparison.Ordinal) >= 0 || level.IndexOf("Information", StringComparison.OrdinalIgnoreCase) >= 0) return "信息";
            return level;
        }

        public static int[] CriticalIds = new int[] {
            41, 1001, 6008, 1074, 42, 107, 506, 507, 17, 18, 19, 47,
            219, 411, 7000, 7001, 7009, 7011, 7031, 7034,
            4101, 37, 7, 11, 51, 129, 55, 98, 140, 10016, 36
        };
    }
}
