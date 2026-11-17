using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LogCollector
{
    public class Group
    {
        public string Name = "";
        public List<FileItem> Items = new List<FileItem>();
    }

    public class AnalyzerPanel : UserControl
    {
        private static readonly Color Dark = Color.FromArgb(31, 56, 100);
        private static readonly Color Accent = Color.FromArgb(46, 83, 149);
        private static readonly Color Bg = Color.FromArgb(244, 246, 250);
        private static readonly Font UiFont = new Font("Microsoft YaHei UI", 9F);
        private static readonly Font BodyFont = new Font("Microsoft YaHei UI", 9.5F);
        private static readonly Color Line = Color.FromArgb(227, 231, 238);

        private Settings _set;
        private List<Package> _packages = new List<Package>();
        private List<FileItem> _files = new List<FileItem>();
        private List<Group> _groups = new List<Group>();
        private HashSet<string> _sel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _pkgPath = "";
        private string _answer = "";
        private bool _busy;

        private TextBox _txtRoot;
        private ComboBox _cboPkg;
        private Label _lblSel, _lblStatus;
        private Panel _tree;
        private TabControl _tabs;
        private RichTextBox _preview, _answerBox;
        private Button _btnAnalyze, _btnCopy, _btnExport;
        private TextBox _sEndpoint, _sKey, _sModel, _sPer, _sTotal;



        public AnalyzerPanel()
        {
            _set = Settings.Load();
            BuildUi();
            LoadPackages();
        }

        /// <summary>切到某个采集包的父目录并扫描。</summary>
        public void SetRoot(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            _txtRoot.Text = root;
            LoadPackages();
        }

        /// <summary>采集完成后直接打开刚生成的包。</summary>
        public void ShowPackage(string packageDir)
        {
            if (string.IsNullOrEmpty(packageDir) || !Directory.Exists(packageDir)) return;
            string parent = Path.GetDirectoryName(packageDir);
            if (!string.Equals(parent, _txtRoot.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _txtRoot.Text = parent;
                LoadPackages();
            }
            for (int i = 0; i < _packages.Count; i++)
            {
                if (string.Equals(_packages[i].Path, packageDir, StringComparison.OrdinalIgnoreCase))
                {
                    _cboPkg.SelectedIndex = i;
                    break;
                }
            }
        }

        // ---------------------------------------------------------------- UI
        private void BuildUi()
        {
            BackColor = Bg;
            Font = new Font("Segoe UI", 9F);

            // ---------- 路径行 ----------
            Panel bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 46;
            bar.BackColor = Color.White;

            Label lr = new Label();
            lr.Text = "Packages root";
            lr.AutoSize = true;
            lr.ForeColor = Color.FromArgb(71, 85, 105);
            lr.Location = new Point(16, 14);
            bar.Controls.Add(lr);

            _txtRoot = new TextBox();
            _txtRoot.Font = UiFont;
            _txtRoot.Location = new Point(104, 11);
            _txtRoot.Width = 620;
            _txtRoot.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _txtRoot.BorderStyle = BorderStyle.FixedSingle;
            bar.Controls.Add(_txtRoot);

            Button open = MakeButton("Open...", Color.FromArgb(107, 114, 128));
            open.Size = new Size(84, 26);
            open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            open.Click += delegate { BrowseRoot(); };
            bar.Controls.Add(open);

            Button scan = MakeButton("Scan", Accent);
            scan.Size = new Size(70, 26);
            scan.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            scan.Click += delegate { LoadPackages(); };
            bar.Controls.Add(scan);

            bar.Resize += delegate
            {
                scan.Left = bar.Width - scan.Width - 16;
                open.Left = scan.Left - open.Width - 8;
                _txtRoot.Width = open.Left - _txtRoot.Left - 12;
                int top = _txtRoot.Top + (_txtRoot.Height - open.Height) / 2;
                open.Top = top;
                scan.Top = top;
            };
            bar.PerformLayout();

            // ---------- 包选择行 ----------
            Panel bar2 = new Panel();
            bar2.Dock = DockStyle.Top;
            bar2.Height = 42;
            bar2.BackColor = Bg;

            Label lp = new Label();
            lp.Text = "Package";
            lp.AutoSize = true;
            lp.ForeColor = Color.FromArgb(71, 85, 105);
            lp.Location = new Point(16, 13);
            bar2.Controls.Add(lp);

            _cboPkg = new ComboBox();
            _cboPkg.Font = UiFont;
            _cboPkg.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboPkg.Location = new Point(104, 9);
            _cboPkg.Width = 520;
            _cboPkg.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _cboPkg.SelectedIndexChanged += delegate { LoadTree(); };
            bar2.Controls.Add(_cboPkg);

            _lblSel = new Label();
            _lblSel.Text = "";
            _lblSel.AutoSize = false;
            _lblSel.TextAlign = ContentAlignment.MiddleRight;
            _lblSel.Size = new Size(200, 22);
            _lblSel.ForeColor = Color.FromArgb(71, 85, 105);
            _lblSel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblSel.Location = new Point(Width - 216, 11);
            bar2.Controls.Add(_lblSel);

            // ---------- 主区 ----------
            Panel main = new Panel();
            main.Dock = DockStyle.Fill;
            main.Padding = new Padding(12, 6, 12, 10);

            _tree = new Panel();
            _tree.Dock = DockStyle.Left;
            _tree.Width = 300;
            _tree.MinimumSize = new Size(150, 0);
            _tree.BackColor = Color.White;
            _tree.AutoScroll = true;
            _tree.BorderStyle = BorderStyle.FixedSingle;
            _tree.Padding = new Padding(0);

            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.Padding = new Padding(10, 0, 0, 0);

            BuildTabs(right);

            main.Controls.Add(right);
            main.Controls.Add(_tree);

            // 面板变窄时文件树跟着收，别把预览挤没
            main.Resize += delegate
            {
                int want = Math.Max(250, Math.Min(330, main.ClientSize.Width * 2 / 5));
                if (Math.Abs(_tree.Width - want) > 2) _tree.Width = want;
            };

            Controls.Add(main);
            Controls.Add(bar2);
            Controls.Add(bar);

            _txtRoot.Text = string.IsNullOrEmpty(_set.Root)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "LogCollection")
                : _set.Root;
        }

        private void BuildTabs(Panel host)
        {
            _tabs = new TabControl();
            _tabs.Dock = DockStyle.Fill;
            _tabs.Font = new Font("Segoe UI", 9F);

            TabPage pv = new TabPage("Preview");
            pv.BackColor = Color.White;
            pv.Padding = new Padding(10);

            _preview = new RichTextBox();
            _preview.Dock = DockStyle.Fill;
            _preview.ReadOnly = true;
            _preview.BorderStyle = BorderStyle.None;
            _preview.BackColor = Color.FromArgb(251, 252, 254);
            _preview.Font = new Font("Consolas", 9F);
            _preview.WordWrap = false;
            _preview.DetectUrls = false;
            _preview.Text = "Click a text file on the left to preview it.";
            pv.Controls.Add(_preview);

            TabPage ai = new TabPage("AI analysis");
            ai.BackColor = Color.White;
            ai.Padding = new Padding(12);

            Panel aiTop = new Panel();
            aiTop.Dock = DockStyle.Top;
            aiTop.Height = 48;

            _btnAnalyze = MakeButton("Analyze", Accent);
            _btnAnalyze.Size = new Size(150, 32);
            _btnAnalyze.Location = new Point(0, 4);
            _btnAnalyze.Click += delegate { RunAnalysis(); };
            aiTop.Controls.Add(_btnAnalyze);

            _btnCopy = MakeButton("Copy", Color.FromArgb(107, 114, 128));
            _btnCopy.Size = new Size(90, 32);
            _btnCopy.Location = new Point(160, 4);
            _btnCopy.Enabled = false;
            _btnCopy.Click += delegate { CopyAnswer(); };
            aiTop.Controls.Add(_btnCopy);

            _btnExport = MakeButton("Export .txt", Color.FromArgb(107, 114, 128));
            _btnExport.Size = new Size(120, 32);
            _btnExport.Location = new Point(258, 4);
            _btnExport.Enabled = false;
            _btnExport.Click += delegate { ExportAnswer(); };
            aiTop.Controls.Add(_btnExport);

            _lblStatus = new Label();
            _lblStatus.Text = "";
            _lblStatus.AutoSize = true;
            _lblStatus.ForeColor = Color.Gray;
            _lblStatus.Font = UiFont;
            _lblStatus.Location = new Point(390, 12);
            aiTop.Controls.Add(_lblStatus);

            _answerBox = new RichTextBox();
            _answerBox.Dock = DockStyle.Fill;
            _answerBox.ReadOnly = true;
            _answerBox.BorderStyle = BorderStyle.FixedSingle;
            _answerBox.BackColor = Color.White;
            _answerBox.Font = BodyFont;
            _answerBox.DetectUrls = false;

            ai.Controls.Add(_answerBox);
            ai.Controls.Add(aiTop);

            TabPage st = new TabPage("Settings");
            st.BackColor = Color.White;
            st.Padding = new Padding(16);

            Panel sp = new Panel();
            sp.Dock = DockStyle.Fill;
            sp.AutoScroll = true;

            int y = 0;
            y = AddSetting(sp, "Endpoint (OpenAI compatible)", ref _sEndpoint, y, _set.Endpoint);
            y = AddSetting(sp, "API key", ref _sKey, y, _set.Key);
            _sKey.UseSystemPasswordChar = true;
            y = AddSetting(sp, "Model", ref _sModel, y, _set.Model);
            y = AddSetting(sp, "Characters per file", ref _sPer, y, _set.Per.ToString());
            y = AddSetting(sp, "Total characters sent", ref _sTotal, y, _set.Total.ToString());

            Button save = MakeButton("Save settings", Accent);
            save.Size = new Size(130, 30);
            save.Location = new Point(0, y + 6);
            save.Click += delegate { SaveSettings(); };
            sp.Controls.Add(save);

            Label note = new Label();
            note.Text = "The key is stored encrypted for this Windows user (DPAPI).\r\n"
                      + "Only the text you select is sent to the endpoint above.";
            note.AutoSize = true;
            note.ForeColor = Color.Gray;
            note.Location = new Point(0, y + 46);
            sp.Controls.Add(note);

            st.Controls.Add(sp);

            _tabs.TabPages.Add(pv);
            _tabs.TabPages.Add(ai);
            _tabs.TabPages.Add(st);
            host.Controls.Add(_tabs);
        }

        private int AddSetting(Panel host, string label, ref TextBox box, int y, string value)
        {
            Label l = new Label();
            l.Text = label;
            l.AutoSize = true;
            l.ForeColor = Color.FromArgb(71, 85, 105);
            l.Location = new Point(0, y);
            host.Controls.Add(l);

            box = new TextBox();
            box.Font = UiFont;
            box.Text = value;
            box.Location = new Point(0, y + 20);
            box.Width = 520;
            box.BorderStyle = BorderStyle.FixedSingle;
            host.Controls.Add(box);

            return y + 52;
        }

        private Button MakeButton(string text, Color back)
        {
            Button b = new Button();
            b.Text = text;
            b.BackColor = back;
            b.ForeColor = Color.White;
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private Button Chip(string text, int unused)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = false;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.White;
            b.ForeColor = Accent;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 216, 238);
            b.Font = new Font("Segoe UI", 8.5F);
            b.Cursor = Cursors.Hand;
            b.Height = 24;
            b.Width = TextRenderer.MeasureText(text, b.Font).Width + 24;
            return b;
        }

        // ---------------------------------------------------------------- 数据
        private void BrowseRoot()
        {
            using (FolderBrowserDialog f = new FolderBrowserDialog())
            {
                f.Description = "Select the folder that contains the collected packages";
                if (Directory.Exists(_txtRoot.Text)) f.SelectedPath = _txtRoot.Text;
                if (f.ShowDialog(this) == DialogResult.OK) { _txtRoot.Text = f.SelectedPath; LoadPackages(); }
            }
        }

        private void LoadPackages()
        {
            string root = _txtRoot.Text.Trim();
            _set.Root = root;
            _cboPkg.Items.Clear();
            _packages = Logs.Scan(root);
            if (_packages.Count == 0)
            {
                _tree.Controls.Clear();
                Label l = new Label();
                l.Text = "No package found under\r\n" + root + "\r\n\r\nA package is a folder that contains\r\nreport.html or Collection_Info.txt.";
                l.AutoSize = true;
                l.ForeColor = Color.Gray;
                l.Location = new Point(12, 12);
                _tree.Controls.Add(l);
                _lblSel.Text = "";
                return;
            }
            foreach (Package p in _packages) _cboPkg.Items.Add(p);
            _cboPkg.SelectedIndex = 0;
        }

        private void LoadTree()
        {
            int i = _cboPkg.SelectedIndex;
            if (i < 0 || i >= _packages.Count) return;
            Package p = _packages[i];
            _pkgPath = p.Path;
            _files = Logs.Tree(p.Path);
            _sel.Clear();
            _cache.Clear();
            _open.Clear();

            _groups = new List<Group>();
            Dictionary<string, Group> map = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
            foreach (FileItem f in _files)
            {
                string rel = f.Rel.Replace('/', '\\');
                int cut = rel.IndexOf('\\');
                string g = cut > 0 ? rel.Substring(0, cut) : "(root)";
                Group grp;
                if (!map.TryGetValue(g, out grp))
                {
                    grp = new Group();
                    grp.Name = g;
                    map[g] = grp;
                    _groups.Add(grp);
                }
                grp.Items.Add(f);
            }
            _groups.Sort(delegate(Group a, Group b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            RenderTree();
        }

        // ---------------------------------------------------------------- 树的渲染
        private void RenderTree()
        {
            _tree.SuspendLayout();
            _tree.Controls.Clear();
            int y = 4;
            foreach (Group g in _groups)
            {
                int sel = 0;
                foreach (FileItem f in g.Items) if (_sel.Contains(f.Rel)) sel++;
                bool open = _open.Contains(g.Name);

                Panel head = new Panel();
                head.Location = new Point(0, y);
                head.Height = 26;
                head.Width = _tree.ClientSize.Width;
                head.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                head.BackColor = Color.FromArgb(247, 249, 252);
                head.Paint += delegate(object s, PaintEventArgs pe)
                {
                    pe.Graphics.DrawLine(new Pen(Color.FromArgb(238, 241, 246)), 0, head.Height - 1, head.Width, head.Height - 1);
                };
                head.Cursor = Cursors.Hand;
                string gname = g.Name;
                head.Click += delegate { ToggleGroup(gname); };

                Label caret = new Label();
                caret.Text = open ? "\u25BE" : "\u25B8";
                caret.AutoSize = false;
                caret.Size = new Size(14, 26);
                caret.TextAlign = ContentAlignment.MiddleCenter;
                caret.ForeColor = Color.Gray;
                caret.Location = new Point(2, 0);
                caret.Click += delegate { ToggleGroup(gname); };
                head.Controls.Add(caret);

                Label name = new Label();
                name.Text = g.Name;
                name.AutoSize = false;
                name.Size = new Size(Math.Max(50, head.Width - 170), 26);
                name.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                name.TextAlign = ContentAlignment.MiddleLeft;
                name.ForeColor = Dark;
                name.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
                name.Location = new Point(18, 0);
                name.Click += delegate { ToggleGroup(gname); };
                head.Controls.Add(name);

                Label cnt = new Label();
                cnt.Text = sel > 0 ? (sel + "/" + g.Items.Count) : g.Items.Count.ToString();
                cnt.AutoSize = false;
                cnt.Size = new Size(44, 26);
                cnt.TextAlign = ContentAlignment.MiddleRight;
                cnt.ForeColor = Color.Gray;
                cnt.Font = new Font("Segoe UI", 8F);
                cnt.Location = new Point(head.Width - 164, 0);
                cnt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                head.Controls.Add(cnt);

                Button ball = SmallButton("all");
                ball.Location = new Point(head.Width - 112, 2);
                ball.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                ball.Click += delegate { GroupSelect(gname, true); };
                head.Controls.Add(ball);

                Button bnone = SmallButton("none");
                bnone.Location = new Point(head.Width - 58, 2);
                bnone.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                bnone.Click += delegate { GroupSelect(gname, false); };
                head.Controls.Add(bnone);

                _tree.Controls.Add(head);
                y += 27;

                if (!open) continue;

                int shown = 0;
                foreach (FileItem f in g.Items)
                {
                    if (shown >= 400)
                    {
                        Label more = new Label();
                        more.Text = "    ... " + (g.Items.Count - shown) + " more files (use all/none or open the folder in Explorer)";
                        more.AutoSize = true;
                        more.ForeColor = Color.Gray;
                        more.Location = new Point(10, y);
                        _tree.Controls.Add(more);
                        y += 22;
                        break;
                    }
                    y = AddFileRow(f, y);
                    shown++;
                }
                y += 4;
            }
            _tree.ResumeLayout();
            UpdateSelLabel();
        }

        private int AddFileRow(FileItem f, int y)
        {
            bool isText = f.Kind == "text" || f.Kind == "html";
            Panel row = new Panel();
            row.Location = new Point(0, y);
            row.Height = 24;
            row.Width = _tree.ClientSize.Width;
            row.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            CheckBox cb = new CheckBox();
            cb.Location = new Point(26, 4);
            cb.Width = 18;
            cb.Enabled = isText;
            cb.Checked = _sel.Contains(f.Rel);
            string rel = f.Rel;
            cb.CheckedChanged += delegate
            {
                if (cb.Checked) { _sel.Add(rel); Preview(rel); }
                else _sel.Remove(rel);
                UpdateSelLabel();
                UpdateGroupCount(rel);
            };
            row.Controls.Add(cb);

            LinkLabel nm = new LinkLabel();
            nm.Text = f.Name;
            nm.AutoSize = false;
            nm.Size = new Size(Math.Max(60, row.Width - 140), 22);
            nm.LinkBehavior = LinkBehavior.NeverUnderline;
            nm.LinkColor = isText ? Color.FromArgb(51, 65, 85) : Color.FromArgb(176, 183, 195);
            nm.ActiveLinkColor = Accent;
            nm.Font = new Font("Segoe UI", 8.5F);
            nm.Location = new Point(48, 3);
            nm.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            if (isText) nm.LinkClicked += delegate { Preview(rel); };
            else nm.Enabled = false;
            row.Controls.Add(nm);

            Label sz = new Label();
            sz.Text = Logs.Size(f.Size);
            sz.AutoSize = false;
            sz.TextAlign = ContentAlignment.MiddleRight;
            sz.Size = new Size(80, 22);
            sz.ForeColor = Color.Gray;
            sz.Font = new Font("Segoe UI", 7.5F);
            sz.Location = new Point(row.Width - 84, 3);
            sz.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            row.Controls.Add(sz);

            _tree.Controls.Add(row);
            return y + 24;
        }

        private Button SmallButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.Size = new Size(54, 22);
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.BackColor = Color.White;
            b.ForeColor = Accent;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 216, 238);
            b.Font = new Font("Segoe UI", 7.5F);
            b.TextAlign = ContentAlignment.MiddleCenter;
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void ToggleGroup(string name)
        {
            if (_open.Contains(name)) _open.Remove(name); else _open.Add(name);
            RenderTree();
        }

        private void GroupSelect(string name, bool on)
        {
            Group g = _groups.Find(delegate(Group x) { return x.Name == name; });
            if (g == null) return;
            foreach (FileItem f in g.Items)
            {
                if (f.Kind != "text" && f.Kind != "html") continue;
                if (on) _sel.Add(f.Rel); else _sel.Remove(f.Rel);
            }
            _open.Add(name);
            RenderTree();
        }

        private void UpdateGroupCount(string rel) { /* counts refresh on next render */ }

        private void UpdateSelLabel()
        {
            long bytes = 0;
            int n = 0;
            foreach (FileItem f in _files) if (_sel.Contains(f.Rel)) { n++; bytes += f.Size; }
            _lblSel.Text = n == 0 ? "no files selected" : (n + " selected, " + Logs.Size(bytes));
        }

        private void Preview(string rel)
        {
            string full = Path.Combine(_pkgPath, rel);
            string text;
            if (!_cache.TryGetValue(rel, out text))
            {
                try { text = Logs.ReadText(full, 512 * 1024); }
                catch (Exception ex) { text = "[cannot read] " + ex.Message; }
                _cache[rel] = text;
            }
            if (_preview.Text != text)
            {
                _preview.Text = text.Length == 0 ? "[empty file]" : text;
                _preview.SelectionStart = 0;
                _preview.ScrollToCaret();
            }
            _tabs.SelectedIndex = 0;
        }



        // ---------------------------------------------------------------- AI
        private void SaveSettings()
        {
            _set.Endpoint = _sEndpoint.Text.Trim();
            _set.Key = _sKey.Text.Trim();
            _set.Model = _sModel.Text.Trim();
            int v;
            if (int.TryParse(_sPer.Text.Trim(), out v) && v > 1000) _set.Per = v;
            if (int.TryParse(_sTotal.Text.Trim(), out v) && v > 5000) _set.Total = v;
            _set.Save();
            MessageBox.Show(FindForm(), "Settings saved.", "BSOD Log Analyzer");
        }

        private void RunAnalysis()
        {
            if (_busy) return;
            if (_sel.Count == 0) { MessageBox.Show(FindForm(), "Select at least one log file first."); return; }

            _set.Endpoint = _sEndpoint.Text.Trim();
            _set.Key = _sKey.Text.Trim();
            _set.Model = _sModel.Text.Trim();
            if (_set.Key.Length == 0) { MessageBox.Show(FindForm(), "Set your API key in the Settings tab first."); _tabs.SelectedIndex = 2; return; }

            string content = BuildContent();
            if (content.Length == 0) { MessageBox.Show(FindForm(), "The selected files produced no text."); return; }

            string question = "请分析这些日志，按领域（启动、电源、驱动、磁盘、网络、应用）归纳主要问题，"
                            + "并说明每个问题的判断依据和置信度。";
            string user = "Analyse these Windows laptop logs and answer the question at the end.\r\n\r\n"
                        + content + "\r\n\r\n---\r\nQuestion: " + question;
            string system = "你是一名资深的 Windows 平台工程师，正在分析笔记本测试中采集到的日志。"
                          + "请用中文回答，结论要具体，引用文件名和时间戳，明确区分“日志已证实”和“你的推测”。"
                          + "使用简短的小标题和要点列表，不要输出与日志无关的客套话。";

            _busy = true;
            _btnAnalyze.Enabled = false;
            _btnAnalyze.Text = "Analyzing...";
            _lblStatus.Text = "calling " + _set.Model + " (" + content.Length.ToString("N0") + " chars of logs)";
            _answerBox.Text = "";
            _btnCopy.Enabled = false;
            _btnExport.Enabled = false;

            Settings snapshot = _set;
            Thread th = new Thread(delegate()
            {
                string err;
                string answer = Ai.Ask(snapshot, system, user, out err);
                BeginInvoke(new Action(delegate()
                {
                    _busy = false;
                    _btnAnalyze.Enabled = true;
                    _btnAnalyze.Text = "Analyze with AI";
                    if (answer == null)
                    {
                        _lblStatus.Text = "failed";
                        _answerBox.Text = "";
            _btnCopy.Enabled = false;
            _btnExport.Enabled = false;
                        ShowError(err);
                    }
                    else
                    {
                        _answer = answer;
                        _lblStatus.Text = "done (" + answer.Length.ToString("N0") + " chars)";
                        RenderMarkdown(answer);
                        _btnCopy.Enabled = true;
                        _btnExport.Enabled = true;
                    }
                }));
            });
            th.IsBackground = true;
            th.Start();
        }

        private void CopyAnswer()
        {
            if (string.IsNullOrEmpty(_answer)) return;
            try { Clipboard.SetText(_answer); _lblStatus.Text = "copied to clipboard"; }
            catch (Exception ex) { _lblStatus.Text = "copy failed: " + ex.Message; }
        }

        private void ExportAnswer()
        {
            if (string.IsNullOrEmpty(_answer)) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.FileName = "ai-analysis.txt";
                d.Filter = "Text file (*.txt)|*.txt|Markdown (*.md)|*.md";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, _answer, new UTF8Encoding(true));
                    _lblStatus.Text = "saved: " + Path.GetFileName(d.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "Cannot save the file: " + ex.Message, "BSOD Log Analyzer");
                }
            }
        }

        private void ShowError(string err)
        {
            _answerBox.SelectionFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            _answerBox.SelectionColor = Color.FromArgb(153, 27, 27);
            _answerBox.AppendText("Analysis failed\r\n\r\n");
            _answerBox.SelectionFont = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            _answerBox.SelectionColor = Color.FromArgb(60, 60, 60);
            _answerBox.AppendText(err ?? "unknown error");
            _answerBox.SelectionColor = _answerBox.ForeColor;
        }

        private string BuildContent()
        {
            StringBuilder sb = new StringBuilder();
            foreach (FileItem f in _files)
            {
                if (!_sel.Contains(f.Rel)) continue;
                string text;
                if (!_cache.TryGetValue(f.Rel, out text))
                {
                    try { text = Logs.ReadText(Path.Combine(_pkgPath, f.Rel), 512 * 1024); }
                    catch { continue; }
                    _cache[f.Rel] = text;
                }
                string body = text.Length > _set.Per ? text.Substring(0, _set.Per) + "\r\n...[truncated]" : text;
                string block = "=== FILE: " + f.Rel + " (" + Logs.Size(f.Size) + ") ===\r\n" + body + "\r\n\r\n";
                if (sb.Length + block.Length > _set.Total)
                {
                    sb.AppendLine("...[total size limit reached, remaining files skipped]");
                    break;
                }
                sb.Append(block);
            }
            return sb.ToString();
        }


        // ---------------------------------------------------------------- Markdown
        private void RenderMarkdown(string md)
        {
            _answerBox.Clear();
            _answerBox.SelectionColor = Color.FromArgb(31, 41, 55);
            string[] lines = md.Replace("\r\n", "\n").Split('\n');
            bool code = false;

            foreach (string raw in lines)
            {
                string line = raw;
                if (line.TrimStart().StartsWith("```"))
                {
                    code = !code;
                    continue;
                }
                if (code)
                {
                    AddRun(line + "\n", new Font("Consolas", 9F), Color.FromArgb(51, 65, 85));
                    continue;
                }
                if (line.StartsWith("### ")) { AddRun(line.Substring(4) + "\n", new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold), Dark); continue; }
                if (line.StartsWith("## ")) { AddRun(line.Substring(3) + "\n", new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold), Dark); continue; }
                if (line.StartsWith("# ")) { AddRun(line.Substring(2) + "\n", new Font("Microsoft YaHei UI", 13F, FontStyle.Bold), Dark); continue; }
                if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
                {
                    AddRun("  \u2022  ", new Font("Microsoft YaHei UI", 9.5F), Accent);
                    AddInline(line.TrimStart().Substring(2) + "\n");
                    continue;
                }
                if (line.Trim().Length == 0) { _answerBox.AppendText("\n"); continue; }
                AddInline(line + "\n");
            }
            _answerBox.SelectionStart = 0;
            _answerBox.ScrollToCaret();
        }

        private void AddInline(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                int b = text.IndexOf("**", i, StringComparison.Ordinal);
                int c = text.IndexOf('`', i);
                int next = -1;
                bool bold = false;
                if (b >= 0) { next = b; bold = true; }
                if (c >= 0 && (next < 0 || c < next)) { next = c; bold = false; }

                if (next < 0)
                {
                    AddRun(text.Substring(i), new Font("Microsoft YaHei UI", 9.5F), Color.FromArgb(31, 41, 55));
                    return;
                }
                if (next > i)
                    AddRun(text.Substring(i, next - i), new Font("Microsoft YaHei UI", 9.5F), Color.FromArgb(31, 41, 55));

                if (bold)
                {
                    int end = text.IndexOf("**", next + 2, StringComparison.Ordinal);
                    if (end < 0) { AddRun(text.Substring(next), new Font("Microsoft YaHei UI", 9.5F), Color.FromArgb(31, 41, 55)); return; }
                    AddRun(text.Substring(next + 2, end - next - 2), new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold), Color.FromArgb(17, 24, 39));
                    i = end + 2;
                }
                else
                {
                    int end = text.IndexOf('`', next + 1);
                    if (end < 0) { AddRun(text.Substring(next), new Font("Microsoft YaHei UI", 9.5F), Color.FromArgb(31, 41, 55)); return; }
                    AddRun(text.Substring(next + 1, end - next - 1), new Font("Consolas", 9F), Color.FromArgb(51, 65, 85));
                    i = end + 1;
                }
            }
        }

        private void AddRun(string text, Font font, Color color)
        {
            _answerBox.SelectionStart = _answerBox.TextLength;
            _answerBox.SelectionLength = 0;
            _answerBox.SelectionFont = font;
            _answerBox.SelectionColor = color;
            _answerBox.AppendText(text);
        }
    }
}
