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
    public class MainForm : Form
    {
        private static readonly Color Dark = Color.FromArgb(31, 56, 100);
        private static readonly Color Accent = Color.FromArgb(46, 83, 149);
        private static readonly Color Bg = Color.FromArgb(243, 245, 249);
        private static readonly Color Line = Color.FromArgb(224, 228, 236);

        private Dictionary<string, CheckBox> _checks = new Dictionary<string, CheckBox>();
        private Dictionary<string, bool> _sel = new Dictionary<string, bool>();   // 勾选状态，和控件解耦
        private Panel _jobsPanel;
        private List<JobDef> _defs;
        private TextBox _txtOperator, _txtOut;
        private AnalyzerPanel _analyzer;
        private Panel _panelBottom;
        private RichTextBox _log;
        private ProgressBar _bar;
        private Label _lblStatus, _lblMachine, _lblAdmin;
        private Button _btnStart, _btnStop, _btnReport, _btnElevate, _btnBrowse;
        private CancellationTokenSource _cts;
        private bool _running;
        private string _lastDir;
        private List<Panel> _scrollPanels = new List<Panel>();
        private Panel _quickBar;
        private List<string> _cats = new List<string>();
        private HashSet<string> _openCats = new HashSet<string>();

        public MainForm()
        {
            _defs = Catalog.All();
            foreach (JobDef d in _defs) _sel[d.Id] = d.DefaultOn;   // 默认全选（折叠起来的大类也算）
            BuildUi();
            LoadMachineInfo();
            try
            {
                string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "LogCollection");
                if (_analyzer != null) _analyzer.SetRoot(def);
            }
            catch { }
        }

        // ------------------------------------------------------------ UI
        private void BuildUi()
        {
            Text = "Laptop Log Collector  v" + Program.Version;
            Size = new Size(1450, 890);
            MinimumSize = new Size(1200, 740);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            Font = new Font("Microsoft YaHei UI", 9F);
            DoubleBuffered = true;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            root.BackColor = Bg;

            // ---------- header ----------
            Panel top = new Panel();
            top.Dock = DockStyle.Fill;
            top.BackColor = Dark;
            top.Margin = new Padding(0);

            Label title = new Label();
            title.Text = "Laptop Log Collector";
            title.ForeColor = Color.White;
            title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(20, 12);
            top.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "Select what to collect, then hand the package and HTML report to engineering";
            sub.ForeColor = Color.FromArgb(178, 196, 224);
            sub.AutoSize = true;
            sub.Location = new Point(22, 46);
            top.Controls.Add(sub);

            _lblMachine = new Label();
            _lblMachine.Text = "Reading machine information...";
            _lblMachine.ForeColor = Color.FromArgb(210, 222, 240);
            _lblMachine.AutoSize = false;
            _lblMachine.TextAlign = ContentAlignment.MiddleRight;
            _lblMachine.Size = new Size(560, 24);
            top.Controls.Add(_lblMachine);

            _lblAdmin = new Label();
            _lblAdmin.Text = "";
            _lblAdmin.ForeColor = Color.FromArgb(255, 209, 102);
            _lblAdmin.AutoSize = false;
            _lblAdmin.TextAlign = ContentAlignment.MiddleRight;
            _lblAdmin.Size = new Size(330, 20);
            top.Controls.Add(_lblAdmin);

            _btnElevate = MakeSmallButton("Restart as administrator", Color.FromArgb(72, 98, 145));
            _btnElevate.Size = new Size(178, 24);
            _btnElevate.Visible = false;
            _btnElevate.Click += delegate
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                    Close();
                }
                catch (Exception ex) { MessageBox.Show(this, "Elevation failed: " + ex.Message, "Notice"); }
            };
            top.Controls.Add(_btnElevate);

            top.Resize += delegate { LayoutTop(top); };
            LayoutTop(top);

            // ---------- middle ----------
            TableLayoutPanel mid = new TableLayoutPanel();
            mid.Dock = DockStyle.Fill;
            mid.ColumnCount = 2;
            mid.RowCount = 1;
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 610));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mid.Padding = new Padding(14, 12, 14, 8);
            mid.BackColor = Bg;

            Panel left = new Panel();
            left.Dock = DockStyle.Fill;
            left.AutoScroll = true;
            left.BackColor = Bg;
            left.Padding = new Padding(0, 0, 8, 0);
            _scrollPanels.Add(left);

            int y = 0;
            // quick buttons
            Panel quick = new Panel();
            quick.Location = new Point(0, 0);
            quick.Size = new Size(600, 30);
            Button bAll = MakeSmallButton("Select all", Accent);
            bAll.Location = new Point(0, 0); bAll.Size = new Size(64, 26);
            bAll.Click += delegate { SetAll(true); };
            Button bNone = MakeSmallButton("Clear all", Color.Gray);
            bNone.Location = new Point(72, 0); bNone.Size = new Size(72, 26);
            bNone.Click += delegate { SetAll(false); };
            Button bDef = MakeSmallButton("Restore defaults", Color.Gray);
            bDef.Location = new Point(152, 0); bDef.Size = new Size(82, 26);
            bDef.Click += delegate { ResetDefault(); };
            Label who = new Label();
            who.Text = "Collected by *";
            who.AutoSize = true;
            who.ForeColor = Color.FromArgb(71, 85, 105);
            who.Location = new Point(248, 6);
            quick.Controls.Add(bAll);
            quick.Controls.Add(bNone);
            quick.Controls.Add(bDef);
            quick.Controls.Add(who);

            _txtOperator = new TextBox();
            _txtOperator.Location = new Point(342, 3);
            _txtOperator.Size = new Size(240, 24);
            _txtOperator.BorderStyle = BorderStyle.FixedSingle;
            _txtOperator.BackColor = Color.White;
            _txtOperator.HandleCreated += delegate
            {
                // 空框里显示灰色提示文字（Windows 原生的 cue banner）
                SendMessage(_txtOperator.Handle, EM_SETCUEBANNER, (IntPtr)1, "Enter collector name");
            };
            quick.Controls.Add(_txtOperator);
            left.Controls.Add(quick);
            _quickBar = quick;
            _jobsPanel = left;
            y = 36;

            _cats.Clear();
            foreach (JobDef d in _defs) if (!_cats.Contains(d.Category)) _cats.Add(d.Category);
            if (_openCats.Count == 0 && _cats.Count > 0) _openCats.Add(_cats[0]);
            RenderJobs(left);

            _analyzer = new AnalyzerPanel();
            _analyzer.Dock = DockStyle.Fill;
            _analyzer.Margin = new Padding(10, 0, 0, 0);
            _analyzer.BackColor = Color.White;

            mid.Controls.Add(left, 0, 0);
            mid.Controls.Add(_analyzer, 1, 0);

            // ---------- footer ----------
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Fill;
            bottom.BackColor = Color.White;
            bottom.Margin = new Padding(0);
            _panelBottom = bottom;

            Label lo = new Label();
            lo.Text = "Output folder";
            lo.AutoSize = true;
            lo.ForeColor = Color.FromArgb(71, 85, 105);
            lo.Location = new Point(16, 14);
            bottom.Controls.Add(lo);

            _txtOut = new TextBox();
            _txtOut.Location = new Point(80, 11);
            _txtOut.Size = new Size(560, 24);
            _txtOut.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "LogCollection");
            _txtOut.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bottom.Controls.Add(_txtOut);

            _btnStart = MakeButton("Start collection", Accent, Color.White);
            _btnStart.Size = new Size(120, 34);
            _btnStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStart.Click += delegate { StartCollect(); };
            bottom.Controls.Add(_btnStart);

            _btnStop = MakeButton("Stop", Color.FromArgb(220, 38, 38), Color.White);
            _btnStop.Size = new Size(72, 34);
            _btnStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStop.Click += delegate
            {
                if (_cts != null) _cts.Cancel();
                EnableButton(_btnStop, false, Color.FromArgb(220, 38, 38));
                SetStatus("Stopping...");
            };
            bottom.Controls.Add(_btnStop);
            EnableButton(_btnStop, false, Color.FromArgb(220, 38, 38));

            _btnReport = MakeButton("View report", Color.FromArgb(16, 185, 129), Color.White);
            _btnReport.Size = new Size(96, 34);
            _btnReport.Enabled = false;
            _btnReport.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnReport.Click += delegate { OpenReport(); };
            bottom.Controls.Add(_btnReport);
            EnableButton(_btnReport, false, Color.FromArgb(16, 185, 129));

            _btnBrowse = MakeButton("Browse...", Color.FromArgb(107, 114, 128), Color.White);
            _btnBrowse.Size = new Size(96, 34);
            _btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnBrowse.Click += delegate { BrowseOutput(); };
            bottom.Controls.Add(_btnBrowse);

            _bar = new ProgressBar();
            _bar.Location = new Point(16, 52);
            _bar.Size = new Size(1000, 16);
            bottom.Controls.Add(_bar);

            _lblStatus = new Label();
            _lblStatus.Text = "Ready";
            _lblStatus.AutoSize = true;
            _lblStatus.ForeColor = Color.FromArgb(71, 85, 105);
            _lblStatus.Location = new Point(16, 72);
            bottom.Controls.Add(_lblStatus);

            _log = new RichTextBox();
            _log.Location = new Point(16, 96);
            _log.Size = new Size(1000, 128);
            _log.ScrollBars = RichTextBoxScrollBars.ForcedVertical;
            _log.BackColor = Color.FromArgb(17, 24, 39);
            _log.ForeColor = Color.FromArgb(209, 213, 219);
            _log.Font = new Font("Consolas", 8.5F);
            _log.BorderStyle = BorderStyle.None;
            _log.ReadOnly = true;
            _log.WordWrap = true;
            _log.DetectUrls = false;
            bottom.Controls.Add(_log);

            bottom.Resize += delegate { LayoutBottom(); };
            root.Controls.Add(top, 0, 0);
            root.Controls.Add(mid, 0, 1);
            root.Controls.Add(bottom, 0, 2);
            Controls.Add(root);

            LayoutBottom();
            UpdateAdminState();
        }

        private const int EM_SETCUEBANNER = 0x1501;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private void EnableButton(Button b, bool on, Color onColor)
        {
            b.Enabled = on;
            b.BackColor = on ? onColor : Color.FromArgb(186, 192, 202);
        }

        private void LayoutTop(Panel top)
        {
            int w = top.ClientSize.Width;
            _lblMachine.Location = new Point(Math.Max(20, w - _lblMachine.Width - 20), 10);
            int elevateW = _btnElevate.Visible ? _btnElevate.Width + 10 : 0;
            _btnElevate.Location = new Point(w - _btnElevate.Width - 18, 40);
            _lblAdmin.Location = new Point(w - _lblAdmin.Width - 18 - elevateW, 42);
        }

        /// <summary>按大类渲染采集项：点大类标题展开/收起。</summary>
        private void RenderJobs(Panel left)
        {
            for (int i = left.Controls.Count - 1; i >= 0; i--)
            {
                Control c = left.Controls[i];
                if (c != _quickBar) left.Controls.RemoveAt(i);
            }

            int y = 36;
            int width = Math.Max(420, left.ClientSize.Width - 8);

            foreach (string cat in _cats)
            {
                List<JobDef> items = new List<JobDef>();
                foreach (JobDef d in _defs) if (d.Category == cat) items.Add(d);
                bool open = _openCats.Contains(cat);

                Panel head = new Panel();
                head.Location = new Point(0, y);
                head.Size = new Size(width, 30);
                head.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                head.BackColor = Color.FromArgb(246, 248, 252);
                head.Cursor = Cursors.Hand;

                string cname = cat;
                head.Click += delegate { ToggleCat(cname, left); };

                Label caret = new Label();
                caret.Text = open ? "\u25BE" : "\u25B8";
                caret.AutoSize = false;
                caret.Size = new Size(18, 30);
                caret.TextAlign = ContentAlignment.MiddleCenter;
                caret.ForeColor = Color.Gray;
                caret.Location = new Point(2, 0);
                caret.Click += delegate { ToggleCat(cname, left); };
                head.Controls.Add(caret);

                Label nm = new Label();
                nm.Text = cat;
                nm.AutoSize = false;
                nm.Size = new Size(330, 30);
                nm.TextAlign = ContentAlignment.MiddleLeft;
                nm.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
                nm.ForeColor = Dark;
                nm.Location = new Point(22, 0);
                nm.Click += delegate { ToggleCat(cname, left); };
                head.Controls.Add(nm);

                Label cnt = new Label();
                cnt.Text = items.Count + " items";
                cnt.AutoSize = false;
                cnt.Size = new Size(90, 30);
                cnt.TextAlign = ContentAlignment.MiddleRight;
                cnt.ForeColor = Color.Gray;
                cnt.Font = new Font("Microsoft YaHei UI", 8F);
                cnt.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                cnt.Location = new Point(width - 94, 0);
                cnt.Click += delegate { ToggleCat(cname, left); };
                head.Controls.Add(cnt);

                left.Controls.Add(head);
                y += 32;

                if (!open) continue;

                foreach (JobDef d in items)
                {
                    bool on;
                    if (!_sel.TryGetValue(d.Id, out on)) on = d.DefaultOn;

                    CheckBox cb = new CheckBox();
                    cb.Text = Width2(Runner.NumOf(d.Id).ToString()) + "  " + d.Name;
                    cb.Checked = on;
                    cb.AutoSize = true;
                    cb.Font = new Font("Microsoft YaHei UI", 9F);
                    cb.ForeColor = Color.FromArgb(33, 41, 54);
                    cb.Location = new Point(30, y);
                    left.Controls.Add(cb);
                    _checks[d.Id] = cb;

                    string jid = d.Id;
                    cb.CheckedChanged += delegate { _sel[jid] = cb.Checked; };

                    Label h = new Label();
                    h.Text = d.Hint;
                    h.AutoSize = false;
                    h.Size = new Size(width - 60, 16);
                    h.ForeColor = Color.Gray;
                    h.Font = new Font("Microsoft YaHei UI", 8F);
                    h.Location = new Point(50, y + 19);
                    left.Controls.Add(h);

                    y += 40;
                }
                y += 6;
            }
        }

        private void ToggleCat(string cat, Panel left)
        {
            if (_openCats.Contains(cat)) _openCats.Remove(cat);
            else _openCats.Add(cat);
            RenderJobs(left);
        }

        private void LayoutBottom()
        {
            Panel bottom = _panelBottom;
            if (bottom == null || _btnStart == null) return;

            int right = bottom.ClientSize.Width - 16;
            int gap = 6;
            int x = right;
            x -= _btnStart.Width; _btnStart.Location = new Point(x, 8);
            x -= _btnStop.Width + gap; _btnStop.Location = new Point(x, 8);
            x -= _btnReport.Width + gap; _btnReport.Location = new Point(x, 8);
            x -= _btnBrowse.Width + gap; _btnBrowse.Location = new Point(x, 8);
            int left = 118;
            _txtOut.Location = new Point(left, 11);
            _txtOut.Width = Math.Max(120, x - left - 12);

            // 进度条和日志框左右对齐，日志框离窗口底边留一点空隙
            int content = Math.Max(200, bottom.ClientSize.Width - 32);
            _bar.Location = new Point(16, 52);
            _bar.Width = content;

            int logTop = 96;
            int logHeight = Math.Max(40, bottom.ClientSize.Height - logTop - 10);
            _log.Location = new Point(16, logTop);
            _log.Size = new Size(content, logHeight);
        }

        private int AddField(Panel p, string label, ref TextBox box, int y, bool multi)
        {
            Label l = new Label();
            l.Text = label;
            l.AutoSize = true;
            l.ForeColor = Color.FromArgb(71, 85, 105);
            l.Location = new Point(16, y);
            p.Controls.Add(l);
            box = new TextBox();
            box.Location = new Point(16, y + 22);
            box.Size = new Size(360, 24);
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            box.BorderStyle = BorderStyle.FixedSingle;
            p.Controls.Add(box);
            return y + 52;
        }

        /// <summary>把序号补成两位并加点，勾选列表里名字能对齐。</summary>
        private static string Width2(string n)
        {
            return string.IsNullOrEmpty(n) ? "   " : n.PadLeft(2, ' ') + ".";
        }

        private Button MakeButton(string text, Color back, Color fore)
        {
            Button b = new Button();
            b.Text = text;
            b.BackColor = back;
            b.ForeColor = fore;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private Button MakeSmallButton(string text, Color back)
        {
            Button b = MakeButton(text, back, Color.White);
            b.Font = new Font("Microsoft YaHei UI", 8.5F);
            return b;
        }

        private void SetAll(bool on)
        {
            foreach (JobDef d in _defs) _sel[d.Id] = on;
            if (_jobsPanel != null) RenderJobs(_jobsPanel);
        }

        private void ResetDefault()
        {
            foreach (JobDef d in _defs) _sel[d.Id] = d.DefaultOn;
            if (_jobsPanel != null) RenderJobs(_jobsPanel);
        }

        // ------------------------------------------------------------ state
        private void LoadMachineInfo()
        {
            MachineInfo m = Tool.ReadMachine();
            _lblMachine.Text = m.Display() + "   " + m.OsName + " " + m.OsVersion;
            Thread th = new Thread(delegate()
            {
                try
                {
                    string sn = Tool.FetchSerial(CancellationToken.None);
                    if (!string.IsNullOrEmpty(sn))
                    {
                        BeginInvoke(new Action(delegate
                        {
                            _lblMachine.Text = m.Display() + "   SN " + sn + "   " + m.OsName + " " + m.OsVersion;
                        }));
                    }
                }
                catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        private void UpdateAdminState()
        {
            if (Tool.IsElevated())
            {
                _lblAdmin.Text = "Running as administrator";
                _lblAdmin.ForeColor = Color.FromArgb(134, 239, 172);
                _btnElevate.Visible = false;
            }
            else
            {
                _lblAdmin.Text = "Standard user: some items unavailable";
                _lblAdmin.ForeColor = Color.FromArgb(253, 224, 71);
                _btnElevate.Visible = true;
            }
            Panel tp = _btnElevate.Parent as Panel;
            if (tp != null) LayoutTop(tp);
        }

        private void SetStatus(string t)
        {
            _lblStatus.Text = t;
        }

        private void AppendLog(string msg)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(AppendLog), msg); return; }
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg;
            _log.AppendText(line + "\n");
            if (_log.Lines.Length > 600)
            {
                int cut = _log.Lines.Length - 600;
                int idx = _log.GetFirstCharIndexFromLine(cut);
                if (idx > 0) _log.Select(0, idx);
                _log.SelectedText = "";
            }
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        private void ProgressSafe(int done, int total, string name)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<int, int, string>(ProgressSafe), done, total, name); return; }
            if (total > 0)
            {
                _bar.Maximum = total;
                _bar.Value = Math.Min(done, total);
            }
            if (!string.IsNullOrEmpty(name)) SetStatus("Collecting: " + name);
            else SetStatus("Finished " + done + " / " + total);
        }

        // ------------------------------------------------------------ collection
        private void StartCollect()
        {
            if (_running) return;

            string who = _txtOperator.Text.Trim();
            if (who.Length == 0)
            {
                MessageBox.Show(this, "Please enter the collector's name before collecting.",
                                "Collector name required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _txtOperator.Focus();
                return;
            }

            List<JobDef> picked = new List<JobDef>();
            foreach (JobDef d in _defs)
            {
                bool on;
                if (_sel.TryGetValue(d.Id, out on) && on) picked.Add(d);
            }
            if (picked.Count == 0)
            {
                MessageBox.Show(this, "Please select at least one item.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string root = _txtOut.Text.Trim();
            try
            {
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Output folder unavailable: " + ex.Message, "Notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Tool.IsElevated())
            {
                DialogResult r = MessageBox.Show(this,
                    "Running as a standard user: dump files, energy report and sleep study cannot be collected.\r\nRestart as administrator?",
                    "Permission", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.Yes)
                {
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
                        psi.UseShellExecute = true;
                        psi.Verb = "runas";
                        Process.Start(psi);
                        Close();
                    }
                    catch (Exception ex) { MessageBox.Show(this, "Elevation failed; continuing with current rights." + ex.Message, "Notice"); }
                    return;
                }
            }

            Session s = new Session();
            s.IsAdmin = Tool.IsElevated();
            s.Info = Tool.ReadMachine();
            if (s.Info.Serial.Length == 0)
            {
                SetStatus("Reading serial number...");
                try { s.Info.Serial = Tool.FetchSerial(CancellationToken.None).Trim(); } catch { }
            }
            s.OperatorName = who;

            bool needSerial = s.Info.Serial.Length == 0;
            string tag = needSerial ? s.Info.Model : s.Info.Serial;
            try { s.OutDir = Runner.MakeOutputDir(root, tag); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to create output folder: " + ex.Message, "Notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _cts = new CancellationTokenSource();
            s.Token = _cts.Token;
            s.OnLog = AppendLog;
            s.OnProgress = ProgressSafe;

            _running = true;
            EnableButton(_btnStart, false, Accent);
            EnableButton(_btnStop, true, Color.FromArgb(220, 38, 38));
            EnableButton(_btnReport, false, Color.FromArgb(16, 185, 129));
            _log.Clear();
            _bar.Value = 0;
            _bar.Maximum = picked.Count + 1;
            AppendLog("Output folder: " + s.OutDir);
            AppendLog(picked.Count + " item(s), starting...");

            Thread th = new Thread(delegate()
            {
                try { Runner.Run(s, picked); }
                catch (Exception ex) { AppendLog("Collection error: " + ex.Message); }
                finally { try { BeginInvoke(new Action(delegate { Finish(); })); } catch { } }
            });
            th.IsBackground = true;
            th.Start();

            _lastDir = s.OutDir;
        }

        private void Finish()
        {
            _running = false;
            EnableButton(_btnStart, true, Accent);
            EnableButton(_btnStop, false, Color.FromArgb(220, 38, 38));
            EnableButton(_btnReport, true, Color.FromArgb(16, 185, 129));
            _bar.Value = _bar.Maximum;
            SetStatus("Collection finished, report generated");
            if (_analyzer != null) _analyzer.ShowPackage(_lastDir);
            ResetScroll();
            AppendLog("");
            AppendLog("Saved: " + _lastDir);
            AppendLog("Use \"View report\" if you want to open report.html.");
        }

        private void BrowseOutput()
        {
            using (FolderBrowserDialog f = new FolderBrowserDialog())
            {
                f.Description = "Select the folder where collected packages are saved";
                f.ShowNewFolderButton = true;
                string cur = _txtOut.Text.Trim();
                if (cur.Length > 0 && Directory.Exists(cur)) f.SelectedPath = cur;
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    _txtOut.Text = f.SelectedPath;
                    _txtOut.SelectionStart = _txtOut.TextLength;
                }
            }
        }

        private void OpenReport()
        {
            try
            {
                string p = Path.Combine(_lastDir, "report.html");
                if (File.Exists(p)) Process.Start(p);
                else MessageBox.Show(this, "Report file not found.", "Notice");
            }
            catch (Exception ex) { MessageBox.Show(this, "Failed to open: " + ex.Message, "Notice"); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateAdminState();
            ResetScroll();
            if (_btnStart != null) _btnStart.Focus();
        }

        private void ResetScroll()
        {
            ActiveControl = null;
            foreach (Panel p in _scrollPanels)
            {
                p.AutoScrollPosition = new Point(0, 0);
                p.PerformLayout();
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(Width, wa.Width - 24);
            int h = Math.Min(Height, wa.Height - 24);
            Size = new Size(w, h);
            Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2);
            LayoutBottom();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_running)
            {
                DialogResult r = MessageBox.Show(this, "Collection is still running. Exit anyway?", "Confirm",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                if (_cts != null) _cts.Cancel();
            }
            base.OnFormClosing(e);
        }
    }
}
