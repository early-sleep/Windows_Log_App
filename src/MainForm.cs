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
        private Button _btnStart, _btnStop, _btnOpen, _btnReport, _btnElevate;
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
            Text = "笔记本日志采集工具  v" + Program.Version;
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

            // ---------- 顶栏 ----------
            Panel top = new Panel();
            top.Dock = DockStyle.Fill;
            top.BackColor = Dark;
            top.Margin = new Padding(0);

            Label title = new Label();
            title.Text = "笔记本日志采集工具";
            title.ForeColor = Color.White;
            title.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(20, 12);
            top.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "选择需要采集的内容，生成可直接交给研发的日志包与 HTML 报告";
            sub.ForeColor = Color.FromArgb(178, 196, 224);
            sub.AutoSize = true;
            sub.Location = new Point(22, 46);
            top.Controls.Add(sub);

            _lblMachine = new Label();
            _lblMachine.Text = "正在读取机器信息…";
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

            _btnElevate = MakeSmallButton("以管理员身份重启", Color.FromArgb(72, 98, 145));
            _btnElevate.Size = new Size(140, 24);
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
                catch (Exception ex) { MessageBox.Show(this, "提权失败：" + ex.Message, "提示"); }
            };
            top.Controls.Add(_btnElevate);

            top.Resize += delegate { LayoutTop(top); };
            LayoutTop(top);

            // ---------- 中部 ----------
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
            // 快捷按钮
            Panel quick = new Panel();
            quick.Location = new Point(0, 0);
            quick.Size = new Size(600, 30);
            Button bAll = MakeSmallButton("全选", Accent);
            bAll.Location = new Point(0, 0); bAll.Size = new Size(64, 26);
            bAll.Click += delegate { SetAll(true); };
            Button bNone = MakeSmallButton("全不选", Color.Gray);
            bNone.Location = new Point(72, 0); bNone.Size = new Size(72, 26);
            bNone.Click += delegate { SetAll(false); };
            Button bDef = MakeSmallButton("恢复默认", Color.Gray);
            bDef.Location = new Point(152, 0); bDef.Size = new Size(82, 26);
            bDef.Click += delegate { ResetDefault(); };
            Label tip = new Label();
            tip.Text = "勾选需要采集的内容（蓝色为默认推荐）";
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
            rt.Text = "现场信息（选填，会写进报告）";
            rt.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            rt.ForeColor = Dark;
            rt.AutoSize = true;
            rt.Location = new Point(16, 12);
            right.Controls.Add(rt);

            int ry = 44;
            ry = AddField(right, "用例编号", ref _txtCase, ry, false);
            ry = AddField(right, "采集人", ref _txtOperator, ry, false);
            _txtOperator.Text = Environment.UserName;

            Label ls = new Label();
            ls.Text = "现象描述";
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
            la.Text = "附加日志目录（勾选「附加日志目录」时生效）";
            la.AutoSize = true;
            la.ForeColor = Color.FromArgb(71, 85, 105);
            la.Location = new Point(16, ry);
            right.Controls.Add(la);

            _txtAppDir = new TextBox();
            _txtAppDir.Location = new Point(16, ry + 22);
            _txtAppDir.Size = new Size(268, 24);
            _txtAppDir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            right.Controls.Add(_txtAppDir);
            Button bPick = MakeSmallButton("浏览…", Color.Gray);
            bPick.Size = new Size(80, 24);
            bPick.Location = new Point(292, ry + 22);
            bPick.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bPick.Click += delegate
            {
                FolderBrowserDialog f = new FolderBrowserDialog();
                f.Description = "选择被测软件的日志目录";
                if (f.ShowDialog(this) == DialogResult.OK) _txtAppDir.Text = f.SelectedPath;
            };
            right.Controls.Add(bPick);
            ry += 60;

            Label lp = new Label();
            lp.Text = "进程名（进程内存转储用，如 chrome.exe，留空取最大）";
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
            hint.Text = "提示：\r\n· 采集期间请勿操作电脑，尤其是性能与能耗类采集。\r\n· 蓝屏问题请勾选「蓝屏转储」，并在重启后立即采集。\r\n· 部分项目需要管理员权限，普通用户运行时会被标记为跳过。";
            hint.AutoSize = false;
            hint.Size = new Size(360, 110);
            hint.Location = new Point(16, ry);
            hint.ForeColor = Color.Gray;
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            right.Controls.Add(hint);

            mid.Controls.Add(left, 0, 0);
            mid.Controls.Add(right, 1, 0);

            // ---------- 底部 ----------
            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Fill;
            bottom.BackColor = Color.White;
            bottom.Margin = new Padding(0);

            Label lo = new Label();
            lo.Text = "输出目录";
            lo.AutoSize = true;
            lo.ForeColor = Color.FromArgb(71, 85, 105);
            lo.Location = new Point(16, 14);
            bottom.Controls.Add(lo);

            _txtOut = new TextBox();
            _txtOut.Location = new Point(80, 11);
            _txtOut.Size = new Size(560, 24);
            _txtOut.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "日志采集");
            _txtOut.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            bottom.Controls.Add(_txtOut);

            _btnStart = MakeButton("开始采集", Accent, Color.White);
            _btnStart.Size = new Size(120, 34);
            _btnStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStart.Click += delegate { StartCollect(); };
            bottom.Controls.Add(_btnStart);

            _btnStop = MakeButton("停止", Color.FromArgb(220, 38, 38), Color.White);
            _btnStop.Size = new Size(72, 34);
            _btnStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStop.Click += delegate
            {
                if (_cts != null) _cts.Cancel();
                EnableButton(_btnStop, false, Color.FromArgb(220, 38, 38));
                SetStatus("正在停止…");
            };
            bottom.Controls.Add(_btnStop);
            EnableButton(_btnStop, false, Color.FromArgb(220, 38, 38));

            _btnReport = MakeButton("查看报告", Color.FromArgb(16, 185, 129), Color.White);
            _btnReport.Size = new Size(96, 34);
            _btnReport.Enabled = false;
            _btnReport.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnReport.Click += delegate { OpenReport(); };
            bottom.Controls.Add(_btnReport);
            EnableButton(_btnReport, false, Color.FromArgb(16, 185, 129));

            _btnOpen = MakeButton("打开文件夹", Color.FromArgb(107, 114, 128), Color.White);
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
            _lblStatus.Text = "就绪";
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
            int w = Width - 40;
            int right = w + 16;
            _btnStart.Location = new Point(right - 120, 8);
            _btnStop.Location = new Point(right - 120 - 78, 8);
            _btnReport.Location = new Point(right - 120 - 78 - 102, 8);
            _btnOpen.Location = new Point(right - 120 - 78 - 102 - 116, 8);
            _txtOut.Width = Math.Max(120, right - 120 - 78 - 102 - 116 - 96 - 80);
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

        // ------------------------------------------------------------ 状态
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
                _lblAdmin.Text = "当前：管理员权限";
                _lblAdmin.ForeColor = Color.FromArgb(134, 239, 172);
                _btnElevate.Visible = false;
            }
            else
            {
                _lblAdmin.Text = "当前：普通用户，部分采集项不可用";
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
            if (!string.IsNullOrEmpty(name)) SetStatus("正在采集：" + name);
            else SetStatus("已完成 " + done + " / " + total);
        }

        // ------------------------------------------------------------ 采集
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
                MessageBox.Show(this, "请至少勾选一项采集内容。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string root = _txtOut.Text.Trim();
            try
            {
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "输出目录不可用：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Tool.IsElevated())
            {
                DialogResult r = MessageBox.Show(this,
                    "当前以普通用户运行，转储文件、能耗诊断、睡眠诊断等项目将无法采集。\r\n是否以管理员身份重新启动？",
                    "权限提示", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
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
                    catch (Exception ex) { MessageBox.Show(this, "提权失败，将继续以当前权限采集。" + ex.Message, "提示"); }
                    return;
                }
            }

            Session s = new Session();
            s.IsAdmin = Tool.IsElevated();
            s.Info = Tool.ReadMachine();
            if (s.Info.Serial.Length == 0)
            {
                SetStatus("正在读取设备序列号…");
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
                MessageBox.Show(this, "创建输出目录失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            AppendLog("输出目录：" + s.OutDir);
            AppendLog("共 " + picked.Count + " 项，开始采集…");

            Thread th = new Thread(delegate()
            {
                try { Runner.Run(s, picked); }
                catch (Exception ex) { AppendLog("采集异常：" + ex.Message); }
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
            SetStatus("采集完成，报告已生成");
            ResetScroll();
            DialogResult r = MessageBox.Show(this,
                "采集完成。\r\n\r\n是否立即打开 HTML 报告？\r\n文件位置：" + _lastDir,
                "完成", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) OpenReport();
        }

        private void OpenReport()
        {
            try
            {
                string p = Path.Combine(_lastDir, "report.html");
                if (File.Exists(p)) Process.Start(p);
                else MessageBox.Show(this, "报告文件不存在。", "提示");
            }
            catch (Exception ex) { MessageBox.Show(this, "打开失败：" + ex.Message, "提示"); }
        }

        private void OpenFolder()
        {
            try
            {
                string p = _lastDir;
                if (string.IsNullOrEmpty(p) || !Directory.Exists(p)) p = _txtOut.Text.Trim();
                if (Directory.Exists(p)) Process.Start("explorer.exe", "\"" + p + "\"");
                else MessageBox.Show(this, "目录不存在：" + p, "提示");
            }
            catch (Exception ex) { MessageBox.Show(this, "打开失败：" + ex.Message, "提示"); }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            UpdateAdminState();
            ResetScroll();
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
                DialogResult r = MessageBox.Show(this, "采集尚未结束，确定要退出吗？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                if (_cts != null) _cts.Cancel();
            }
            base.OnFormClosing(e);
        }
    }
}
