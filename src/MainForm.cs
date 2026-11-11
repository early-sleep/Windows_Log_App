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
        private List<JobDef> _defs;
        private TextBox _txtCase, _txtOperator, _txtSymptom, _txtAppDir, _txtOut, _txtProc;
        private RichTextBox _log;
        private ProgressBar _bar;
        private Label _lblStatus, _lblMachine, _lblAdmin;
        private Button _btnStart, _btnStop, _btnOpen, _btnReport, _btnElevate, _btnBrowse;
        private CancellationTokenSource _cts;
        private bool _running;
        private string _lastDir;
        private List<Panel> _scrollPanels = new List<Panel>();

        public MainForm()
        {
            _defs = Catalog.All();
            BuildUi();
            LoadMachineInfo();
        }

        // ------------------------------------------------------------ UI
        private void BuildUi()
        {
            Text = "Laptop Log Collector  v" + Program.Version;
            Size = new Size(1080, 800);
            MinimumSize = new Size(980, 700);
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 240));
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
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 646));
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
            Label tip = new Label();
            tip.Text = "Tick what to collect (blue titles are recommended defaults)";
            tip.AutoSize = true;
            tip.ForeColor = Color.Gray;
            tip.Location = new Point(248, 5);
            quick.Controls.Add(bAll);
            quick.Controls.Add(bNone);
            quick.Controls.Add(bDef);
            quick.Controls.Add(tip);
            left.Controls.Add(quick);
            y = 36;

            List<string> cats = new List<string>();
            foreach (JobDef d in _defs) if (!cats.Contains(d.Category)) cats.Add(d.Category);

            foreach (string cat in cats)
            {
                List<JobDef> items = new List<JobDef>();
                foreach (JobDef d in _defs) if (d.Category == cat) items.Add(d);

                GroupBox gb = new GroupBox();
                gb.Text = " " + cat + " ";
                gb.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                gb.ForeColor = Dark;
                gb.Location = new Point(0, y);
                gb.Size = new Size(600, 22 + items.Count * 40);
                gb.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

                int iy = 22;
                foreach (JobDef d in items)
                {
                    CheckBox cb = new CheckBox();
                    cb.Text = d.Name;
                    cb.Checked = d.DefaultOn;
                    cb.AutoSize = true;
                    cb.Font = new Font("Microsoft YaHei UI", 9F);
                    cb.ForeColor = Color.FromArgb(33, 41, 54);
                    cb.Location = new Point(14, iy);
                    gb.Controls.Add(cb);
                    _checks[d.Id] = cb;

                    Label h = new Label();
                    h.Text = d.Hint;
                    h.AutoSize = false;
                    h.Size = new Size(540, 16);
                    h.ForeColor = Color.Gray;
                    h.Font = new Font("Microsoft YaHei UI", 8F);
                    h.Location = new Point(34, iy + 19);
                    gb.Controls.Add(h);

                    iy += 40;
                }
                left.Controls.Add(gb);
                y += gb.Height + 10;
            }

            Panel right = new Panel();
            right.Dock = DockStyle.Fill;
            right.BackColor = Color.White;
            right.Padding = new Padding(16, 12, 16, 12);
            right.AutoScroll = true;
            _scrollPanels.Add(right);

            Label rt = new Label();
            rt.Text = "Test context (optional, written into the report)";
            rt.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            rt.ForeColor = Dark;
            rt.AutoSize = true;
            rt.Location = new Point(16, 12);
            right.Controls.Add(rt);

            int ry = 44;
            ry = AddField(right, "Case ID", ref _txtCase, ry, false);
            ry = AddField(right, "Collected by", ref _txtOperator, ry, false);
            _txtOperator.Text = Environment.UserName;

            Label ls = new Label();
            ls.Text = "Symptom description";
            ls.AutoSize = true;
            ls.ForeColor = Color.FromArgb(71, 85, 105);
            ls.Location = new Point(16, ry);
            right.Controls.Add(ls);
            _txtSymptom = new TextBox();
            _txtSymptom.Multiline = true;
            _txtSymptom.ScrollBars = ScrollBars.Vertical;
            _txtSymptom.Location = new Point(16, ry + 22);
            _txtSymptom.Size = new Size(360, 96);
            _txtSymptom.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _txtSymptom.BackColor = Color.FromArgb(250, 251, 253);
            _txtSymptom.BorderStyle = BorderStyle.FixedSingle;
            right.Controls.Add(_txtSymptom);
            ry += 128;

            Label la = new Label();
            la.Text = "Extra log folder (used when the item is ticked)";
            la.AutoSize = true;
            la.ForeColor = Color.FromArgb(71, 85, 105);
            la.Location = new Point(16, ry);
            right.Controls.Add(la);

            _txtAppDir = new TextBox();
            _txtAppDir.Location = new Point(16, ry + 22);
            _txtAppDir.Size = new Size(268, 24);
            _txtAppDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            right.Controls.Add(_txtAppDir);
            Button bPick = MakeSmallButton("Browse...", Color.Gray);
            bPick.Size = new Size(80, 24);
            bPick.Location = new Point(292, ry + 22);
            bPick.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bPick.Click += delegate
            {
                FolderBrowserDialog f = new FolderBrowserDialog();
                f.Description = "Select the log folder of the application under test";
                if (f.ShowDialog(this) == DialogResult.OK) _txtAppDir.Text = f.SelectedPath;
            };
            right.Controls.Add(bPick);
            ry += 60;

            Label lp = new Label();
            lp.Text = "Process name for dump (blank = largest)";
            lp.AutoSize = true;
            lp.ForeColor = Color.FromArgb(71, 85, 105);
            lp.Location = new Point(16, ry);
            right.Controls.Add(lp);

            _txtProc = new TextBox();
            _txtProc.Location = new Point(16, ry + 22);
            _txtProc.Size = new Size(268, 24);
            _txtProc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            right.Controls.Add(_txtProc);
            ry += 56;

            Label hint = new Label();
            hint.Text = "Notes:\r\n- Do not use the machine while collecting, especially performance and energy items.\r\n- For BSOD issues tick \"Blue screen dumps\" and collect right after the reboot.\r\n- Some items need administrator rights; without them they are marked as skipped.";
            hint.AutoSize = false;
            hint.Size = new Size(360, 110);
            hint.Location = new Point(16, ry);
            hint.ForeColor = Color.Gray;
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            right.Controls.Add(hint);

            mid.Controls.Add(left, 0, 0);
            mid.Controls.Add(right, 1, 0);

            // ---------- footer ----------
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Fill;
            bottom.BackColor = Color.White;
            bottom.Margin = new Padding(0);

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

            _btnOpen = MakeButton("Open folder", Color.FromArgb(107, 114, 128), Color.White);
            _btnOpen.Size = new Size(110, 34);
            _btnOpen.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnOpen.Click += delegate { OpenFolder(); };
            bottom.Controls.Add(_btnOpen);

            _bar = new ProgressBar();
            _bar.Location = new Point(16, 52);
            _bar.Size = new Size(1000, 16);
            _bar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
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
            _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
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

        private void LayoutBottom()
        {
            int right = Width - 40 + 16;
            int gap = 6;
            int x = right;
            x -= _btnStart.Width; _btnStart.Location = new Point(x, 8);
            x -= _btnStop.Width + gap; _btnStop.Location = new Point(x, 8);
            x -= _btnReport.Width + gap; _btnReport.Location = new Point(x, 8);
            x -= _btnOpen.Width + gap; _btnOpen.Location = new Point(x, 8);
            x -= _btnBrowse.Width + gap; _btnBrowse.Location = new Point(x, 8);
            int left = 118;
            _txtOut.Location = new Point(left, 11);
            _txtOut.Width = Math.Max(120, x - left - 12);
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
            foreach (CheckBox cb in _checks.Values) cb.Checked = on;
        }

        private void ResetDefault()
        {
            foreach (JobDef d in _defs)
            {
                CheckBox cb;
                if (_checks.TryGetValue(d.Id, out cb)) cb.Checked = d.DefaultOn;
            }
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
            List<JobDef> picked = new List<JobDef>();
            foreach (JobDef d in _defs)
            {
                CheckBox cb;
                if (_checks.TryGetValue(d.Id, out cb) && cb.Checked) picked.Add(d);
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
            s.OperatorName = string.IsNullOrEmpty(_txtOperator.Text.Trim()) ? Environment.UserName : _txtOperator.Text.Trim();
            s.CaseId = _txtCase.Text.Trim();
            s.Symptom = _txtSymptom.Text.Trim();
            s.AppDir = _txtAppDir.Text.Trim();
            s.ProcessName = _txtProc.Text.Trim();

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
            ResetScroll();
            DialogResult r = MessageBox.Show(this,
                "Collection finished.\r\n\r\nOpen the HTML report now?\r\nLocation: " + _lastDir,
                "Done", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) OpenReport();
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

        private void OpenFolder()
        {
            try
            {
                string p = _lastDir;
                if (string.IsNullOrEmpty(p) || !Directory.Exists(p)) p = _txtOut.Text.Trim();
                if (Directory.Exists(p)) Process.Start("explorer.exe", "\"" + p + "\"");
                else MessageBox.Show(this, "Folder not found: " + p, "Notice");
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
