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
            d["events-system"] = "Basics/1_System_Logs";
            d["events-app"] = "Basics/2_Application_Logs";
            d["evtx-all"] = "Basics/3_All_Event_Logs";
            d["sysinfo"] = "Basics/4_System_Info";
            d["drivers"] = "Basics/5_Drivers_Devices";
            d["snapshot"] = "Basics/6_Runtime_Snapshot";
            d["software"] = "Basics/7_Installed_Updates_and_Software";
            d["minidump"] = "Crashes and dumps/8_Mini_Dumps";
            d["wer"] = "Crashes and dumps/9_WER_Reports";
            d["livekernel"] = "Crashes and dumps/10_LiveKernel_Reports";
            d["memdump"] = "Crashes and dumps/11_Memory_Dump";
            d["panther"] = "Setup and component logs/12_Setup_Panther";
            d["winlogs"] = "Setup and component logs/13_Windows_Logs";
            d["inf"] = "Setup and component logs/14_Inf_Drivers";
            d["battery"] = "Power and battery/15_Battery";
            d["powercfg"] = "Power and battery/16_Power_Settings";
            d["sleepstudy"] = "Power and battery/17_Sleep_Study";
            d["gpu"] = "Performance/18_DXDiag";
            d["netcfg"] = "Network/19_Network";
            d["disk"] = "Storage/20_Storage";
            return d;
        }

        /// <summary>目录名 -> 带分类的完整相对路径（"1_System_Logs" -> "Basics/1_System_Logs"）。</summary>
        public static string FullPath(string folder)
        {
            string full;
            if (FolderMap.TryGetValue(folder, out full)) return full;
            return folder;
        }

        private static Dictionary<string, string> _folderMap;
        private static Dictionary<string, string> FolderMap
        {
            get
            {
                if (_folderMap != null) return _folderMap;
                Dictionary<string, string> m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string v in JobDirs.Values)
                {
                    int cut = v.LastIndexOf('/');
                    string leaf = cut >= 0 ? v.Substring(cut + 1) : v;
                    m[leaf] = v;
                }
                _folderMap = m;
                return m;
            }
        }

        /// <summary>任务对应的目录序号，界面上显示的数字和输出目录前缀保持一致。</summary>
        public static int NumOf(string jobId)
        {
            string dir;
            if (jobId == null || !JobDirs.TryGetValue(jobId, out dir)) return 0;
            int slash = dir.LastIndexOf('/');
            string leaf = slash >= 0 ? dir.Substring(slash + 1) : dir;
            int cut = leaf.IndexOf('_');
            int n;
            if (cut > 0 && int.TryParse(leaf.Substring(0, cut), out n)) return n;
            return 0;
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

            // 收集人姓名单独写一份，放在包根目录
            try
            {
                string who = string.IsNullOrEmpty(s.OperatorName) ? Environment.UserName : s.OperatorName;
                StringBuilder w = new StringBuilder();
                w.AppendLine("Collected by : " + who);
                w.AppendLine("Package      : " + Path.GetFileName(s.OutDir));
                w.AppendLine("Machine      : " + s.Info.Display() + "   SN " + s.Info.Serial);
                w.AppendLine("Started      : " + start.ToString("yyyy-MM-dd HH:mm:ss"));
                File.WriteAllText(Path.Combine(s.OutDir, "Collected_By.txt"), w.ToString(), new UTF8Encoding(true));
            }
            catch { }

            s.Log("Package: " + Path.GetFileName(s.OutDir)
                  + (string.IsNullOrEmpty(s.Info.Serial) ? "  (BIOS serial not available, named after the model)" : ""));

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
                    r.Message = "Cancelled by user";
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
                        r.Message = "Administrator rights required; skipped";
                        s.Log("  · Skipped (requires administrator)");
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
                    s.Log("  · Skipped: " + ex.Message);
                }
                catch (Exception ex)
                {
                    r.Status = JobStatus.Failed;
                    r.Message = ex.Message;
                    failed++;
                    s.Log("  · Failed: " + ex.Message);
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
                    s.Log("  · Done in " + ((int)r.Seconds) + " s" + (r.Bytes > 0 ? ", " + Tool.FmtSize(r.Bytes) : ""));

                s.StepDone(total);
            }

            DateTime end = DateTime.Now;

            // viewer: one page per folder plus an overview at the root
            s.Step(total, "Generating HTML viewer");
            try { HtmlPreview.BuildAll(s); }
            catch (Exception ex) { s.Log("Failed to generate HTML viewer: " + ex.Message); }
            s.StepDone(total);

            // report and notes
            s.Step(total, "Generating HTML report");
            try
            {
                string html = Report.Build(s, start, end, s.Results);
                File.WriteAllText(Path.Combine(s.OutDir, "report.html"), html, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                s.Log("Failed to generate report: " + ex.Message);
            }
            try { File.WriteAllText(Path.Combine(s.OutDir, "Collection_Info.txt"), Summary(s, start, end), new UTF8Encoding(true)); }
            catch { }
            s.StepDone(total);

            s.Log("");
            s.Log("Done: " + total + " item(s), " + failed + " failed");
            s.Log("Output folder: " + s.OutDir);
            return failed;
        }

        public static string Summary(Session s, DateTime start, DateTime end)
        {
            StringBuilder b = new StringBuilder();
            b.AppendLine("Windows laptop log collection package");
            b.AppendLine("========================================");
            b.AppendLine("Model      : " + s.Info.Display());
            b.AppendLine("Serial     : " + s.Info.Serial);
            b.AppendLine("OS         : " + s.Info.OsName + " " + s.Info.OsVersion + " Build " + s.Info.OsBuild + " " + s.Info.OsArch);
            b.AppendLine("CPU        : " + s.Info.Cpu);
            b.AppendLine("BIOS       : " + s.Info.Bios + " " + s.Info.BiosDate);
            b.AppendLine("Collected  : " + s.OperatorName);
            b.AppendLine("Case ID    : " + s.CaseId);
            b.AppendLine("Time       : " + start.ToString("yyyy-MM-dd HH:mm:ss") + " → " + end.ToString("yyyy-MM-dd HH:mm:ss")
                        + " (" + ((int)(end - start).TotalSeconds) + " s)");
            b.AppendLine("Privilege  : " + (s.IsAdmin ? "Administrator" : "Standard user"));
            if (!string.IsNullOrEmpty(s.Symptom))
            {
                b.AppendLine("Symptom    :");
                b.AppendLine(s.Symptom);
            }
            b.AppendLine();
            b.AppendLine("Results");
            b.AppendLine("----------------------------------------");
            foreach (JobResult r in s.Results)
            {
                string st = r.Status == JobStatus.Success ? "Success" : (r.Status == JobStatus.Failed ? "Failed" : (r.Status == JobStatus.Skipped ? "Skipped" : "Incomplete"));
                b.AppendLine(string.Format("{0,-10} {1,-30} {2,10}  {3}", st, r.Name, r.Bytes > 0 ? Tool.FmtSize(r.Bytes) : "-", r.Message));
            }
            b.AppendLine();
            b.AppendLine("How to read");
            b.AppendLine("----------------------------------------");
            b.AppendLine("1. Open report.html first: Key findings, Power and sleep settings, Event timeline;");
            b.AppendLine("2. For all text content open All_Content.html;");
            b.AppendLine("3. BSOD: open the .dmp file under Crashes and dumps/8_Mini_Dumps with WinDbg;");
            b.AppendLine("4. Event logs: double-click Basics/1_System_Logs/System.evtx to open it in Event Viewer,");
            b.AppendLine("   or read Basics/1_System_Logs/System_events.csv for every event with the full message;");
            b.AppendLine("5. Battery: open Power and battery/15_Battery/battery_report.html;");
            b.AppendLine("6. Power settings: see Power and battery/16_Power_Settings/power_settings.txt (sleep/hibernate/display timeouts, lid action).");
            return b.ToString();
        }
    }

    static class Program
    {
        public const string Version = "3.11";

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
                Console.WriteLine("No matching collection items.");
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
