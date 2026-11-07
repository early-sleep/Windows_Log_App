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
            d["events-system"] = "10_System_Logs";
            d["events-app"] = "11_Application_Logs";
            d["evtx-all"] = "12_All_Event_Logs";
            d["sysinfo"] = "20_System_Info";
            d["drivers"] = "21_Drivers_Devices";
            d["snapshot"] = "22_Runtime_Snapshot";
            d["software"] = "23_Patches_Software";
            d["msinfo32"] = "24_msinfo32";
            d["minidump"] = "30_Crash_Dumps";
            d["wer"] = "31_WER_Reports";
            d["livekernel"] = "34_LiveKernel_Reports";
            d["memdump"] = "32_Memory_Dump";
            d["pdump"] = "33_Process_Dump";
            d["battery"] = "40_Battery";
            d["powercfg"] = "41_Power_Settings";
            d["energy"] = "42_Energy_Report";
            d["sleepstudy"] = "43_Sleep_Study";
            d["perf"] = "50_Performance";
            d["gpu"] = "60_Display_GPU";
            d["netcfg"] = "70_Network";
            d["wlan"] = "71_WLAN_Report";
            d["disk"] = "80_Storage";
            d["panther"] = "35_Setup_Panther";
            d["winlogs"] = "36_Windows_Logs";
            d["inf"] = "37_Inf_Drivers";
            d["appdir"] = "90_App_Logs";
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
            b.AppendLine("2. For all text content open All_Content.html (each folder also has browse.html);");
            b.AppendLine("3. BSOD: open the .dmp file under 30_Crash_Dumps with WinDbg;");
            b.AppendLine("4. Event logs: double-click 10_System_Logs/System.evtx to open it in Event Viewer;");
            b.AppendLine("5. Battery: open 40_Battery/battery_report.html;");
            b.AppendLine("6. Power settings: see 41_Power_Settings/power_settings.txt (sleep/hibernate/display timeouts, lid action).");
            return b.ToString();
        }
    }

    static class Program
    {
        public const string Version = "1.7";

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
