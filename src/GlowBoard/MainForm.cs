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

        private static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mondiro", "GlowBoard");
        private static readonly string LegacyBoardPath = Path.Combine(DataDir, "board.gboard");
        private static readonly string WebDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mondiro", "GlowBoard", "WebView2");

        private readonly WebView2 _web;
        private readonly NotifyIcon _tray;
        private readonly Settings _settings;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private readonly string _startupFile;
        private ToolStripMenuItem _autoStartItem;

        private bool _peek;            // tạm thời cho board nổi lên trên các cửa sổ khác
        private bool _onTop;           // tuỳ chọn: luôn nằm trên mọi cửa sổ
        private ToolStripMenuItem _onTopItem;
        private ToolStripMenuItem _notifyItem;
        private bool _quitting;
        private bool _pageLoaded;

        private readonly Updater _updater = new Updater();
        private readonly Timer _updateTimer = new Timer();
        private readonly bool _justUpdated;
        private bool _checking;

        public MainForm(string startupFile, bool justUpdated)
        {
            _startupFile = startupFile;
            _justUpdated = justUpdated;
            _settings = Settings.Load(Path.Combine(DataDir, "settings.ini"));
            _onTop = _settings.GetBool("OnTop");

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
            _tray.BalloonTipClicked += (s, e) => ShowBoard();

            ResizeEnd += (s, e) => SaveBounds();

            // Kiểm tra bản mới: 30 giây sau khi mở, sau đó mỗi 2 giờ.
            // Có bản mới thì tải sẵn, đợi lúc bạn không dùng board mới khởi động lại.
            _updateTimer.Interval = 30 * 1000;
            _updateTimer.Tick += async (s, e) =>
            {
                if (_updater.StagedVersion != null) { await TryApplyUpdateAsync(); return; }
                _updateTimer.Interval = 2 * 60 * 60 * 1000;
                await CheckUpdateAsync(false);
            };
            _updateTimer.Start();
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
            m.Items.Add("Chế độ Admin…", null, (s, e) => { ShowBoard(); Post(new { type = "admin" }); });
            m.Items.Add("Rời team / nhập mã khác", null, (s, e) => { ShowBoard(); Post(new { type = "leave" }); });
            m.Items.Add(new ToolStripSeparator());
            _notifyItem = new ToolStripMenuItem("Thông báo khi team thay đổi", null, (s, e) =>
            {
                _settings.Set("Notify", NotifyOn ? 0 : 1); _settings.Save();
                _notifyItem.Checked = NotifyOn;
            }) { Checked = NotifyOn };
            m.Items.Add(_notifyItem);
            _onTopItem = new ToolStripMenuItem("Luôn nằm trên cùng", null, (s, e) => SetOnTop(!_onTop)) { Checked = _onTop };
            m.Items.Add(_onTopItem);
            _autoStartItem = new ToolStripMenuItem("Khởi động cùng Windows", null, (s, e) => ToggleAutoStart()) { Checked = IsAutoStart() };
            m.Items.Add(_autoStartItem);
            m.Items.Add("Đưa board về vị trí mặc định", null, (s, e) => { Bounds = DefaultBounds(); SaveBounds(); ShowBoard(); });
            m.Items.Add("Kiểm tra cập nhật", null, async (s, e) => await CheckUpdateAsync(true));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("GlowBoard v" + Updater.CurrentVersion) { Enabled = false });
            m.Items.Add("Thoát", null, async (s, e) => await QuitAsync());
            m.Opening += (s, e) => _autoStartItem.Checked = IsAutoStart();
            return m;
        }

        private bool NotifyOn => _settings.GetInt("Notify", 1) != 0;

        private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

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

        /// <summary>Bật / tắt chế độ luôn nằm trên cùng (tắt thì board lại nằm dưới như widget).</summary>
        private void SetOnTop(bool on)
        {
            _onTop = on;
            _settings.Set("OnTop", on ? 1 : 0); _settings.Save();
            if (_onTopItem != null) _onTopItem.Checked = on;
            TopMost = on;
            if (!on) SendToBottom();
            Post(new { type = "onTop", on });
        }

        private void SendToBottom()
        {
            if (_onTop) return;
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
            if (_onTop) TopMost = true; else SendToBottom();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_peek) { _peek = false; SendToBottom(); }
        }

        // ================= Tự cập nhật =================
        private async Task CheckUpdateAsync(bool manual)
        {
            if (_checking) return;
            _checking = true;
            try
            {
                var v = await _updater.CheckAndStageAsync();
                if (v == null)
                {
                    if (manual) _tray.ShowBalloonTip(3000, AppTitle, "Bạn đang dùng bản mới nhất (v" + Updater.CurrentVersion + ").", ToolTipIcon.Info);
                    return;
                }
                _updateTimer.Interval = 30 * 1000;   // đã tải xong, đợi lúc thích hợp để cài
                _updateTimer.Start();
                if (manual) await TryApplyUpdateAsync(true);
            }
            catch (Exception ex)
            {
                if (manual) _tray.ShowBalloonTip(4000, AppTitle, "Không kiểm tra được cập nhật: " + ex.Message, ToolTipIcon.Warning);
            }
            finally { _checking = false; }
        }

        /// <summary>Cài bản mới khi board đang ẩn hoặc bạn không thao tác trên board (hoặc ngay lập tức nếu bạn bấm Kiểm tra cập nhật).</summary>
        private async Task TryApplyUpdateAsync(bool now = false)
        {
            if (_updater.StagedVersion == null || _quitting) return;
            if (!now && Visible && ContainsFocus) return;
            try
            {
                await SaveBoardNowAsync();
                _updater.Apply();
            }
            catch (Exception ex)
            {
                _tray.ShowBalloonTip(5000, AppTitle, "Không cài được bản cập nhật (" + ex.Message + "). Hãy giải nén GlowBoard vào thư mục không cần quyền admin, ví dụ D:\\Apps\\GlowBoard.", ToolTipIcon.Warning);
                _updateTimer.Stop();
                return;
            }
            SaveBounds();
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--updated --wait " + Process.GetCurrentProcess().Id) { UseShellExecute = false });
            _quitting = true;
            Close();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_SHOWME)
            {
                ShowBoard();
                return;
            }
            if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING && !_peek && !_onTop)
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
            try { await SaveBoardNowAsync(); }
            catch { /* vẫn thoát được dù không lưu kịp */ }
            _quitting = true;
            Close();
        }

        /// <summary>Đẩy các thay đổi đang chờ lên Firebase (bản lưu trên máy sẽ tự đồng bộ nếu đang mất mạng).</summary>
        private async Task SaveBoardNowAsync()
        {
            if (!_pageLoaded) return;
            await _web.CoreWebView2.ExecuteScriptAsync("window.flushNow && window.flushNow()");
            await Task.Delay(600);
        }

        // ================= WebView2 =================
        private const string AppOrigin = "https://glowboard.app/";

        protected override async void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CoreWebView2Environment env;
            try
            {
                Directory.CreateDirectory(WebDataDir);
                env = await CoreWebView2Environment.CreateAsync(null, WebDataDir);
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
                if (a.Uri.StartsWith(AppOrigin, StringComparison.OrdinalIgnoreCase)) return;
                a.Cancel = true;          // không cho trang bị điều hướng đi chỗ khác
                OpenLink(a.Uri);
            };
            core.NavigationCompleted += (s, a) => _pageLoaded = true;

            // Giao diện (index.html, fb.js) nằm sẵn trong exe, được phục vụ dưới địa chỉ https://glowboard.app/
            // để Firebase có "origin" riêng và lưu được đăng nhập + dữ liệu offline trên máy.
            core.AddWebResourceRequestedFilter(AppOrigin + "*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (s, a) =>
            {
                var path = new Uri(a.Request.Uri).AbsolutePath.TrimStart('/');
                if (path.Length == 0) path = "index.html";
                var data = ReadResource(path);
                a.Response = data == null
                    ? env.CreateWebResourceResponse(null, 404, "Not Found", "")
                    : env.CreateWebResourceResponse(new MemoryStream(data), 200, "OK",
                        "Content-Type: " + (path.EndsWith(".js") ? "text/javascript" : "text/html") + "; charset=utf-8\r\nCache-Control: no-store");
            };
            core.Navigate(AppOrigin + "index.html");
        }

        private static byte[] ReadResource(string path)
        {
            string name = path == "index.html" ? "GlowBoard.index.html" : path == "fb.js" ? "GlowBoard.fb.js" : null;
            if (name == null) return null;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var m = new MemoryStream())
            {
                if (s == null) return null;
                s.CopyTo(m);
                return m.ToArray();
            }
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
                case "ready":
                    Post(new { type = "onTop", on = _onTop });
                    if (_justUpdated) Post(new { type = "toast", message = "Đã cập nhật GlowBoard lên v" + Updater.CurrentVersion });
                    break;
                case "getLegacy": Post(new { type = "legacy", text = ReadLegacyBoard() }); break;
                case "hide": HideBoard(); break;
                case "notify":
                    // Thông báo Windows khi người khác thay đổi board (đã gom 2 phút ở phía giao diện)
                    if (NotifyOn)
                        _tray.ShowBalloonTip(8000, Truncate(Str("title") ?? AppTitle, 63), Truncate(Str("text") ?? "", 250), ToolTipIcon.Info);
                    break;
                case "toggleTop": SetOnTop(!_onTop); break;
                case "openLink": OpenLink(Str("url")); break;
                case "drag":
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
                    SaveBounds();
                    break;
            }
        }

        /// <summary>Board cá nhân của các bản trước (file .gboard trên máy) – để Admin nhập vào board team.</summary>
        private string ReadLegacyBoard()
        {
            foreach (var path in new[] { _startupFile, _settings.Get("LastFile"), LegacyBoardPath })
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                try
                {
                    var text = File.ReadAllText(path, Encoding.UTF8);
                    if (_json.DeserializeObject(text) is Dictionary<string, object> obj && obj.TryGetValue("items", out var items)
                        && items is object[] arr && arr.Length > 0)
                        return text;
                }
                catch { }
            }
            return null;
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
            if (disposing) { _updateTimer.Dispose(); _tray?.Dispose(); _web?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
