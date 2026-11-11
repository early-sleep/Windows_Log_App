using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;

namespace LogAnalyzer
{
    internal class ServerForm : Form
    {
        public ServerForm(string url, string root)
        {
            Text = "Log Analyzer";
            ClientSize = new Size(482, 214);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.White;

            Label title = new Label();
            title.Text = "Log Analyzer is running";
            title.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(31, 56, 100);
            title.AutoSize = true;
            title.Location = new Point(18, 14);
            Controls.Add(title);

            Label l1 = new Label();
            l1.Text = "Address for your browser:";
            l1.AutoSize = true;
            l1.ForeColor = Color.Gray;
            l1.Location = new Point(20, 50);
            Controls.Add(l1);

            TextBox box = new TextBox();
            box.Text = url;
            box.ReadOnly = true;
            box.Location = new Point(20, 70);
            box.Width = 440;
            Controls.Add(box);

            Label l2 = new Label();
            l2.Text = "Packages: " + root;
            l2.AutoSize = false;
            l2.Size = new Size(440, 18);
            l2.ForeColor = Color.Gray;
            l2.Location = new Point(20, 102);
            Controls.Add(l2);

            Button open = new Button();
            open.Text = "Open browser";
            open.Location = new Point(20, 134);
            open.Size = new Size(140, 34);
            open.Click += delegate { try { Process.Start(url); } catch { } };
            Controls.Add(open);

            Button stop = new Button();
            stop.Text = "Stop and exit";
            stop.Location = new Point(172, 134);
            stop.Size = new Size(140, 34);
            stop.Click += delegate { Close(); };
            Controls.Add(stop);

            Label hint = new Label();
            hint.Text = "Keep this window open while you use the page. Closing it stops the server.";
            hint.AutoSize = true;
            hint.ForeColor = Color.Gray;
            hint.Location = new Point(20, 180);
            Controls.Add(hint);

            FormClosing += delegate { Program.LogLine("window closed, exiting"); };
        }
    }

    internal static class Program
    {
        private const string Version = "1.5";
        private static string _root;
        private static int _port;

        [STAThread]
        private static void Main(string[] args)
        {
            string root = args.Length > 0 && Directory.Exists(args[0])
                ? args[0]
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "LogCollection");
            _root = root;

