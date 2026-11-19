using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace LogCollector
{
    // ================= 日志分析（从 BSOD_analyse 合并进来） =================
    public class Package
    {
        public string Name = "";
        public string Path = "";
        public DateTime Time;
        public override string ToString() { return Name; }
    }

    public class FileItem
    {
        public string Rel = "";
        public string Name = "";
        public long Size;
        public string Kind = "text";
    }

    public static class Logs
    {
        public static List<Package> Scan(string root)
        {
            List<Package> list = new List<Package>();
            if (!Directory.Exists(root)) return list;
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { return list; }
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            foreach (string d in dirs)
            {
                if (!File.Exists(System.IO.Path.Combine(d, "report.html")) &&
                    !File.Exists(System.IO.Path.Combine(d, "Collection_Info.txt"))) continue;
                Package p = new Package();
                p.Path = d;
                p.Name = System.IO.Path.GetFileName(d);
                try { p.Time = Directory.GetLastWriteTime(d); } catch { }
                list.Add(p);
            }
            return list;
        }

        public static List<FileItem> Tree(string pkg)
        {
            List<FileItem> list = new List<FileItem>();
            if (!Directory.Exists(pkg)) return list;
            int n = 0;
            foreach (string f in Directory.GetFiles(pkg, "*", SearchOption.AllDirectories))
            {
                if (n++ > 6000) break;
                string rel = f.Substring(pkg.Length).TrimStart('\\');
                if (rel.EndsWith("browse.html", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.EndsWith("All_Content.html", StringComparison.OrdinalIgnoreCase)) continue;
                FileItem it = new FileItem();
                it.Rel = rel;
                it.Name = System.IO.Path.GetFileName(f);
                try { it.Size = new FileInfo(f).Length; } catch { }
                it.Kind = Kind(System.IO.Path.GetExtension(f));
                list.Add(it);
            }
            list.Sort(delegate(FileItem a, FileItem b) { return string.Compare(a.Rel, b.Rel, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        private static string Kind(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".txt": case ".csv": case ".log": case ".json": case ".md": case ".ini": case ".xml":
                case ".xlsx":
                    return "text";
                case ".html": case ".htm":
                    return "html";
                case ".evtx": case ".dmp": case ".etl": case ".zip": case ".png": case ".jpg":
                    return "binary";
                default:
                    return "other";
            }
        }

        public static string ReadText(string full, int cap)
        {
            if (full.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                return ReadXlsx(full, cap);

            byte[] buf;
            using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int want = (int)Math.Min(fs.Length, cap);
                buf = new byte[want];
                int read = 0, n;
                while (read < want && (n = fs.Read(buf, read, want - read)) > 0) read += n;
                if (read < want) Array.Resize(ref buf, read);
            }
            return Decode(buf);
        }

        /// <summary>把 xlsx 的 sheet1 解析成行（每行是若干单元格文本）。</summary>
        public static List<string[]> ReadXlsxRows(string path)
        {
            List<string[]> rows = new List<string[]>();
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (System.IO.Compression.ZipArchive zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
            {
                System.IO.Compression.ZipArchiveEntry entry = zip.GetEntry("xl/worksheets/sheet1.xml");
                if (entry == null) return rows;

                using (Stream s = entry.Open())
                using (System.Xml.XmlReader r = System.Xml.XmlReader.Create(s))
                {
                    List<string> cur = null;
                    while (r.Read())
                    {
                        if (r.NodeType == System.Xml.XmlNodeType.Element && r.Name == "row")
                        {
                            cur = new List<string>();
                        }
                        else if (r.NodeType == System.Xml.XmlNodeType.Element && r.Name == "t")
                        {
                            string v = r.ReadElementContentAsString();
                            if (cur != null) cur.Add(v);
                        }
                        else if (r.NodeType == System.Xml.XmlNodeType.EndElement && r.Name == "row" && cur != null)
                        {
                            rows.Add(cur.ToArray());
                            cur = null;
                        }
                    }
                }
            }
            return rows;
        }

        /// <summary>
        /// 事件表的“重点摘要”：只挑被标红（41 / 6008 / 1001）和被标黄（506 / 507）的事件，
        /// 每条带上前后若干行上下文，给 AI 用来推断原因。
        /// </summary>
        public static string ReadEventDigest(string path, int cap)
        {
            List<string[]> rows;
            try { rows = ReadXlsxRows(path); }
            catch (Exception ex) { return "[cannot read event table] " + ex.Message; }
            if (rows.Count < 2) return "";

            string[] head = rows[0];
            int idCol = -1, timeCol = 0;
            for (int i = 0; i < head.Length; i++)
            {
                if (head[i] == "Event ID") idCol = i;
                if (head[i] == "Time" || head[i] == "TimeCreated") timeCol = i;
            }
            if (idCol < 0) idCol = 1;

            int[] redIds = new int[] { 41, 6008, 1001 };
            int[] yellowIds = new int[] { 506, 507 };

            List<int> red = new List<int>();
            List<int> yellow = new List<int>();
            for (int i = 1; i < rows.Count; i++)
            {
                int id;
                if (!int.TryParse(CellOf(rows[i], idCol), out id)) continue;
                if (Array.IndexOf(redIds, id) >= 0) red.Add(i);
                else if (Array.IndexOf(yellowIds, id) >= 0) yellow.Add(i);
            }

            StringBuilder b = new StringBuilder();
            b.Append("rows: ").Append(rows.Count - 1)
             .Append("   red (41/6008/1001): ").Append(red.Count)
             .Append("   yellow (506/507): ").Append(yellow.Count).Append("\n");

            // 先列出所有被标记的事件，保证即使后面上下文被截断，模型也知道有哪些
            b.Append("\nMARKED EVENTS\n");
            AppendMarked(b, rows, red, "RED", cap);
            AppendMarked(b, rows, yellow, "YELLOW", cap);

            // 再给每条的上下文窗口
            List<int> all = new List<int>();
            all.AddRange(red);
            all.AddRange(yellow);
            all.Sort();

            const int Before = 12, After = 6;
            b.Append("\nCONTEXT WINDOWS (12 rows before, 6 after each marked event)\n");
            int lastEmitted = -1;
            foreach (int idx in all)
            {
                if (b.Length > cap) { b.Append("\n...[digest truncated at ").Append(cap).Append(" chars]\n"); break; }
                int from = Math.Max(1, idx - Before);
                int to = Math.Min(rows.Count - 1, idx + After);
                if (from <= lastEmitted) from = lastEmitted + 1;    // 窗口重叠时不重复
                if (from > to) continue;

                b.Append("\n--- around ").Append(CellOf(rows[idx], timeCol))
                 .Append("  Event ").Append(CellOf(rows[idx], idCol)).Append(" ---\n");
                for (int r = from; r <= to; r++)
                {
                    b.Append(r == idx ? ">> " : "   ");
                    b.Append(LineOf(rows[r], head, 220)).Append('\n');
                }
                lastEmitted = to;
            }

            if (red.Count == 0 && yellow.Count == 0)
                b.Append("\nNo red or yellow event in this table.\n");

            string text = b.ToString();
            if (text.Length > cap) text = text.Substring(0, cap) + "\n...[truncated]";
            return text;
        }

        private static void AppendMarked(StringBuilder b, List<string[]> rows, List<int> idx, string tag, int cap)
        {
            foreach (int i in idx)
            {
                if (b.Length > cap / 2) { b.Append("   ... (more ").Append(tag).Append(" events omitted)\n"); return; }
                b.Append(tag).Append("  ").Append(LineOf(rows[i], null, 160)).Append('\n');
            }
        }

        private static string CellOf(string[] row, int col)
        {
            if (row == null || col < 0 || col >= row.Length) return "";
            return row[col] ?? "";
        }

        /// <summary>一行拼成 "时间 | id | 级别 | 来源 | 消息" 这样的可读形式。</summary>
        private static string LineOf(string[] row, string[] head, int clip)
        {
            string time = CellOf(row, 0), id = CellOf(row, 1), level = CellOf(row, 2);
            string prov = CellOf(row, 3);
            string msg = row.Length > 0 ? row[row.Length - 1] : "";
            if (msg.Length > clip) msg = msg.Substring(0, clip) + "…";
            return time + " | " + id + " | " + level + " | " + prov + " | " + msg;
        }

        /// <summary>
        /// 把采集包里的 .xlsx 事件表读成“一行一条、制表符分隔”的文本，
        /// 预览和 AI 都能直接用（CSV 已经不再生成）。
        /// </summary>
        private static string ReadXlsx(string path, int cap)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (System.IO.Compression.ZipArchive zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read))
                {
                    System.IO.Compression.ZipArchiveEntry entry = zip.GetEntry("xl/worksheets/sheet1.xml");
                    if (entry == null) return "[cannot read xlsx: sheet1.xml missing]";

                    using (Stream s = entry.Open())
                    using (System.Xml.XmlReader r = System.Xml.XmlReader.Create(s))
                    {
                        int cells = 0;
                        bool firstRow = true;
                        while (r.Read() && sb.Length < cap)
                        {
                            if (r.NodeType == System.Xml.XmlNodeType.Element && r.Name == "row")
                            {
                                if (!firstRow) sb.Append('\n');
                                firstRow = false;
                                cells = 0;
                            }
                            else if (r.NodeType == System.Xml.XmlNodeType.Element && r.Name == "t")
                            {
                                string v = r.ReadElementContentAsString();
                                if (cells > 0) sb.Append('\t');
                                sb.Append(v);
                                cells++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return "[cannot read xlsx] " + ex.Message;
            }
            string text = sb.ToString();
            if (text.Length > cap) text = text.Substring(0, cap) + "\r\n...[truncated]";
            return text;
        }

        private static string Decode(byte[] buf)
        {
            if (buf == null || buf.Length == 0) return "";
            int len = buf.Length;
            if (len >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF) return Encoding.UTF8.GetString(buf, 3, len - 3);
            if (len >= 2 && buf[0] == 0xFF && buf[1] == 0xFE) return Encoding.Unicode.GetString(buf, 2, len - 2);
            if (len >= 2 && buf[0] == 0xFE && buf[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(buf, 2, len - 2);
            try { return new UTF8Encoding(false, true).GetString(buf); }
            catch
            {
                try { return Encoding.GetEncoding(936).GetString(buf); }
                catch { return Encoding.Default.GetString(buf); }
            }
        }

        public static string Size(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1048576) return (b / 1024.0).ToString("0.0") + " KB";
            if (b < 1073741824L) return (b / 1048576.0).ToString("0.0") + " MB";
            return (b / 1073741824.0).ToString("0.00") + " GB";
        }
    }

    // ------------------------------------------------------------------ 设置
    public class Settings
    {
        public string Endpoint = "https://api.deepseek.com/v1";
        public string Key = "";
        public string Model = "deepseek-chat";
        public int Per = 2000000;
        public int Total = 15000000;
        public string Root = "";

        private static string FilePath()
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BsodAnalyzer");
            Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "settings.json");
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                string f = FilePath();
                if (!File.Exists(f)) return s;
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
                object v;
                if (d.TryGetValue("endpoint", out v)) s.Endpoint = Convert.ToString(v);
                if (d.TryGetValue("model", out v)) s.Model = Convert.ToString(v);
                if (d.TryGetValue("per", out v)) s.Per = Convert.ToInt32(v);
                if (d.TryGetValue("total", out v)) s.Total = Convert.ToInt32(v);
                if (d.TryGetValue("root", out v)) s.Root = Convert.ToString(v);
                if (d.TryGetValue("keyProtected", out v))
                {
                    string b64 = Convert.ToString(v);
                    byte[] enc = Convert.FromBase64String(b64);
                    byte[] raw = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
                    s.Key = Encoding.UTF8.GetString(raw);
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["endpoint"] = Endpoint;
                d["model"] = Model;
                d["per"] = Per;
                d["total"] = Total;
                d["root"] = Root;
                if (!string.IsNullOrEmpty(Key))
                {
                    byte[] raw = Encoding.UTF8.GetBytes(Key);
                    byte[] enc = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
                    d["keyProtected"] = Convert.ToBase64String(enc);
                }
                File.WriteAllText(FilePath(), js.Serialize(d), Encoding.UTF8);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ AI
    public static class Ai
    {
        public static string Ask(Settings s, string system, string user, out string error)
        {
            error = null;
            string url = (s.Endpoint ?? "").TrimEnd('/');
            if (url.Length == 0) { error = "Endpoint is empty."; return null; }
            if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) url += "/chat/completions";

            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["model"] = s.Model;
            payload["temperature"] = 0.2;
            payload["stream"] = false;
            List<object> msgs = new List<object>();
            if (!string.IsNullOrEmpty(system)) msgs.Add(new Dictionary<string, object> { { "role", "system" }, { "content", system } });
            msgs.Add(new Dictionary<string, object> { { "role", "user" }, { "content", user } });
            payload["messages"] = msgs;

            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Method = "POST";
                r.ContentType = "application/json";
                r.Headers["Authorization"] = "Bearer " + s.Key;
                r.Timeout = 300000;
                r.ReadWriteTimeout = 300000;
                byte[] data = Encoding.UTF8.GetBytes(js.Serialize(payload));
                r.ContentLength = data.Length;
                using (Stream st = r.GetRequestStream()) st.Write(data, 0, data.Length);
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return Extract(sr.ReadToEnd());
                }
            }
            catch (WebException wex)
            {
                string detail = wex.Message, status = "";
                try
                {
                    HttpWebResponse r2 = wex.Response as HttpWebResponse;
                    if (r2 != null)
                    {
                        status = "HTTP " + (int)r2.StatusCode + ": ";
                        using (StreamReader sr = new StreamReader(r2.GetResponseStream(), Encoding.UTF8))
                            detail = sr.ReadToEnd();
                    }
                }
                catch { }
                error = "API call failed. " + status + Friendly(detail);
                return null;
            }
            catch (Exception ex)
            {
                error = "API call failed: " + ex.Message;
                return null;
            }
        }

        private static string Friendly(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "no response";
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 16 * 1024 * 1024;
                object root = js.DeserializeObject(raw);
                object m = GetPath(root, "error", "message");
                if (m != null) return Convert.ToString(m);
                object e = GetPath(root, "error");
                if (e != null) return Convert.ToString(e);
            }
            catch { }
            return raw.Length > 400 ? raw.Substring(0, 400) : raw;
        }

        private static string Extract(string raw)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            object root = js.DeserializeObject(raw);
            object content = GetPath(root, "choices", 0, "message", "content");
            string text = content == null ? "" : Convert.ToString(content);
            if (text.Trim().Length == 0)
            {
                object reasoning = GetPath(root, "choices", 0, "message", "reasoning_content");
                if (reasoning != null) text = Convert.ToString(reasoning);
            }
            if (text.Trim().Length == 0)
                throw new Exception("The API returned no text. Raw: " + Friendly(raw));
            return text;
        }

        private static object GetPath(object node, params object[] path)
        {
            foreach (object step in path)
            {
                if (node == null) return null;
                IDictionary<string, object> dict = node as IDictionary<string, object>;
                string key = step as string;
                if (dict != null && key != null)
                {
                    object next;
                    dict.TryGetValue(key, out next);
                    node = next;
                    continue;
                }
                System.Collections.IList list = node as System.Collections.IList;
                if (list != null && step is int)
                {
                    int i = (int)step;
                    node = (i >= 0 && i < list.Count) ? list[i] : null;
                    continue;
                }
                return null;
            }
            return node;
        }
    }
}
