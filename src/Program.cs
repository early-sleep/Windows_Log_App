using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LogCollector
{
    public static class Runner
    {
        public static Dictionary<string, string> JobDirs = BuildDirs();

        private static Dictionary<string, string> BuildDirs()
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            d["events-system"] = "10_系统日志";
            d["events-app"] = "11_应用程序日志";
            d["sysinfo"] = "20_系统信息";
            d["drivers"] = "21_驱动设备";
            d["snapshot"] = "22_运行状态";
            d["software"] = "23_补丁与软件";
            d["msinfo32"] = "24_msinfo32";
            d["minidump"] = "30_崩溃转储";
            d["wer"] = "31_应用崩溃报告";
            d["memdump"] = "32_内存转储";
            d["pdump"] = "33_进程转储";
            d["battery"] = "40_电源电池";
            d["powercfg"] = "41_电源设置";
            d["energy"] = "42_能耗诊断";
            d["sleepstudy"] = "43_睡眠诊断";
            d["perf"] = "50_性能";
            d["gpu"] = "60_显示显卡";
            d["netcfg"] = "70_网络";
            d["wlan"] = "71_无线报告";
            d["disk"] = "80_存储";
            d["appdir"] = "90_附加日志";
            return d;
        }

        public static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "UNKNOWN";
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            s = s.Replace(' ', '_');
            if (s.Length > 40) s = s.Substring(0, 40);
            return s;
        }

        public static string MakeOutputDir(string root, string tag)
        {
            if (string.IsNullOrEmpty(root)) root = Path.Combine(Path.GetTempPath(), "LogCollector");
            string name = SafeName(tag) + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static int Run(Session s, List<JobDef> jobs)
        {
            Directory.CreateDirectory(s.OutDir);
            DateTime start = DateTime.Now;
            int total = jobs.Count;
            int failed = 0;
            Jobs.DumpPolicyRam(s.Info.RamGB);

            for (int i = 0; i < total; i++)
            {
                JobDef def = jobs[i];
                JobResult r = new JobResult();
                r.Id = def.Id;
                r.Name = def.Name;
                r.Category = def.Category;
                s.Results.Add(r);

                if (s.Token.IsCancellationRequested)
                {
                    r.Status = JobStatus.Cancelled;
                    r.Message = "用户取消";
                    continue;
                }

                s.Step(total, def.Name);
                s.Log("[" + (i + 1) + "/" + total + "] " + def.Name + " …");
                DateTime t0 = DateTime.Now;
                r.Status = JobStatus.Running;
                try
                {
                    if (def.NeedAdmin && !s.IsAdmin)
                    {
                        r.Status = JobStatus.Skipped;
                        r.Message = "需要管理员权限，已跳过";
                        s.Log("  · 跳过（需要管理员权限）");
                    }
                    else
                    {
                        def.Run(s);
                        r.Status = JobStatus.Success;
                    }
                }
                catch (SkippedException ex)
                {
                    r.Status = JobStatus.Skipped;
                    r.Message = ex.Message;
                    s.Log("  · 跳过：" + ex.Message);
                }
                catch (Exception ex)
                {
                    r.Status = JobStatus.Failed;
                    r.Message = ex.Message;
                    failed++;
                    s.Log("  · 失败：" + ex.Message);
                }
                r.Seconds = (DateTime.Now - t0).TotalSeconds;

                string sub;
                if (JobDirs.TryGetValue(def.Id, out sub))
                {
                    string p = Path.Combine(s.OutDir, sub);
                    r.Path = p;
                    if (Directory.Exists(p)) r.Bytes = Tool.DirSize(p);
                }
                if (r.Status == JobStatus.Success)
                    s.Log("  · 完成，用时 " + ((int)r.Seconds) + " 秒" + (r.Bytes > 0 ? "，" + Tool.FmtSize(r.Bytes) : ""));

                s.StepDone(total);
            }

            DateTime end = DateTime.Now;

            // 浏览页：每个子目录一份，根目录一份汇总
            s.Step(total, "生成 HTML 浏览页");
            try { HtmlPreview.BuildAll(s); }
            catch (Exception ex) { s.Log("生成浏览页失败：" + ex.Message); }
            s.StepDone(total);

            // 报告与说明
            s.Step(total, "生成 HTML 报告");
            try
            {
                string html = Report.Build(s, start, end, s.Results);
                File.WriteAllText(Path.Combine(s.OutDir, "report.html"), html, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                s.Log("生成报告失败：" + ex.Message);
            }
            try { File.WriteAllText(Path.Combine(s.OutDir, "采集说明.txt"), Summary(s, start, end), new UTF8Encoding(true)); }
            catch { }
            s.StepDone(total);

            s.Log("");
            s.Log("采集完成：共 " + total + " 项，失败 " + failed + " 项");
            s.Log("输出目录：" + s.OutDir);
            return failed;
        }

        public static string Summary(Session s, DateTime start, DateTime end)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("Windows 笔记本日志采集包");
            b.AppendLine("========================================");
            b.AppendLine("机型      : " + s.Info.Display());
            b.AppendLine("序列号    : " + s.Info.Serial);
            b.AppendLine("系统      : " + s.Info.OsName + " " + s.Info.OsVersion + " Build " + s.Info.OsBuild + " " + s.Info.OsArch);
            b.AppendLine("CPU       : " + s.Info.Cpu);
            b.AppendLine("BIOS      : " + s.Info.Bios + " " + s.Info.BiosDate);
            b.AppendLine("采集人    : " + s.OperatorName);
            b.AppendLine("用例编号  : " + s.CaseId);
            b.AppendLine("采集时间  : " + start.ToString("yyyy-MM-dd HH:mm:ss") + " → " + end.ToString("yyyy-MM-dd HH:mm:ss")
                        + "（" + ((int)(end - start).TotalSeconds) + " 秒）");
            b.AppendLine("运行权限  : " + (s.IsAdmin ? "管理员" : "普通用户"));
            if (!string.IsNullOrEmpty(s.Symptom))
            {
                b.AppendLine("现象描述  :");
                b.AppendLine(s.Symptom);
            }
            b.AppendLine();
            b.AppendLine("采集项结果");
            b.AppendLine("----------------------------------------");
            foreach (JobResult r in s.Results)
            {
                string st = r.Status == JobStatus.Success ? "成功" : (r.Status == JobStatus.Failed ? "失败" : (r.Status == JobStatus.Skipped ? "跳过" : "未完成"));
                b.AppendLine(string.Format("{0,-10} {1,-30} {2,10}  {3}", st, r.Name, r.Bytes > 0 ? Tool.FmtSize(r.Bytes) : "-", r.Message));
            }
            b.AppendLine();
            b.AppendLine("查看方式");
            b.AppendLine("----------------------------------------");
            b.AppendLine("1. 先打开 report.html，看「关键发现」「电源与睡眠设置」与「事件时间线」；");
            b.AppendLine("2. 想看所有文本内容：打开 全部内容.html（每个子目录也有 浏览.html）；");
            b.AppendLine("3. 蓝屏问题：用 WinDbg 打开 30_崩溃转储 下的 .dmp 文件；");
            b.AppendLine("4. 事件日志：双击 10_系统日志/System.evtx 可用事件查看器直接打开；");
            b.AppendLine("5. 电池问题：打开 40_电源电池/电池报告.html；");
            b.AppendLine("6. 电源设置：见 41_电源设置/电源设置.txt（睡眠/休眠/关屏超时、合盖动作）。");
            return b.ToString();
        }
    }

    static class Program
    {
        public const string Version = "1.2";

        [STAThread]
        static int Main(string[] args)
        {
            bool cli = false, all = false, verbose = false;
            string outDir = "", jobsArg = "", appDir = "", caseId = "", symptom = "", tag = "", proc = "";
            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                if (!(raw.StartsWith("/") || raw.StartsWith("-"))) continue;
                string body = raw.TrimStart('/', '-');
                string key = body, val = null;
                int ci = body.IndexOf(':');
                if (ci > 0) { key = body.Substring(0, ci); val = body.Substring(ci + 1); }
                key = key.ToLowerInvariant();
                if (key == "cli") cli = true;
                else if (key == "all") all = true;
                else if (key == "verbose") verbose = true;
                else
                {
                    if (val == null && i + 1 < args.Length && !args[i + 1].StartsWith("/")) val = args[++i];
                    if (key == "out") outDir = val;
                    else if (key == "jobs") jobsArg = val;
                    else if (key == "appdir") appDir = val;
                    else if (key == "case") caseId = val;
                    else if (key == "symptom") symptom = val;
                    else if (key == "tag") tag = val;
                    else if (key == "proc") proc = val;
                }
            }
            outDir = outDir ?? "";
            jobsArg = jobsArg ?? "";
            appDir = appDir ?? "";
            caseId = caseId ?? "";
            symptom = symptom ?? "";
            tag = tag ?? "";
            proc = proc ?? "";

            if (cli) return Cli(outDir, all, jobsArg, appDir, caseId, symptom, tag, proc, verbose);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        static int Cli(string outDir, bool all, string jobsArg, string appDir, string caseId, string symptom, string tag, string proc, bool verbose)
        {
            Session s = new Session();
            s.IsAdmin = Tool.IsElevated();
            s.Verbose = verbose;
            s.Info = Tool.ReadMachine();
            if (s.Info.Serial.Length == 0)
            {
                try { s.Info.Serial = Tool.FetchSerial(CancellationToken.None).Trim(); } catch { }
            }
            s.CaseId = caseId;
            s.Symptom = symptom;
            s.OperatorName = Environment.UserName;
            s.AppDir = appDir;
            s.ProcessName = proc;
            s.Token = CancellationToken.None;
            s.OnLog = delegate(string m) { Console.WriteLine(m); };
            s.OnProgress = delegate(int d, int t, string n) { };

            List<JobDef> picked = new List<JobDef>();
            string[] ids = (jobsArg ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (JobDef d in Catalog.All())
            {
                if (all) picked.Add(d);
                else if (ids.Length > 0)
                {
                    foreach (string id in ids)
                        if (string.Equals(id.Trim(), d.Id, StringComparison.OrdinalIgnoreCase)) picked.Add(d);
                }
                else if (d.DefaultOn) picked.Add(d);
            }
            if (picked.Count == 0)
            {
                Console.WriteLine("没有匹配的采集项。");
                return 2;
            }
            string tagName = string.IsNullOrEmpty(tag) ? (s.Info.Serial.Length > 0 ? s.Info.Serial : s.Info.Model) : tag;
            s.OutDir = Runner.MakeOutputDir(outDir, tagName);
            int failed = Runner.Run(s, picked);
            Console.WriteLine(failed == 0 ? "OK" : "PARTIAL");
            return failed == 0 ? 0 : 1;
        }
    }
}