            int start = 8787;
            for (int i = 0; i < 40; i++)
            {
                int p = start + i;
                if (!PortFree(p)) continue;
                if (Start(p)) { _port = p; break; }
            }
            if (_port == 0)
            {
                MessageBox.Show("Could not bind a local port (8787-8826 are all in use).",
                                "Log Analyzer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string url = "http://127.0.0.1:" + _port + "/";
            LogLine("started v" + Version + " on " + url + "  root=" + _root);
            try { Process.Start(url); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ServerForm(url, _root));
        }

        private static void PickFolderEndpoint(HttpListenerContext ctx)
        {
            string picked = PickFolder();
            Json(ctx, picked == null ? "{\"path\":null}" : "{\"path\":" + J(picked) + "}");
        }

        /// <summary>在桌面上弹一个文件夹选择框（对话框必须在 UI 线程上创建）。</summary>
        private static string PickFolder()
        {
            try
            {
                if (Application.OpenForms.Count == 0) return null;
                Form owner = Application.OpenForms[0];
                string result = null;
                ManualResetEvent done = new ManualResetEvent(false);

                owner.BeginInvoke(new Action(delegate()
                {
                    try
                    {
                        if (owner.WindowState == FormWindowState.Minimized)
                            owner.WindowState = FormWindowState.Normal;
                        owner.Activate();
                        using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                        {
                            dlg.Description = "Select the folder that contains the collected packages";
                            dlg.ShowNewFolderButton = false;
                            if (Directory.Exists(_root)) dlg.SelectedPath = _root;
                            if (dlg.ShowDialog(owner) == DialogResult.OK) result = dlg.SelectedPath;
                        }
                    }
                    catch { }
                    finally { done.Set(); }
                }));

                if (!done.WaitOne(TimeSpan.FromMinutes(10))) return null;
                return result;
            }
            catch { return null; }
        }

        private static void BeginShutdown()
        {
            LogLine("shutdown requested");
            Thread t = new Thread(delegate()
            {
                Thread.Sleep(300);
                Environment.Exit(0);
            });
            t.IsBackground = true;
            t.Start();
        }

        private static readonly object _logLock = new object();
        internal static void LogLine(string msg)
        {
            try
            {
                lock (_logLock)
                {
                    string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "analyzer.log");
                    if (File.Exists(f) && new FileInfo(f).Length > 2 * 1024 * 1024) File.Delete(f);
                    File.AppendAllText(f, DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\r\n", Encoding.UTF8);
                }
            }
            catch { }
        }

        private static bool PortFree(int port)
        {
            try
            {
                TcpListener l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch { return false; }
        }

        private static bool Start(int port)
        {
            HttpListener l = new HttpListener();
            l.Prefixes.Add("http://127.0.0.1:" + port + "/");
            try { l.Start(); } catch { return false; }
            Thread t = new Thread(delegate()
            {
                while (true)
                {
                    HttpListenerContext ctx = null;
                    try { ctx = l.GetContext(); }
                    catch (Exception ex)
                    {
                        // 客户端中断、监听器抖动都不要让服务退出
                        LogLine("accept error: " + ex.Message);
                        if (!l.IsListening) return;
                        Thread.Sleep(50);
                        continue;
                    }
                    HttpListenerContext captured = ctx;
                    ThreadPool.QueueUserWorkItem(delegate(object st)
                    {
                        try { Handle(captured); }
                        catch (Exception ex) { TryError(captured, ex.Message); }
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
            return true;
        }

        // ------------------------------------------------------------------ routing
        private static void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
            if (path.StartsWith("/api/")) LogLine(ctx.Request.HttpMethod + " " + ctx.Request.RawUrl);
            if (!Local(ctx)) { ctx.Response.StatusCode = 403; ctx.Response.Close(); return; }

            if (path == "/" || path == "/index.html") { ServeFile(ctx, "analyzer.html", "text/html; charset=utf-8"); return; }
            if (path == "/api/health" || path == "/api/ping") { Json(ctx, "{\"ok\":true,\"version\":\"" + Version + "\"}"); return; }
            if (path == "/api/shutdown") { Json(ctx, "{\"ok\":true}"); BeginShutdown(); return; }
            if (path == "/api/pickfolder") { PickFolderEndpoint(ctx); return; }
            if (path == "/api/packages") { Packages(ctx); return; }
            if (path == "/api/tree") { Tree(ctx); return; }
            if (path == "/api/file") { FileText(ctx); return; }
            if (path == "/api/chat") { Chat(ctx); return; }
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }

        /// <summary>只接受本机、且 Origin 为空或同源的请求，避免其它网页借用这个服务。</summary>
        private static bool Local(HttpListenerContext ctx)
        {
            try
            {
                string origin = ctx.Request.Headers["Origin"];
                if (!string.IsNullOrEmpty(origin) &&
                    origin.IndexOf("127.0.0.1:" + _port, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
                return true;
            }
            catch { return false; }
        }

        private static void ServeFile(HttpListenerContext ctx, string name, string type)
        {
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            if (!File.Exists(file)) { Send(ctx, 404, "text/plain", "analyzer.html not found next to the exe"); return; }
            Send(ctx, 200, type, File.ReadAllText(file, Encoding.UTF8));
        }

        private static void Packages(HttpListenerContext ctx)
        {
            string root = ctx.Request.QueryString["root"];
            if (string.IsNullOrEmpty(root)) root = _root;
            if (!Directory.Exists(root)) { Json(ctx, "{\"root\":" + J(root) + ",\"packages\":[]}"); return; }

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"root\":").Append(J(root)).Append(",\"packages\":[");
            bool first = true;
            try
            {
                string[] dirs = Directory.GetDirectories(root);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                foreach (string d in dirs)
                {
                    if (!File.Exists(Path.Combine(d, "report.html")) &&
                        !File.Exists(Path.Combine(d, "Collection_Info.txt"))) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"name\":").Append(J(Path.GetFileName(d)))
                      .Append(",\"path\":").Append(J(d))
                      .Append(",\"time\":").Append(J(Directory.GetLastWriteTime(d).ToString("yyyy-MM-dd HH:mm")));
                    sb.Append('}');
                }
            }
            catch (Exception ex)
            {
                sb.Append("],\"error\":").Append(J(ex.Message)).Append('}');
                Json(ctx, sb.ToString());
                return;
            }
            sb.Append("]}");
            Json(ctx, sb.ToString());
        }

        private static void Tree(HttpListenerContext ctx)
        {
            string pkg = ctx.Request.QueryString["path"];
            if (string.IsNullOrEmpty(pkg) || !Directory.Exists(pkg)) { Json(ctx, "{\"files\":[],\"error\":\"package not found\"}"); return; }

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"path\":").Append(J(pkg)).Append(",\"files\":[");
            bool first = true;
            int count = 0;
            try
            {
                foreach (string f in Directory.GetFiles(pkg, "*", SearchOption.AllDirectories))
                {
                    if (count++ > 5000) break;
                    string rel = f.Substring(pkg.Length).TrimStart('\\');
                    if (rel.EndsWith("browse.html", StringComparison.OrdinalIgnoreCase)) continue;
                    if (rel.EndsWith("All_Content.html", StringComparison.OrdinalIgnoreCase)) continue;
                    FileInfo fi = new FileInfo(f);
                    string kind = TextKind(fi.Extension);
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"rel\":").Append(J(rel))
                      .Append(",\"name\":").Append(J(fi.Name))
                      .Append(",\"size\":").Append(fi.Length)
                      .Append(",\"kind\":").Append(J(kind)).Append('}');
                }
            }
            catch (Exception ex) { sb.Append("],\"error\":").Append(J(ex.Message)).Append('}'); Json(ctx, sb.ToString()); return; }
            sb.Append("]}");
            Json(ctx, sb.ToString());
        }

        private static string TextKind(string ext)
        {
            ext = (ext ?? "").ToLowerInvariant();
            switch (ext)
            {
                case ".txt": case ".csv": case ".log": case ".json": case ".md": case ".ini": case ".xml":
                    return "text";
                case ".html": case ".htm":
                    return "html";
                case ".evtx": case ".dmp": case ".etl": case ".zip": case ".png": case ".jpg":
                    return "binary";
                default:
                    return "other";
            }
        }

        private static void FileText(HttpListenerContext ctx)
        {
            string pkg = ctx.Request.QueryString["path"];
            string rel = ctx.Request.QueryString["rel"];
            if (string.IsNullOrEmpty(pkg) || string.IsNullOrEmpty(rel)) { Json(ctx, "{\"error\":\"missing parameters\"}"); return; }

            string full = Path.GetFullPath(Path.Combine(pkg, rel));
            if (!full.StartsWith(Path.GetFullPath(pkg), StringComparison.OrdinalIgnoreCase))
            {
                Json(ctx, "{\"error\":\"path outside package\"}");
                return;
            }
            if (!File.Exists(full)) { Json(ctx, "{\"error\":\"file not found\"}"); return; }

            const int Cap = 512 * 1024;
            byte[] buf;
            using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int want = (int)Math.Min(fs.Length, Cap);
                buf = new byte[want];
                int read = 0, n;
                while (read < want && (n = fs.Read(buf, read, want - read)) > 0) read += n;
                if (read < want) Array.Resize(ref buf, read);
            }
            string text = Decode(buf);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"rel\":").Append(J(rel))
              .Append(",\"truncated\":").Append(new FileInfo(full).Length > Cap ? "true" : "false")
              .Append(",\"text\":").Append(J(text)).Append('}');
            Json(ctx, sb.ToString());
        }

        // ------------------------------------------------------------------ ai proxy
        private static void Chat(HttpListenerContext ctx)
        {
            string body;
            using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) body = sr.ReadToEnd();
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            Dictionary<string, object> req;
            try { req = js.Deserialize<Dictionary<string, object>>(body); }
            catch (Exception ex) { Json(ctx, "{\"error\":" + J("bad request: " + ex.Message) + "}"); return; }

            string endpoint = Str(req, "endpoint", "https://api.deepseek.com/v1");
            string apiKey = Str(req, "apiKey", "");
            string model = Str(req, "model", "deepseek-chat");
            string system = Str(req, "system", "");
            string user = Str(req, "user", "");

            if (apiKey.Length == 0) { Json(ctx, "{\"error\":\"API key is empty. Open Settings and paste your key.\"}"); return; }
            if (user.Length == 0) { Json(ctx, "{\"error\":\"Nothing selected to analyse.\"}"); return; }

            string url = endpoint.TrimEnd('/');
            if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                url += "/chat/completions";

            var payload = new Dictionary<string, object>();
            payload["model"] = model;
            payload["temperature"] = 0.2;
            List<object> msgs = new List<object>();
            if (system.Length > 0) msgs.Add(new Dictionary<string, object> { { "role", "system" }, { "content", system } });
            msgs.Add(new Dictionary<string, object> { { "role", "user" }, { "content", user } });
            payload["messages"] = msgs;
            payload["stream"] = false;
            string json = js.Serialize(payload);

            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Method = "POST";
                r.ContentType = "application/json";
                r.Headers["Authorization"] = "Bearer " + apiKey;
                r.Timeout = 300000;
                r.ReadWriteTimeout = 300000;
                byte[] data = Encoding.UTF8.GetBytes(json);
                r.ContentLength = data.Length;
                using (Stream s = r.GetRequestStream()) s.Write(data, 0, data.Length);
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string raw = sr.ReadToEnd();
                    Json(ctx, ExtractAnswer(raw));
                }
            }
            catch (WebException wex)
            {
                string detail = wex.Message;
                string status = "";
                try
                {
                    HttpWebResponse r2 = wex.Response as HttpWebResponse;
                    if (r2 != null)
                    {
                        status = "HTTP " + (int)r2.StatusCode + " " + r2.StatusDescription + ": ";
                        using (StreamReader sr = new StreamReader(r2.GetResponseStream(), Encoding.UTF8))
                            detail = sr.ReadToEnd();
                    }
                }
                catch { }
                Json(ctx, "{\"error\":" + J("API call failed. " + status + FriendlyError(detail)) + "}");
            }
            catch (Exception ex)
            {
                Json(ctx, "{\"error\":" + J("API call failed: " + ex.Message) + "}");
            }
        }

