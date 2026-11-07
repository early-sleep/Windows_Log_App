using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LogCollector
{
    public static class Report
    {
        public static string Build(Session s, DateTime start, DateTime end, List<JobResult> results)
        {
            List<Finding> findings = BuildFindings(s);
            StringBuilder b = new StringBuilder();

            b.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            b.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            b.Append("<title>Log analysis report - ").Append(H(s.Info.Display())).Append("</title>");
            b.Append("<style>");
            b.Append(Css);
            b.Append("</style></head><body>");

            // ---------- header ----------
            b.Append("<div class=\"wrap\">");
            b.Append("<div class=\"hd\">");
            b.Append("<div class=\"hd-l\">");
            b.Append("<div class=\"kicker\">Windows laptop log collection report</div>");
            b.Append("<h1>").Append(H(s.Info.Display())).Append("</h1>");
            b.Append("<div class=\"sub\">").Append(H(s.Info.OsName)).Append(" ").Append(H(s.Info.OsVersion))
             .Append(" (Build ").Append(H(s.Info.OsBuild)).Append(") · ").Append(H(s.Info.OsArch)).Append("</div>");
            b.Append("</div>");
            b.Append("<div class=\"hd-r\">");
            b.Append("<table class=\"meta\">");
            Row(b, "Collected", start.ToString("yyyy-MM-dd HH:mm:ss"));
            Row(b, "Duration", ((int)(end - start).TotalSeconds).ToString() + " s");
            Row(b, "Collected by", s.OperatorName);
            Row(b, "Case ID", s.CaseId);
            Row(b, "Event range", s.RangeText());
            Row(b, "Privilege", s.IsAdmin ? "Administrator" : "Standard user");
            b.Append("</table></div></div>");

            // ---------- test context ----------
            if (!string.IsNullOrEmpty(s.Symptom))
            {
                b.Append("<div class=\"card\"><div class=\"card-t\">Symptom</div><div class=\"sym\">")
                 .Append(H(s.Symptom).Replace("\n", "<br>")).Append("</div></div>");
            }

            // ---------- summary cards ----------
            b.Append("<div class=\"cards\">");
            int bug = CountBugCheck(s);
            int shut = CountAny(s, new int[] { 41, 6008 });
            int whea = CountAny(s, new int[] { 17, 18, 19, 47 });
            int tdr = Count(s, 4101);
            int pnp = CountProbe(s, "PNP.");
            b.Append(Card("Blue screens", bug.ToString(), bug > 0 ? "bad" : "ok", "BugCheck records (last 7 days)"));
            b.Append(Card("Unexpected shutdowns", shut.ToString(), shut > 0 ? "bad" : "ok", "Power loss / hang / power button"));
            b.Append(Card("Hardware errors", whea.ToString(), whea > 0 ? "bad" : "ok", "WHEA corrected and fatal"));
            b.Append(Card("Display timeouts", tdr.ToString(), tdr > 0 ? "warn" : "ok", "TDR recoveries"));
            b.Append(Card("Problem devices", pnp.ToString(), pnp > 0 ? "warn" : "ok", "Devices with a non-zero error code"));
            b.Append(Card("Uptime", H(s.Info.Uptime), "info", "Booted " + H(s.Info.BootTime)));
            b.Append("</div>");

            // ---------- key findings ----------
            b.Append("<div class=\"card\"><div class=\"card-t\">Key findings</div>");
            if (findings.Count == 0)
            {
                b.Append("<div class=\"empty\">No obvious anomalies were found in the collected logs. If the issue is real, check that the collection time window covers the moment it occurred, and try selecting more items.</div>");
            }
            else
            {
                b.Append("<ul class=\"findings\">");
                foreach (Finding f in findings)
                {
                    b.Append("<li class=\"").Append(f.Level).Append("\"><span class=\"dot\"></span>")
                     .Append(H(f.Text)).Append("</li>");
                }
                b.Append("</ul>");
            }
            b.Append("</div>");

            // ---------- event timeline ----------
            b.Append("<div class=\"card\"><div class=\"card-t\">Event timeline <span class=\"hint\">newest first, up to 60 rows</span></div>");
            List<EventRow> evs = new List<EventRow>(s.Events);
            evs.Sort(delegate(EventRow a, EventRow c) { return c.Time.CompareTo(a.Time); });
            if (evs.Count == 0)
            {
                b.Append("<div class=\"empty\">No key events were parsed (none in the time window, or the event log could not be read).</div>");
            }
            else
            {
                b.Append("<table class=\"tl\"><thead><tr><th style=\"width:150px\">Time</th><th style=\"width:68px\">Level</th>")
                 .Append("<th style=\"width:210px\">Source</th><th style=\"width:52px\">ID</th><th>Description</th></tr></thead><tbody>");
                int n = 0;
                int skipped = 0;
                Dictionary<string, int> seen = new Dictionary<string, int>();
                foreach (EventRow e in evs)
                {
                    string key = e.Log + "|" + e.Id;
                    int c = 0;
                    seen.TryGetValue(key, out c);
                    if (c >= 4) { skipped++; continue; }
                    if (n >= 60) { skipped++; continue; }
                    seen[key] = c + 1;
                    string lv = EventCatalog.LevelText(e.Level);
                    string desc = e.Describe();
                    string detail = "";
                    if (desc.Length == 0) { desc = Clip(e.Message, 160); }
                    else detail = Clip(e.Message, 160);
                    b.Append("<tr><td class=\"mono\">").Append(e.Time.ToString("MM-dd HH:mm:ss")).Append("</td><td>")
                     .Append("<span class=\"chip ").Append(lv == "Error" ? "c-bad" : (lv == "Warning" ? "c-warn" : "c-info")).Append("\">")
                     .Append(H(lv)).Append("</span></td><td class=\"src\">").Append(H(e.Provider))
                     .Append("</td><td class=\"mono\">").Append(e.Id).Append("</td><td>")
                     .Append(H(desc));
                    if (detail.Length > 0) b.Append("<div class=\"msg\">").Append(H(detail)).Append("</div>");
                    b.Append("</td></tr>");
                    n++;
                }
                if (skipped > 0)
                {
                    b.Append("<tr><td colspan=\"5\" class=\"msg\">").Append(skipped)
                     .Append(" more similar or older events are not listed; see the CSVs in 10_System_Logs and 11_Application_Logs.</td></tr>");
                }
                b.Append("</tbody></table>");
            }
            b.Append("</div>");

            // ---------- collection results ----------
            b.Append("<div class=\"card\"><div class=\"card-t\">Collection results</div><table class=\"tl\">");
            b.Append("<thead><tr><th style=\"width:260px\">Item</th><th style=\"width:80px\">Status</th><th style=\"width:90px\">Size</th><th>Note</th></tr></thead><tbody>");
            foreach (JobResult r in results)
            {
                string cls = r.Status == JobStatus.Success ? "ok" : (r.Status == JobStatus.Failed ? "bad" : "warn");
                string txt = r.Status == JobStatus.Success ? "Success" : (r.Status == JobStatus.Failed ? "Failed" : (r.Status == JobStatus.Skipped ? "Skipped" : "Incomplete"));
                b.Append("<tr><td>").Append(H(r.Name)).Append("</td><td><span class=\"chip c-").Append(cls).Append("\">")
                 .Append(txt).Append("</span></td><td class=\"mono\">").Append(r.Bytes > 0 ? Tool.FmtSize(r.Bytes) : "-")
                 .Append("</td><td class=\"msg\">").Append(H(r.Message)).Append("</td></tr>");
            }
            b.Append("</tbody></table></div>");

            // ---------- hardware ----------
            b.Append("<div class=\"card\"><div class=\"card-t\">Hardware and system</div><table class=\"kv\">");
            Kv(b, "Model", s.Info.Display());
            Kv(b, "Serial", s.Info.Serial);
            Kv(b, "BIOS", s.Info.Bios + (s.Info.BiosDate.Length > 0 ? "（" + s.Info.BiosDate + "）" : ""));
            Kv(b, "Board", s.Info.Board);
            Kv(b, "CPU", s.Info.Cpu + (s.Info.CpuCores.Length > 0 ? " / " + s.Info.CpuCores : ""));
            Kv(b, "Memory", s.Info.RamGB > 0 ? s.Info.RamGB + " GB" : "");
            string mem = ProbeJoin(s, "MEM.");
            if (mem.Length > 0) Kv(b, "Memory modules", mem);
            string gpu = ProbeJoin(s, "GPU.");
            if (gpu.Length > 0) Kv(b, "Graphics", gpu);
            string disk = ProbeJoin(s, "DISK.");
            if (disk.Length > 0) Kv(b, "Storage", disk);
            Kv(b, "Operating system", s.Info.OsName + " " + s.Info.OsVersion + " Build " + s.Info.OsBuild);
            Kv(b, "Installed", s.Info.OsInstall);
            Kv(b, "Computer name", s.Info.ComputerName);
            b.Append("</table></div>");

            // ---------- Battery ----------
            string bat = BatteryBlock(s);
            if (bat.Length > 0)
                b.Append("<div class=\"card\"><div class=\"card-t\">Battery and power</div>").Append(bat).Append("</div>");

            // ---------- power and sleep settings ----------
            string pwr = PowerBlock(s);
            if (pwr.Length > 0)
                b.Append("<div class=\"card\"><div class=\"card-t\">Power and sleep settings <span class=\"hint\">active power scheme</span></div>")
                 .Append(pwr).Append("</div>");

            // ---------- file list ----------
            b.Append("<div class=\"card\"><div class=\"card-t\">Collected files</div><table class=\"tl\">");
            b.Append("<thead><tr><th style=\"width:300px\">Folder / file</th><th style=\"width:100px\">Size</th><th>Description</th></tr></thead><tbody>");
            try
            {
                foreach (string d in Directory.GetDirectories(s.OutDir))
                {
                    string name = Path.GetFileName(d);
                    bool hasPage = File.Exists(Path.Combine(d, "browse.html"));
                    b.Append("<tr><td class=\"mono\">");
                    if (hasPage) b.Append("<a href=\"").Append(H(name)).Append("/browse.html\">").Append(H(name)).Append("/</a>");
                    else b.Append(H(name)).Append("/");
                    b.Append("</td><td class=\"mono\">")
                     .Append(Tool.FmtSize(Tool.DirSize(d))).Append("</td><td class=\"msg\">")
                     .Append(H(Explain(name))).Append("</td></tr>");
                }
            }
            catch { }
            b.Append("</tbody></table>");
            b.Append("<div class=\"msg\" style=\"margin-top:8px\">All text files are also available in <a href=\"All_Content.html\">All_Content.html</a>, or per folder via browse.html.</div>");
            b.Append("</div>");

            // ---------- footer ----------
            b.Append("<div class=\"ft\">Generated by Laptop Log Collector v" + H(Program.Version) + ". The content is raw collected fact, not inference.");
            b.Append("Raw files are in the same folder; <code>System.evtx</code> and <code>Application.evtx</code> open directly in Event Viewer.");
            b.Append("This report may contain personal data; handle it according to your data classification policy.</div>");
            b.Append("</div></body></html>");
            return b.ToString();
        }

        private static string PowerBlock(Session s)
        {
            bool any = false;
            StringBuilder b = new StringBuilder();
            b.Append("<table class=\"tl\"><thead><tr><th style=\"width:240px\">Setting</th>")
             .Append("<th style=\"width:170px\">AC (plugged in)</th><th>DC (battery)</th></tr></thead><tbody>");
            foreach (string[] item in Jobs.PowerItems)
            {
                string k = "PWR." + item[0] + "_ac";
                if (!s.Probe.ContainsKey(k)) continue;
                any = true;
                string v = s.Probe[k];
                string d;
                s.Probe.TryGetValue("PWR." + item[0] + "_dc", out d);
                b.Append("<tr><td>").Append(H(item[1])).Append("</td><td class=\"mono\">")
                 .Append(H(v)).Append("</td><td class=\"mono\">").Append(H(d)).Append("</td></tr>");
            }
            b.Append("</tbody></table>");
            if (!any) return "";
            b.Append("<div class=\"msg\" style=\"margin-top:6px\">Sleep / hibernate / display-off are idle timeouts; \"Never\" means they do not trigger automatically.</div>");
            return b.ToString();
        }

        private static string BatteryBlock(Session s)
        {
            string des = Get(s, "BAT.DesignedCapacity");
            string full = Get(s, "BAT.FullChargedCapacity");
            string cyc = Get(s, "BAT.CycleCount");
            string charge = Get(s, "BAT.Charge");
            string name = Get(s, "BAT.Name");
            if (des.Length == 0 && full.Length == 0 && charge.Length == 0) return "";
            StringBuilder b = new StringBuilder();
            b.Append("<table class=\"kv\">");
            Kv(b, "Battery", name);
            int d, f;
            if (int.TryParse(des, out d) && int.TryParse(full, out f) && d > 0)
            {
                int pct = (int)Math.Round(f * 100.0 / d);
                Kv(b, "Design / full charge", d + " mWh / " + f + " mWh");
                Kv(b, "Health", pct + "%" + (pct < 80 ? " (low, needs attention)" : ""));
            }
            Kv(b, "Cycle count", cyc);
            Kv(b, "Charge level", charge.Length > 0 ? charge + "%" : "");
            b.Append("</table>");
            return b.ToString();
        }

        public static List<Finding> BuildFindings(Session s)
        {
            List<Finding> list = new List<Finding>();
            int bug = CountBugCheck(s);
            if (bug > 0)
            {
                EventRow last = LastBugCheck(s);
                list.Add(new Finding("bad", bug + " blue screen(s) recorded (BugCheck), most recent "
                    + (last != null ? last.Time.ToString("MM-dd HH:mm") + ", stop code " + BugCode(last.Message) : "") +
                    ". Please include the dump files from the Minidump folder."));
            }
            int shut = CountAny(s, new int[] { 41, 6008 });
            if (shut > 0)
                list.Add(new Finding("bad", shut + " unexpected shutdown(s) (power loss, hang or power button); data loss is possible."));
            int whea = CountAny(s, new int[] { 17, 18, 19, 47 });
            if (whea > 0)
                list.Add(new Finding("bad", whea + " WHEA hardware error record(s), pointing at CPU / memory / PCIe level issues."));
            int tdr = Count(s, 4101);
            if (tdr > 0)
                list.Add(new Finding("warn", tdr + " display driver timeout recovery (TDR); symptoms are brief black screens, artifacts or driver resets."));
            int drv = CountAny(s, new int[] { 219, 411 });
            if (drv > 0)
                list.Add(new Finding("warn", drv + " driver load / device start failure(s); see the problem device list under Drivers and devices."));
            int svc = CountAny(s, new int[] { 7000, 7001, 7031, 7034 });
            if (svc > 0)
                list.Add(new Finding("warn", svc + " service start failure or unexpected termination(s); may affect boot time and background features."));
            int disk = CountAny(s, new int[] { 7, 11, 51, 129 });
            if (disk > 0)
                list.Add(new Finding("bad", disk + " disk error event(s) (bad blocks / controller errors / resets); check storage health."));
            int th = Count(s, 37);
            if (th > 0)
                list.Add(new Finding("warn", th + " processor throttling event(s) caused by firmware; may be triggered by temperature or power policy."));
            int appCrash = CountApp(s);
            if (appCrash > 0)
                list.Add(new Finding("warn", appCrash + " application crash or hang(s) in the last 7 days; see 11_Application_Logs."));
            int pnp = CountProbe(s, "PNP.");
            if (pnp > 0)
            {
                List<string> names = new List<string>();
                foreach (KeyValuePair<string, string> kv in s.Probe)
                {
                    if (kv.Key.StartsWith("PNP.") && names.Count < 5) names.Add(kv.Key.Substring(4));
                }
                list.Add(new Finding("warn", pnp + " device(s) report a driver error code: " + string.Join(", ", names.ToArray()) + "."));
            }
            int dumps = CountFiles(Path.Combine(s.OutDir, "30_Crash_Dumps"), "*.dmp", true);
            if (dumps > 0)
                list.Add(new Finding("info", dumps + " memory dump file(s) collected; analyse with WinDbg or BlueScreenView."));
            if (dumps == 0 && bug > 0)
                list.Add(new Finding("warn", "Blue screen records exist but no dump file was found (dumps may be disabled or cleaned up). Enable a kernel memory dump and reproduce."));

            string des = Get(s, "BAT.DesignedCapacity");
            string full = Get(s, "BAT.FullChargedCapacity");
            int d, f;
            if (int.TryParse(des, out d) && int.TryParse(full, out f) && d > 0)
            {
                int pct = (int)Math.Round(f * 100.0 / d);
                if (pct < 80)
                    list.Add(new Finding("warn", "Battery health is " + pct + "% (full charge " + f + " mWh / design " + d + " mWh); below 80% runtime issues are usually battery wear."));
            }
            if (list.Count == 0)
                list.Add(new Finding("info", "No blue screens, unexpected shutdowns, hardware errors, driver failures or disk warnings were found."));
            return list;
        }

        private static string BugCode(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return "Unknown";
            Match m = Regex.Match(msg, "0x[0-9a-fA-F]{8,16}");
            if (m.Success) return m.Value.ToUpperInvariant();
            return "Unknown";
        }

        private static int Count(Session s, int id)
        {
            int n = 0;
            foreach (EventRow e in s.Events) if (e.Id == id && e.Log == "System") n++;
            return n;
        }

        private static int CountAny(Session s, int[] ids)
        {
            int n = 0;
            foreach (EventRow e in s.Events)
            {
                if (e.Log != "System") continue;
                foreach (int id in ids)
                    if (e.Id == id) { n++; break; }
            }
            return n;
        }

        private static int CountApp(Session s)
        {
            int n = 0;
            foreach (EventRow e in s.Events)
                if (e.Log == "Application" && (e.Id == 1000 || e.Id == 1002)) n++;
            return n;
        }

        private static bool IsBugCheck(EventRow e)
        {
            if (e.Id != 1001 || e.Log != "System") return false;
            return e.Provider.IndexOf("BugCheck", StringComparison.OrdinalIgnoreCase) >= 0
                || e.Provider.IndexOf("SystemErrorReporting", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int CountBugCheck(Session s)
        {
            int n = 0;
            foreach (EventRow e in s.Events) if (IsBugCheck(e)) n++;
            return n;
        }

        private static EventRow LastBugCheck(Session s)
        {
            EventRow r = null;
            foreach (EventRow e in s.Events)
                if (IsBugCheck(e) && (r == null || e.Time > r.Time)) r = e;
            return r;
        }

        private static int CountProbe(Session s, string prefix)
        {
            int n = 0;
            foreach (KeyValuePair<string, string> kv in s.Probe)
                if (kv.Key.StartsWith(prefix)) n++;
            return n;
        }

        private static int CountFiles(string dir, string pattern, bool recursive)
        {
            try
            {
                if (!Directory.Exists(dir)) return 0;
                return Directory.GetFiles(dir, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Length;
            }
            catch { return 0; }
        }

        private static string Get(Session s, string key)
        {
            string v;
            if (s.Probe.TryGetValue(key, out v)) return v;
            return "";
        }

        private static string ProbeJoin(Session s, string prefix)
        {
            List<string> vals = new List<string>();
            foreach (KeyValuePair<string, string> kv in s.Probe)
            {
                if (kv.Key.StartsWith(prefix)) vals.Add(kv.Value);
            }
            return string.Join("；", vals.ToArray());
        }

        private static string Explain(string dir)
        {
            if (dir.StartsWith("10")) return "Raw system event log and key events table";
            if (dir.StartsWith("11")) return "Application event log and crash records";
            if (dir.StartsWith("20")) return "System info, hardware details, summary";
            if (dir.StartsWith("21")) return "Driver list, driver store, problem devices";
            if (dir.StartsWith("22")) return "Processes, services, startup, scheduled tasks";
            if (dir.StartsWith("23")) return "Updates and installed software";
            if (dir.StartsWith("24")) return "msinfo32 full report (optional)";
            if (dir.StartsWith("30")) return "Crash dump files for WinDbg";
            if (dir.StartsWith("31")) return "Windows Error Reporting (WER)";
            if (dir.StartsWith("32")) return "Full memory dump (large)";
            if (dir.StartsWith("33")) return "Process memory dump (opens in WinDbg)";
            if (dir.StartsWith("40")) return "Battery health report";
            if (dir.StartsWith("41")) return "Power scheme, sleep states, wake sources";
            if (dir.StartsWith("42")) return "Energy audit (60 s)";
            if (dir.StartsWith("43")) return "Modern Standby (S0ix) diagnostics";
            if (dir.StartsWith("50")) return "CPU / memory / disk sampling";
            if (dir.StartsWith("60")) return "dxdiag and graphics driver info";
            if (dir.StartsWith("70")) return "Network config, Wi-Fi interfaces and drivers";
            if (dir.StartsWith("71")) return "Wi-Fi connection history timeline";
            if (dir.StartsWith("80")) return "Disk and SMART health";
            if (dir.StartsWith("90")) return "Extra logs from the given folder";
            return "";
        }

        private static void Row(StringBuilder b, string k, string v)
        {
            b.Append("<tr><th>").Append(H(k)).Append("</th><td>").Append(H(v)).Append("</td></tr>");
        }

        private static void Kv(StringBuilder b, string k, string v)
        {
            if (string.IsNullOrEmpty(v)) return;
            b.Append("<tr><th>").Append(H(k)).Append("</th><td>").Append(H(v)).Append("</td></tr>");
        }

        private static string Card(string title, string value, string cls, string sub)
        {
            return "<div class=\"card c-" + cls + "\"><div class=\"card-t\">" + H(title) + "</div><div class=\"big\">" +
                   H(value) + "</div><div class=\"sub2\">" + H(sub) + "</div></div>";
        }

        private static string H(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string Clip(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > n ? s.Substring(0, n) + "…" : s;
        }

        private const string Css = @"
*{box-sizing:border-box}
body{margin:0;background:#f4f6fa;color:#1f2937;font:14px/1.65 'Microsoft YaHei','PingFang SC',system-ui,sans-serif}
.wrap{max-width:1100px;margin:0 auto;padding:28px 22px 60px}
.hd{display:flex;justify-content:space-between;align-items:flex-end;gap:24px;border-bottom:3px solid #1f3864;padding-bottom:16px;margin-bottom:22px}
.kicker{font-size:12px;letter-spacing:.12em;color:#2e5395;font-weight:700}
h1{margin:6px 0 4px;font-size:26px;color:#1f3864}
.sub{color:#6b7280;font-size:13px}
.meta{border-collapse:collapse;font-size:12.5px}
.meta th{text-align:right;color:#6b7280;font-weight:400;padding:2px 10px 2px 0;white-space:nowrap}
.meta td{padding:2px 0;white-space:nowrap}
.cards{display:grid;grid-template-columns:repeat(6,1fr);gap:12px;margin-bottom:18px}
.card{background:#fff;border:1px solid #e5e7eb;border-radius:10px;padding:14px 16px;margin-bottom:16px}
.cards .card{margin:0;border-top:3px solid #d1d5db}
.cards .c-bad{border-top-color:#dc2626}
.cards .c-warn{border-top-color:#f59e0b}
.cards .c-ok{border-top-color:#10b981}
.cards .c-info{border-top-color:#3b82f6}
.card-t{font-weight:700;color:#1f3864;margin-bottom:8px;font-size:14px}
.hint{font-weight:400;color:#9ca3af;font-size:12px;margin-left:6px}
.big{font-size:22px;font-weight:700;line-height:1.25;word-break:keep-all}
.sub2{font-size:11.5px;color:#6b7280;margin-top:2px}
.sym{background:#f9fafb;border-left:3px solid #2e5395;padding:10px 14px;border-radius:4px;white-space:normal}
.findings{list-style:none;margin:0;padding:0}
.findings li{position:relative;padding:8px 10px 8px 26px;border-radius:6px;margin-bottom:6px;background:#f9fafb}
.findings li .dot{position:absolute;left:11px;top:16px;width:7px;height:7px;border-radius:50%;background:#9ca3af}
.findings li.bad{background:#fef2f2;color:#991b1b}
.findings li.bad .dot{background:#dc2626}
.findings li.warn{background:#fffbeb;color:#92400e}
.findings li.warn .dot{background:#f59e0b}
.findings li.info{background:#eff6ff;color:#1e3a8a}
.findings li.info .dot{background:#3b82f6}
table.tl{width:100%;border-collapse:collapse;font-size:12.5px}
table.tl th{background:#f1f5f9;text-align:left;padding:8px 10px;color:#475569;font-weight:600;border-bottom:1px solid #e2e8f0}
table.tl td{padding:8px 10px;border-bottom:1px solid #f1f5f9;vertical-align:top}
table.tl tr:hover td{background:#fafcff}
.mono{font-family:Consolas,'Courier New',monospace;font-size:12px;color:#334155}
.src{color:#475569;word-break:break-all}
.msg{color:#6b7280;font-size:11.5px;margin-top:2px;word-break:break-word}
.chip{display:inline-block;padding:1px 8px;border-radius:10px;font-size:11px;font-weight:600;white-space:nowrap}
.c-bad{background:#fee2e2;color:#b91c1c}
.c-warn{background:#fef3c7;color:#92400e}
.c-ok{background:#d1fae5;color:#065f46}
.c-info{background:#dbeafe;color:#1e40af}
table.kv{width:100%;border-collapse:collapse;font-size:13px}
table.kv th{width:150px;text-align:left;color:#6b7280;font-weight:400;padding:6px 10px 6px 0;vertical-align:top;border-bottom:1px solid #f1f5f9}
table.kv td{padding:6px 0;border-bottom:1px solid #f1f5f9;word-break:break-word}
.empty{color:#6b7280;background:#f9fafb;padding:12px;border-radius:6px}
.ft{margin-top:26px;padding-top:14px;border-top:1px solid #e5e7eb;color:#9ca3af;font-size:12px}
.ft code{background:#f1f5f9;padding:1px 5px;border-radius:3px;color:#475569}
@media (max-width:900px){.cards{grid-template-columns:repeat(3,1fr)}.hd{flex-direction:column;align-items:flex-start}}
";
    }
}
