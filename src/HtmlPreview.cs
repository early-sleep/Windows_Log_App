using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LogCollector
{
    /// <summary>
    /// Renders collected text files as HTML: one browse.html per folder, one All_Content.html at the root.
    /// For human reading only; does not replace the raw txt files.
    /// </summary>
    public static class HtmlPreview
    {
        private const long MaxInline = 200 * 1024;      // inline at most 200 KB per file
        private const long MaxFile = 20 * 1024 * 1024;  // skip text files larger than 20 MB
        private static readonly string[] TextExt = new string[] {
            ".txt", ".csv", ".log", ".xml", ".json", ".md", ".ini", ".htm", ".html"
        };

        private static bool IsText(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".html" || ext == ".htm") return false;   // already viewable as-is
            foreach (string e in TextExt) if (e == ext) return true;
            return false;
        }

        public static void BuildAll(Session s)
        {
            List<string> dirs = new List<string>();
            try
            {
                foreach (string d in Directory.GetDirectories(s.OutDir)) dirs.Add(d);
            }
            catch { }
            dirs.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string d in dirs)
            {
                if (s.Token.IsCancellationRequested) return;
                try { WriteFolderPage(d); } catch { }
            }
            try { WriteAllPage(s, dirs); } catch { }
        }

        // ---------------------------------------------------------------- single folder
        private static void WriteFolderPage(string dir)
        {
            List<FileInfo> files = Collect(dir);
            if (files.Count == 0) return;

            StringBuilder b = new StringBuilder();
            Head(b, Path.GetFileName(dir), "../All_Content.html", "Back to overview");
            b.Append("<div class=\"hd\"><h1>").Append(E(Path.GetFileName(dir))).Append("</h1>");
            b.Append("<div class=\"sub\">").Append(files.Count).Append(" files, ")
             .Append(E(Tool.FmtSize(Sum(files)))).Append("</div></div>");
            foreach (FileInfo f in files) Section(b, f);
            Foot(b);

            File.WriteAllText(Path.Combine(dir, "browse.html"), b.ToString(), new UTF8Encoding(false));
        }

        // ---------------------------------------------------------------- everything
        private static void WriteAllPage(Session s, List<string> dirs)
        {
            StringBuilder b = new StringBuilder();
            Head(b, "All content", null, null);

            b.Append("<div class=\"layout\"><nav><div class=\"navt\">Contents</div><ul>");
            b.Append("<li><a href=\"report.html\">◂ Back to report</a></li>");
            List<string> names = new List<string>();
            foreach (string d in dirs)
            {
                List<FileInfo> fs = Collect(d);
                if (fs.Count == 0) continue;
                string n = Path.GetFileName(d);
                names.Add(n);
                b.Append("<li><a href=\"#d").Append(names.Count - 1).Append("\">").Append(E(n)).Append("</a></li>");
            }
            b.Append("</ul></nav><main>");

            b.Append("<div class=\"hd\"><h1>All collected content</h1><div class=\"sub\">")
             .Append(E(s.Info.Display())).Append(" · ").Append(E(s.Info.OsName)).Append(" ")
             .Append(E(s.Info.OsVersion)).Append(" · collected at ")
             .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("</div></div>");

            int idx = 0;
            foreach (string d in dirs)
            {
                List<FileInfo> fs = Collect(d);
                if (fs.Count == 0) continue;
                string n = Path.GetFileName(d);
                b.Append("<section id=\"d").Append(idx).Append("\"><h2>").Append(E(n));
                b.Append("<span class=\"cnt\">").Append(fs.Count).Append(" files / ")
                 .Append(E(Tool.FmtSize(Sum(fs)))).Append("</span>");
                b.Append("<a class=\"lnk\" href=\"").Append(Uri.EscapeDataString(n)).Append("/browse.html\">Open alone</a>");
                b.Append("</h2>");
                foreach (FileInfo f in fs) Section(b, f);
                b.Append("</section>");
                idx++;
            }
            b.Append("</main></div>");
            Foot(b);

            File.WriteAllText(Path.Combine(s.OutDir, "All_Content.html"), b.ToString(), new UTF8Encoding(false));
        }

        // ---------------------------------------------------------------- components
        private static List<FileInfo> Collect(string dir)
        {
            List<FileInfo> list = new List<FileInfo>();
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (!IsText(f)) continue;
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        if (fi.Length > MaxFile) continue;
                        if (string.Equals(fi.Name, "browse.html", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(fi);
                    }
                    catch { }
                }
            }
            catch { }
            list.Sort(delegate(FileInfo a, FileInfo b) { return string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        private static long Sum(List<FileInfo> files)
        {
            long t = 0;
            foreach (FileInfo f in files) t += f.Length;
            return t;
        }

        private static void Section(StringBuilder b, FileInfo f)
        {
            string text = "";
            bool truncated = false;
            try
            {
                byte[] buf;
                using (FileStream fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int want = (int)Math.Min(f.Length, MaxInline);
                    buf = new byte[want];
                    int read = 0;
                    while (read < want)
                    {
                        int n = fs.Read(buf, read, want - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    text = Decode(buf, read);
                    truncated = f.Length > MaxInline;
                }
            }
            catch (Exception ex) { text = "(Cannot read: " + ex.Message + ")"; }

            int lines = 0;
            foreach (char c in text) if (c == '\n') lines++;
            bool openAttr = f.Length <= 20 * 1024 && lines <= 300;

            b.Append("<details class=\"f\"").Append(openAttr ? " open" : "").Append(">");
            b.Append("<summary><span class=\"fn\">").Append(E(f.Name)).Append("</span>");
            b.Append("<span class=\"meta\">").Append(E(Tool.FmtSize(f.Length))).Append(" · ")
             .Append(lines).Append(" lines</span></summary>");
            b.Append("<pre>").Append(E(text)).Append("</pre>");
            if (truncated)
                b.Append("<div class=\"cut\">File is large; only the first 200 KB is shown. Open the original file for the full content.</div>");
            b.Append("</details>");
        }

        /// <summary>
        /// Detect encoding: BOM first, then strict UTF-8, falling back to GBK.
        /// Redirected output of system tools (systeminfo / powercfg) is GBK; without the fallback Chinese text is garbled.
        /// </summary>
        private static string Decode(byte[] buf, int len)
        {
            if (len <= 0) return "";
            if (len >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
                return Encoding.UTF8.GetString(buf, 3, len - 3);
            if (len >= 2 && buf[0] == 0xFF && buf[1] == 0xFE)
                return Encoding.Unicode.GetString(buf, 2, len - 2);
            if (len >= 2 && buf[0] == 0xFE && buf[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(buf, 2, len - 2);
            try
            {
                return new UTF8Encoding(false, true).GetString(buf, 0, len);
            }
            catch
            {
                try { return Encoding.GetEncoding(936).GetString(buf, 0, len); }
                catch { return Encoding.Default.GetString(buf, 0, len); }
            }
        }

        private static string E(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static void Head(StringBuilder b, string title, string backHref, string backText)
        {
            b.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">");
            b.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            b.Append("<title>").Append(E(title)).Append("</title><style>");
            b.Append(Css);
            b.Append("</style></head><body>");
            if (backHref != null)
                b.Append("<div class=\"top\"><a href=\"").Append(backHref).Append("\">◂ ")
                 .Append(E(backText)).Append("</a></div>");
        }

        private static void Foot(StringBuilder b)
        {
            b.Append("<div class=\"ft\">Generated by Laptop Log Collector v").Append(E(Program.Version))
             .Append(" · for reading only; the original files are authoritative</div></body></html>");
        }

        private const string Css = @"
*{box-sizing:border-box}
body{margin:0;background:#f5f6f9;color:#1f2937;font:14px/1.6 'Microsoft YaHei',system-ui,sans-serif}
a{color:#2e5395;text-decoration:none}
a:hover{text-decoration:underline}
.top{padding:10px 24px;background:#fff;border-bottom:1px solid #e3e7ee;font-size:13px}
.layout{display:flex;align-items:flex-start}
nav{width:250px;flex:0 0 250px;position:sticky;top:0;max-height:100vh;overflow:auto;background:#fff;border-right:1px solid #e3e7ee;padding:16px 12px}
.navt{font-size:12px;color:#6b7280;letter-spacing:.1em;margin-bottom:8px}
nav ul{list-style:none;margin:0;padding:0}
nav li{margin:2px 0}
nav a{display:block;padding:5px 8px;border-radius:5px;font-size:13px;color:#374151;word-break:break-all}
nav a:hover{background:#eef3fb;text-decoration:none}
main{flex:1;min-width:0;padding:20px 26px 60px}
.hd{border-bottom:2px solid #1f3864;padding-bottom:12px;margin-bottom:18px}
h1{margin:0 0 4px;font-size:22px;color:#1f3864}
.sub{color:#6b7280;font-size:13px}
h2{font-size:16px;color:#1f3864;margin:26px 0 10px;padding-left:10px;border-left:4px solid #2e5395}
.cnt{font-size:12px;color:#9ca3af;font-weight:400;margin-left:10px}
.lnk{font-size:12px;font-weight:400;margin-left:10px}
details.f{background:#fff;border:1px solid #e3e7ee;border-radius:8px;margin-bottom:8px;overflow:hidden}
details.f summary{cursor:pointer;padding:9px 12px;display:flex;justify-content:space-between;gap:12px;list-style:none}
details.f summary::-webkit-details-marker{display:none}
details.f summary:hover{background:#f7f9fc}
.fn{font-weight:600;color:#1f3864;font-size:13px;word-break:break-all}
.meta{color:#9ca3af;font-size:12px;white-space:nowrap}
pre{margin:0;padding:12px;background:#fbfcfe;border-top:1px solid #eef1f6;font:12px/1.55 Consolas,'Courier New',monospace;color:#334155;white-space:pre;overflow:auto;max-height:640px}
.cut{padding:8px 12px;background:#fffbeb;color:#92400e;font-size:12px;border-top:1px solid #fde68a}
.ft{margin-top:30px;padding-top:12px;border-top:1px solid #e3e7ee;color:#9ca3af;font-size:12px}
@media(max-width:900px){.layout{display:block}nav{width:auto;position:static;max-height:none}}
";
    }
}