        /// <summary>把 API 返回的错误 JSON 提炼成一句人话。</summary>
        private static string FriendlyError(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "no response from the API";
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 16 * 1024 * 1024;
                object root = js.DeserializeObject(raw);
                object msg = GetPath(root, "error", "message");
                if (msg != null) return Convert.ToString(msg);
                object err = GetPath(root, "error");
                if (err != null) return Convert.ToString(err);
            }
            catch { }
            return Clip(raw, 400);
        }

        private static string ExtractAnswer(string raw)
        {
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            try
            {
                object root = js.DeserializeObject(raw);

                object content = GetPath(root, "choices", 0, "message", "content");
                string text = content == null ? "" : Convert.ToString(content);
                if (text.Trim().Length == 0)
                {
                    // reasoning models put the visible text here when content is empty
                    object reasoning = GetPath(root, "choices", 0, "message", "reasoning_content");
                    if (reasoning != null) text = Convert.ToString(reasoning);
                }
                if (text.Trim().Length > 0)
                    return "{\"content\":" + J(text) + "}";

                object errMsg = GetPath(root, "error", "message");
                if (errMsg != null)
                    return "{\"error\":" + J(Convert.ToString(errMsg)) + "}";
                object errObj = GetPath(root, "error");
                if (errObj != null)
                    return "{\"error\":" + J(Convert.ToString(errObj)) + "}";

                return "{\"error\":" + J("The API returned no text. Raw response: " + Clip(raw, 400)) + "}";
            }
            catch (Exception ex)
            {
                return "{\"error\":" + J("Cannot parse the API response (" + ex.Message + "). Raw: " + Clip(raw, 300)) + "}";
            }
        }

        /// <summary>按路径取值，同时支持 Dictionary 与 ArrayList/object[] 两种反序列化结果。</summary>
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

                IList list = node as IList;
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

        private static string Str(Dictionary<string, object> d, string key, string fallback)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return fallback;
        }

        // ------------------------------------------------------------------ helpers
        private static string Clip(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "...";
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

        private static void Json(HttpListenerContext ctx, string json)
        {
            Send(ctx, 200, "application/json; charset=utf-8", json);
        }

        private static void Send(HttpListenerContext ctx, int code, string type, string body)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(body);
                ctx.Response.StatusCode = code;
                ctx.Response.ContentType = type;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
            }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        private static void TryError(HttpListenerContext ctx, string msg)
        {
            try { Json(ctx, "{\"error\":" + J(msg) + "}"); } catch { }
        }

        private static string J(string s)
        {
            if (s == null) return "null";
            StringBuilder b = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
            return b.ToString();
        }
    }
}
