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
            b.Append("<title>日志分析报告 - ").Append(H(s.Info.Display())).Append("</title>");
            b.Append("<style>");
            b.Append(Css);
            b.Append("</style></head><body>");

            // ---------- 头部 ----------
            b.Append("<div class=\"wrap\">");
            b.Append("<div class=\"hd\">");
            b.Append("<div class=\"hd-l\">");
            b.Append("<div class=\"kicker\">Windows 笔记本日志采集报告</div>");
            b.Append("<h1>").Append(H(s.Info.Display())).Append("</h1>");
            b.Append("<div class=\"sub\">").Append(H(s.Info.OsName)).Append(" ").Append(H(s.Info.OsVersion))
             .Append(" (Build ").Append(H(s.Info.OsBuild)).Append(") · ").Append(H(s.Info.OsArch)).Append("</div>");
            b.Append("</div>");
            b.Append("<div class=\"hd-r\">");
            b.Append("<table class=\"meta\">");
            Row(b, "采集时间", start.ToString("yyyy-MM-dd HH:mm:ss"));
            Row(b, "采集耗时", ((int)(end - start).TotalSeconds).ToString() + " 秒");
            Row(b, "采集人", s.OperatorName);
            Row(b, "用例编号", s.CaseId);
            Row(b, "事件覆盖", s.RangeText());
            Row(b, "运行权限", s.IsAdmin ? "管理员" : "普通用户");
            b.Append("</table></div></div>");

            // ---------- 现场信息 ----------
            if (!string.IsNullOrEmpty(s.Symptom))
            {
                b.Append("<div class=\"card\"><div class=\"card-t\">现象描述</div><div class=\"sym\">")
                 .Append(H(s.Symptom).Replace("\n", "<br>")).Append("</div></div>");
            }

            // ---------- 结论卡片 ----------
            b.Append("<div class=\"cards\">");
            int bug = CountBugCheck(s);
            int shut = CountAny(s, new int[] { 41, 6008 });
            int whea = CountAny(s, new int[] { 17, 18, 19, 47 });
            int tdr = Count(s, 4101);
            int pnp = CountProbe(s, "PNP.");
            b.Append(Card("蓝屏次数", bug.ToString(), bug > 0 ? "bad" : "ok", "BugCheck 记录（近 7 天）"));
            b.Append(Card("异常关机", shut.ToString(), shut > 0 ? "bad" : "ok", "断电 / 死机 / 长按电源"));
            b.Append(Card("硬件错误", whea.ToString(), whea > 0 ? "bad" : "ok", "WHEA 已纠正与致命错误"));
            b.Append(Card("显示超时", tdr.ToString(), tdr > 0 ? "warn" : "ok", "TDR 显示驱动恢复"));
            b.Append(Card("异常设备", pnp.ToString(), pnp > 0 ? "warn" : "ok", "驱动错误码非 0 的设备"));
            b.Append(Card("运行时长", H(s.Info.Uptime), "info", "上次启动 " + H(s.Info.BootTime)));
            b.Append("</div>");

            // ---------- 关键发现 ----------
            b.Append("<div class=\"card\"><div class=\"card-t\">关键发现</div>");
            if (findings.Count == 0)
            {
                b.Append("<div class=\"empty\">在已采集的日志中没有发现明显异常。如果问题确实存在，请确认采集时间点是否覆盖了问题发生时刻，并尝试勾选更多采集项。</div>");
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

            // ---------- 事件时间线 ----------
            b.Append("<div class=\"card\"><div class=\"card-t\">关键事件时间线 <span class=\"hint\">按时间倒序，最多 60 条</span></div>");
            List<EventRow> evs = new List<EventRow>(s.Events);
            evs.Sort(delegate(EventRow a, EventRow c) { return c.Time.CompareTo(a.Time); });
            if (evs.Count == 0)
            {
                b.Append("<div class=\"empty\">未解析到关键事件（可能时间窗内无记录，或事件日志读取失败）。</div>");
            }
            else
            {
                b.Append("<table class=\"tl\"><thead><tr><th style=\"width:150px\">时间</th><th style=\"width:68px\">级别</th>")
                 .Append("<th style=\"width:210px\">来源</th><th style=\"width:52px\">ID</th><th>说明</th></tr></thead><tbody>");
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
                     .Append("<span class=\"chip ").Append(lv == "错误" ? "c-bad" : (lv == "警告" ? "c-warn" : "c-info")).Append("\">")
                     .Append(H(lv)).Append("</span></td><td class=\"src\">").Append(H(e.Provider))
                     .Append("</td><td class=\"mono\">").Append(e.Id).Append("</td><td>")
                     .Append(H(desc));
                    if (detail.Length > 0) b.Append("<div class=\"msg\">").Append(H(detail)).Append("</div>");
                    b.Append("</td></tr>");
                    n++;
                }
                if (skipped > 0)
                {
                    b.Append("<tr><td colspan=\"5\" class=\"msg\">另有 ").Append(skipped)
                     .Append(" 条同类或较早的事件未逐条列出，完整内容见 10_系统日志 与 11_应用程序日志 中的 CSV。</td></tr>");
                }
                b.Append("</tbody></table>");
            }
            b.Append("</div>");

            // ---------- 采集结果 ----------
            b.Append("<div class=\"card\"><div class=\"card-t\">采集项执行结果</div><table class=\"tl\">");
            b.Append("<thead><tr><th style=\"width:260px\">采集项</th><th style=\"width:80px\">状态</th><th style=\"width:90px\">体积</th><th>说明</th></tr></thead><tbody>");
            foreach (JobResult r in results)
            {
                string cls = r.Status == JobStatus.Success ? "ok" : (r.Status == JobStatus.Failed ? "bad" : "warn");
                string txt = r.Status == JobStatus.Success ? "成功" : (r.Status == JobStatus.Failed ? "失败" : (r.Status == JobStatus.Skipped ? "跳过" : "未完成"));
                b.Append("<tr><td>").Append(H(r.Name)).Append("</td><td><span class=\"chip c-").Append(cls).Append("\">")
                 .Append(txt).Append("</span></td><td class=\"mono\">").Append(r.Bytes > 0 ? Tool.FmtSize(r.Bytes) : "-")
                 .Append("</td><td class=\"msg\">").Append(H(r.Message)).Append("</td></tr>");
            }
            b.Append("</tbody></table></div>");

            // ---------- 硬件信息 ----------
            b.Append("<div class=\"card\"><div class=\"card-t\">硬件与系统信息</div><table class=\"kv\">");
            Kv(b, "机型", s.Info.Display());
            Kv(b, "序列号", s.Info.Serial);
            Kv(b, "BIOS", s.Info.Bios + (s.Info.BiosDate.Length > 0 ? "（" + s.Info.BiosDate + "）" : ""));
            Kv(b, "主板", s.Info.Board);
            Kv(b, "CPU", s.Info.Cpu + (s.Info.CpuCores.Length > 0 ? " / " + s.Info.CpuCores : ""));
            Kv(b, "内存", s.Info.RamGB > 0 ? s.Info.RamGB + " GB" : "");
            string mem = ProbeJoin(s, "MEM.");
            if (mem.Length > 0) Kv(b, "内存条", mem);
            string gpu = ProbeJoin(s, "GPU.");
            if (gpu.Length > 0) Kv(b, "显卡", gpu);
            string disk = ProbeJoin(s, "DISK.");
            if (disk.Length > 0) Kv(b, "磁盘", disk);
            Kv(b, "操作系统", s.Info.OsName + " " + s.Info.OsVersion + " Build " + s.Info.OsBuild);
            Kv(b, "安装日期", s.Info.OsInstall);
            Kv(b, "计算机名", s.Info.ComputerName);
            b.Append("</table></div>");

            // ---------- 电池 ----------
            string bat = BatteryBlock(s);
            if (bat.Length > 0)
                b.Append("<div class=\"card\"><div class=\"card-t\">电池与电源</div>").Append(bat).Append("</div>");

            // ---------- 文件清单 ----------
            b.Append("<div class=\"card\"><div class=\"card-t\">采集文件清单</div><table class=\"tl\">");
            b.Append("<thead><tr><th style=\"width:300px\">目录 / 文件</th><th style=\"width:100px\">体积</th><th>说明</th></tr></thead><tbody>");
            try
            {
                foreach (string d in Directory.GetDirectories(s.OutDir))
                {
                    string name = Path.GetFileName(d);
                    b.Append("<tr><td class=\"mono\">").Append(H(name)).Append("/</td><td class=\"mono\">")
                     .Append(Tool.FmtSize(Tool.DirSize(d))).Append("</td><td class=\"msg\">")
                     .Append(H(Explain(name))).Append("</td></tr>");
                }
            }
            catch { }
            b.Append("</tbody></table></div>");

            // ---------- 页脚 ----------
            b.Append("<div class=\"ft\">本报告由笔记本日志采集工具 v" + H(Program.Version) + " 生成，内容为采集到的原始事实，不含推断性结论。");
            b.Append("原始文件位于同目录下，可用事件查看器直接打开 <code>System.evtx</code> 与 <code>Application.evtx</code>。");
            b.Append("报告可能包含个人信息，请按公司数据分级要求流转。</div>");
            b.Append("</div></body></html>");
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
            Kv(b, "电池型号", name);
            int d, f;
            if (int.TryParse(des, out d) && int.TryParse(full, out f) && d > 0)
            {
                int pct = (int)Math.Round(f * 100.0 / d);
                Kv(b, "设计容量 / 满充容量", d + " mWh / " + f + " mWh");
                Kv(b, "健康度", pct + "%" + (pct < 80 ? "（偏低，建议关注）" : ""));
            }
            Kv(b, "循环次数", cyc);
            Kv(b, "当前电量", charge.Length > 0 ? charge + "%" : "");
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
                list.Add(new Finding("bad", "检测到 " + bug + " 次蓝屏（BugCheck 记录），最近一次 "
                    + (last != null ? last.Time.ToString("MM-dd HH:mm") + "，停止代码 " + BugCode(last.Message) : "") +
                    "。请把 Minidump 目录下的转储文件一并提供给研发。"));
            }
            int shut = CountAny(s, new int[] { 41, 6008 });
            if (shut > 0)
                list.Add(new Finding("bad", "检测到 " + shut + " 次异常关机（断电、死机或长按电源），可能伴随数据丢失。"));
            int whea = CountAny(s, new int[] { 17, 18, 19, 47 });
            if (whea > 0)
                list.Add(new Finding("bad", "检测到 " + whea + " 条 WHEA 硬件错误记录，指向 CPU / 内存 / PCIe 的硬件层问题。"));
            int tdr = Count(s, 4101);
            if (tdr > 0)
                list.Add(new Finding("warn", "显示驱动超时恢复（TDR）" + tdr + " 次，表现为短暂黑屏、花屏或驱动重置。"));
            int drv = CountAny(s, new int[] { 219, 411 });
            if (drv > 0)
                list.Add(new Finding("warn", "驱动加载失败 / 设备未能启动 " + drv + " 次，请对照「驱动与设备」中的异常设备清单。"));
            int svc = CountAny(s, new int[] { 7000, 7001, 7031, 7034 });
            if (svc > 0)
                list.Add(new Finding("warn", "服务启动失败或意外终止 " + svc + " 次，可能影响开机速度与后台功能。"));
            int disk = CountAny(s, new int[] { 7, 11, 51, 129 });
            if (disk > 0)
                list.Add(new Finding("bad", "磁盘错误事件 " + disk + " 条（坏块 / 控制器错误 / 设备重置），建议立即检查存储健康。"));
            int th = Count(s, 37);
            if (th > 0)
                list.Add(new Finding("warn", "处理器性能受固件限制（降频）" + th + " 次，可能由温度或电源策略触发。"));
            int appCrash = CountApp(s);
            if (appCrash > 0)
                list.Add(new Finding("warn", "应用程序崩溃或未响应 " + appCrash + " 次（近 7 天），详情见 11_应用程序日志。"));
            int pnp = CountProbe(s, "PNP.");
            if (pnp > 0)
            {
                List<string> names = new List<string>();
                foreach (KeyValuePair<string, string> kv in s.Probe)
                {
                    if (kv.Key.StartsWith("PNP.") && names.Count < 5) names.Add(kv.Key.Substring(4));
                }
                list.Add(new Finding("warn", "发现 " + pnp + " 个设备存在驱动错误码：" + string.Join("、", names.ToArray()) + "。"));
            }
            int dumps = CountFiles(Path.Combine(s.OutDir, "30_崩溃转储"), "*.dmp", true);
            if (dumps > 0)
                list.Add(new Finding("info", "已收集 " + dumps + " 个内存转储文件，可用 WinDbg / BlueScreenView 分析。"));
            if (dumps == 0 && bug > 0)
                list.Add(new Finding("warn", "存在蓝屏记录但没有找到转储文件（可能转储功能被关闭或被清理），建议开启「内核内存转储」后复现。"));

            string des = Get(s, "BAT.DesignedCapacity");
            string full = Get(s, "BAT.FullChargedCapacity");
            int d, f;
            if (int.TryParse(des, out d) && int.TryParse(full, out f) && d > 0)
            {
                int pct = (int)Math.Round(f * 100.0 / d);
                if (pct < 80)
                    list.Add(new Finding("warn", "电池健康度 " + pct + "%（满充容量 " + f + " mWh / 设计容量 " + d + " mWh），低于 80% 时续航问题多与电池老化相关。"));
            }
            if (list.Count == 0)
                list.Add(new Finding("info", "未发现蓝屏、异常关机、硬件错误、驱动失败或磁盘告警等记录。"));
            return list;
        }

        private static string BugCode(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return "未知";
            Match m = Regex.Match(msg, "0x[0-9a-fA-F]{8,16}");
            if (m.Success) return m.Value.ToUpperInvariant();
            return "未知";
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
            if (dir.StartsWith("10")) return "系统事件日志原始文件与关键事件表";
            if (dir.StartsWith("11")) return "应用程序事件日志与崩溃记录";
            if (dir.StartsWith("20")) return "系统信息、硬件明细、系统摘要";
            if (dir.StartsWith("21")) return "驱动清单、驱动库、异常设备";
            if (dir.StartsWith("22")) return "进程、服务、启动项、计划任务快照";
            if (dir.StartsWith("23")) return "补丁与已安装软件";
            if (dir.StartsWith("24")) return "msinfo32 完整报告（可选）";
            if (dir.StartsWith("30")) return "蓝屏转储文件（供 WinDbg 分析）";
            if (dir.StartsWith("31")) return "Windows 错误报告（WER）";
            if (dir.StartsWith("32")) return "完整内存转储（体积较大）";
            if (dir.StartsWith("33")) return "进程内存转储（WinDbg 可直接打开）";
            if (dir.StartsWith("40")) return "电池健康报告";
            if (dir.StartsWith("41")) return "电源方案、睡眠能力、唤醒源";
            if (dir.StartsWith("42")) return "能耗诊断（60 秒审计）";
            if (dir.StartsWith("43")) return "现代待机（S0ix）诊断";
            if (dir.StartsWith("50")) return "CPU / 内存 / 磁盘性能采样";
            if (dir.StartsWith("60")) return "dxdiag 与显卡驱动信息";
            if (dir.StartsWith("70")) return "网络配置、无线接口与驱动";
            if (dir.StartsWith("71")) return "无线连接历史时间线";
            if (dir.StartsWith("80")) return "磁盘与 SMART 健康";
            if (dir.StartsWith("90")) return "用户指定的附加日志";
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
