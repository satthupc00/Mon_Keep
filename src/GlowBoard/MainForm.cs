using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace GlowBoard
{
    /// <summary>
    /// Cửa sổ board: không viền, nằm dưới mọi cửa sổ khác như một widget trên desktop.
    /// Toàn bộ giao diện là trang Web/index.html chạy trong WebView2.
    /// </summary>
    public sealed class MainForm : Form
    {
        private const string AppTitle = "Mondiro GlowBoard";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string EmptyBoard = "{\"app\":\"Mondiro GlowBoard\",\"version\":1,\"items\":[]}";

        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mondiro", "GlowBoard");
        private static readonly string DefaultBoardPath = Path.Combine(DataDir, "board.gboard");
        private static readonly string WebDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mondiro", "GlowBoard", "WebView2");

        private readonly WebView2 _web;
        private readonly NotifyIcon _tray;
        private readonly Settings _settings;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly string _startupFile;
        private ToolStripMenuItem _autoStartItem;

        private string _currentFile;   // null = board chưa lưu thành file (tự lưu vào DefaultBoardPath)
        private bool _peek;            // tạm thời cho board nổi lên trên các cửa sổ khác
        private bool _quitting;
        private bool _pageLoaded;
        private bool _dialogOpen;

        public MainForm(string startupFile)
        {
            _startupFile = startupFile;
            _settings = Settings.Load(Path.Combine(DataDir, "settings.ini"));

            Text = AppTitle;
            Icon = LoadAppIcon(new Size(Scale(32), Scale(32)));
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(0x2e, 0x30, 0x35);   // màu viền (vùng kéo giãn)
            Padding = new Padding(Scale(5));
            MinimumSize = new Size(Scale(300), Scale(240));
            Bounds = InitialBounds();

            _web = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.FromArgb(0x45, 0x48, 0x4f),
            };
            Controls.Add(_web);

            _tray = new NotifyIcon { Icon = LoadAppIcon(SystemInformation.SmallIconSize), Text = AppTitle, Visible = true, ContextMenuStrip = BuildTrayMenu() };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowBoard(); };

            ResizeEnd += (s, e) => SaveBounds();
        }

        private int Scale(int px) => (int)Math.Round(px * DeviceDpi / 96.0);

        private static Icon LoadAppIcon(Size size)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("GlowBoard.icon.ico"))
                    return new Icon(s, size);
            }
            catch { return SystemIcons.Application; }
        }

        private Rectangle InitialBounds()
        {
            var r = new Rectangle(_settings.GetInt("X", int.MinValue), _settings.GetInt("Y", int.MinValue),
                                  _settings.GetInt("W", 0), _settings.GetInt("H", 0));
            bool visible = r.Width > 0 && r.Height > 0 &&
                           Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(1, r.Width - 80), Scale(30))));
            return visible ? r : DefaultBounds();
        }

        private Rectangle DefaultBounds()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int w = Scale(400), h = Scale(560);
            return new Rectangle(wa.Right - w - Scale(40), wa.Top + Scale(40), w, h);
        }

        private void SaveBounds()
        {
            if (WindowState != FormWindowState.Normal) return;
            _settings.Set("X", Left); _settings.Set("Y", Top); _settings.Set("W", Width); _settings.Set("H", Height);
            _settings.Save();
        }

        // ================= Khay hệ thống =================
        private ContextMenuStrip BuildTrayMenu()
        {
            var m = new ContextMenuStrip();
            var show = new ToolStripMenuItem("Hiện board", null, (s, e) => ShowBoard()) { Font = new Font(m.Font, FontStyle.Bold) };
            m.Items.Add(show);
            m.Items.Add("Ẩn board", null, (s, e) => HideBoard());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Board mới", null, (s, e) => { ShowBoard(); Post(new { type = "requestNew" }); });
            m.Items.Add("Mở board…", null, (s, e) => { ShowBoard(); Post(new { type = "requestOpen" }); });
            m.Items.Add("Lưu", null, (s, e) => { ShowBoard(); Post(new { type = "requestSave", @as = false }); });
            m.Items.Add("Lưu thành…", null, (s, e) => { ShowBoard(); Post(new { type = "requestSave", @as = true }); });
            m.Items.Add(new ToolStripSeparator());
            _autoStartItem = new ToolStripMenuItem("Khởi động cùng Windows", null, (s, e) => ToggleAutoStart()) { Checked = IsAutoStart() };
            m.Items.Add(_autoStartItem);
            m.Items.Add("Đưa board về vị trí mặc định", null, (s, e) => { Bounds = DefaultBounds(); SaveBounds(); ShowBoard(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Thoát", null, async (s, e) => await QuitAsync());
            m.Opening += (s, e) => _autoStartItem.Checked = IsAutoStart();
            return m;
        }

        private static bool IsAutoStart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k?.GetValue(AppTitle) != null;
        }

        private void ToggleAutoStart()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (IsAutoStart()) k.DeleteValue(AppTitle, false);
                    else k.SetValue(AppTitle, "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch (Exception ex) { MessageBox.Show("Không đổi được cài đặt khởi động: " + ex.Message, AppTitle); }
            _autoStartItem.Checked = IsAutoStart();
        }

        /// <summary>Hiện board và cho nó nổi lên trên tạm thời, tới khi bạn click ra chỗ khác.</summary>
        private void ShowBoard()
        {
            _peek = true;
            if (!Visible) Show();
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOP, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
            Activate();
            _web.Focus();
        }

        private void HideBoard()
        {
            Post(new { type = "flush" });
            Hide();
            if (!_settings.GetBool("HintShown"))
            {
                _settings.Set("HintShown", 1); _settings.Save();
                _tray.ShowBalloonTip(3000, AppTitle, "GlowBoard vẫn đang chạy ở khay hệ thống. Click vào icon để hiện lại board.", ToolTipIcon.Info);
            }
        }

        private void SendToBottom()
        {
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        // ================= Chế độ widget trên desktop =================
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;   // không hiện trên taskbar / Alt+Tab
                cp.ExStyle &= ~NativeMethods.WS_EX_APPWINDOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Gắn board vào desktop (Progman) để nó không bị ẩn khi bấm Win+D
            var desktop = NativeMethods.FindWindow("Progman", null);
            if (desktop != IntPtr.Zero) NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GWLP_HWNDPARENT, desktop);
            // Bo góc trên Windows 11
            try { int round = 2; NativeMethods.DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SendToBottom();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_peek && !_dialogOpen) { _peek = false; SendToBottom(); }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_SHOWME)
            {
                ShowBoard();
                return;
            }
            if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING && !_peek)
            {
                // Luôn giữ board nằm dưới các cửa sổ khác
                var wp = (NativeMethods.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.WINDOWPOS));
                if ((wp.flags & NativeMethods.SWP_NOZORDER) == 0)
                {
                    wp.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                    Marshal.StructureToPtr(wp, m.LParam, false);
                }
            }
            base.WndProc(ref m);

            if (m.Msg == NativeMethods.WM_NCHITTEST && (int)m.Result == 1 /* HTCLIENT */)
            {
                // Kéo các cạnh viền để mở rộng / thu nhỏ board
                var p = PointToClient(new Point(unchecked((short)(long)m.LParam), unchecked((short)((long)m.LParam >> 16))));
                int g = Padding.Left + Scale(2);
                bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < g, b = p.Y >= ClientSize.Height - g;
                int hit = t && l ? NativeMethods.HTTOPLEFT : t && r ? NativeMethods.HTTOPRIGHT
                        : b && l ? NativeMethods.HTBOTTOMLEFT : b && r ? NativeMethods.HTBOTTOMRIGHT
                        : l ? NativeMethods.HTLEFT : r ? NativeMethods.HTRIGHT
                        : t ? NativeMethods.HTTOP : b ? NativeMethods.HTBOTTOM : 0;
                if (hit != 0) m.Result = (IntPtr)hit;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_quitting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;   // Alt+F4 chỉ thu board xuống khay
                HideBoard();
                return;
            }
            SaveBounds();
            _tray.Visible = false;
            base.OnFormClosing(e);
        }

        private async Task QuitAsync()
        {
            try
            {
                if (_pageLoaded)
                {
                    string r = await _web.CoreWebView2.ExecuteScriptAsync("serialize()");
                    var text = _json.Deserialize<string>(r);
                    if (!string.IsNullOrEmpty(text)) WriteBoard(_currentFile ?? DefaultBoardPath, text);
                }
            }
            catch { /* vẫn thoát được dù không lưu kịp */ }
            _quitting = true;
            Close();
        }

        // ================= WebView2 =================
        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try
            {
                Directory.CreateDirectory(WebDataDir);
                var env = await CoreWebView2Environment.CreateAsync(null, WebDataDir);
                await _web.EnsureCoreWebView2Async(env);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                if (MessageBox.Show("GlowBoard cần Microsoft Edge WebView2 Runtime (có sẵn trên Windows 11).\n\nMở trang tải về ngay?",
                        AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    Process.Start("https://go.microsoft.com/fwlink/p/?LinkId=2124703");
                _quitting = true; Close(); return;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không khởi động được GlowBoard:\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _quitting = true; Close(); return;
            }

            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.WebMessageReceived += OnWebMessage;
            core.NewWindowRequested += (s, a) => { a.Handled = true; OpenLink(a.Uri); };
            core.NavigationStarting += (s, a) =>
            {
                if (!_pageLoaded) return;
                a.Cancel = true;          // không cho trang bị điều hướng đi chỗ khác
                OpenLink(a.Uri);
            };
            core.NavigationCompleted += (s, a) => _pageLoaded = true;
            _web.NavigateToString(ReadPage());
        }

        private static string ReadPage()
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("GlowBoard.index.html"))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }

        private void Post(object msg)
        {
            if (_web.CoreWebView2 == null) return;
            _web.CoreWebView2.PostWebMessageAsString(_json.Serialize(msg));
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg;
            try { msg = _json.Deserialize<Dictionary<string, object>>(e.TryGetWebMessageAsString()); }
            catch { return; }
            string Str(string k) => msg.TryGetValue(k, out var v) ? v as string : null;

            switch (Str("type"))
            {
                case "ready": LoadInitialBoard(); break;
                case "autosave": TryWrite(_currentFile ?? DefaultBoardPath, Str("text")); break;
                case "save": SaveBoard(Str("text"), msg.TryGetValue("as", out var a) && a is bool b && b); break;
                case "open": OpenBoard(); break;
                case "new": NewBoard(); break;
                case "hide": HideBoard(); break;
                case "openLink": OpenLink(Str("url")); break;
                case "drag":
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
                    SaveBounds();
                    break;
            }
        }

        // ================= File board =================
        private void LoadInitialBoard()
        {
            foreach (var path in new[] { _startupFile, _settings.Get("LastFile") })
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                var text = ReadBoard(path, false);
                if (text == null) continue;
                SetCurrentFile(path);
                Post(new { type = "load", text, file = Path.GetFileName(path) });
                return;
            }
            SetCurrentFile(null);
            Post(new { type = "load", text = File.Exists(DefaultBoardPath) ? ReadBoard(DefaultBoardPath, false) : null });
        }

        private void SaveBoard(string text, bool saveAs)
        {
            if (text == null) return;
            string path = _currentFile;
            if (saveAs || path == null)
            {
                using (var d = new SaveFileDialog
                {
                    Title = "Lưu board",
                    Filter = "GlowBoard (*.gboard)|*.gboard",
                    DefaultExt = "gboard",
                    FileName = path != null ? Path.GetFileName(path) : "Board.gboard",
                    InitialDirectory = path != null ? Path.GetDirectoryName(path) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                })
                {
                    if (ShowDialogOnTop(d) != DialogResult.OK) { Post(new { type = "saveCancelled" }); return; }
                    path = d.FileName;
                }
            }
            if (!TryWrite(path, text)) { Post(new { type = "saveCancelled" }); return; }
            SetCurrentFile(path);
            Post(new { type = "saved", file = Path.GetFileName(path) });
        }

        private void OpenBoard()
        {
            using (var d = new OpenFileDialog
            {
                Title = "Mở board",
                Filter = "GlowBoard (*.gboard)|*.gboard|Tất cả file|*.*",
                InitialDirectory = _currentFile != null ? Path.GetDirectoryName(_currentFile) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (ShowDialogOnTop(d) != DialogResult.OK) return;
                var text = ReadBoard(d.FileName, true);
                if (text == null) return;
                SetCurrentFile(d.FileName);
                Post(new { type = "load", text, file = Path.GetFileName(d.FileName) });
            }
        }

        private void NewBoard()
        {
            SetCurrentFile(null);
            TryWrite(DefaultBoardPath, EmptyBoard);
            Post(new { type = "load", text = EmptyBoard });
        }

        private DialogResult ShowDialogOnTop(CommonDialog d)
        {
            _peek = true;   // giữ board nổi trong lúc hộp thoại mở
            _dialogOpen = true;
            try { return d.ShowDialog(this); }
            finally { _dialogOpen = false; }
        }

        private void SetCurrentFile(string path)
        {
            _currentFile = path;
            _settings.Set("LastFile", path ?? "");
            _settings.Save();
            _tray.Text = path == null ? AppTitle : Truncate(AppTitle + " – " + Path.GetFileName(path), 63);
        }

        private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        /// <summary>Đọc file và kiểm tra có đúng là board GlowBoard không (tránh ghi đè nhầm file khác).</summary>
        private string ReadBoard(string path, bool showError)
        {
            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                var obj = _json.DeserializeObject(text) as Dictionary<string, object>;
                if (obj == null || !(obj.TryGetValue("items", out var items) && items is object[]))
                    throw new InvalidDataException("File này không phải board GlowBoard.");
                return text;
            }
            catch (Exception ex)
            {
                if (showError) Post(new { type = "error", message = "Không mở được file: " + ex.Message });
                return null;
            }
        }

        private bool TryWrite(string path, string text)
        {
            if (text == null) return false;
            try { WriteBoard(path, text); return true; }
            catch (Exception ex)
            {
                Post(new { type = "error", message = "Không lưu được: " + ex.Message });
                return false;
            }
        }

        private static void WriteBoard(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); return; }
                catch (IOException) { }
                File.Copy(tmp, path, true);
                File.Delete(tmp);
            }
            else File.Move(tmp, path);
        }

        // ================= Mở link =================
        private static readonly string[] BlockedExt = { ".exe", ".bat", ".cmd", ".com", ".msi", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".scr", ".lnk", ".reg", ".hta", ".cpl", ".pif" };

        private void OpenLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(url);
                    return;
                }
                string path = url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(url).LocalPath : url;
                if (Directory.Exists(path)) { Process.Start("explorer.exe", "\"" + path + "\""); return; }
                if (File.Exists(path))
                {
                    if (BlockedExt.Contains(Path.GetExtension(path).ToLowerInvariant()))
                    {
                        Process.Start("explorer.exe", "/select,\"" + path + "\"");   // chỉ mở thư mục chứa file chạy được, không chạy nó
                        return;
                    }
                    Process.Start(path);
                    return;
                }
                Post(new { type = "error", message = "Không tìm thấy: " + url });
            }
            catch (Exception ex)
            {
                Post(new { type = "error", message = "Không mở được link: " + ex.Message });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tray?.Dispose(); _web?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
