using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace GlowBoard
{
    /// <summary>
    /// Tự cập nhật: mỗi lần push lên GitHub, GitHub Actions build và tạo Release mới.
    /// App hỏi GitHub bản Release mới nhất, tải về, thay file rồi tự khởi động lại.
    /// </summary>
    internal sealed class Updater
    {
        private const string Owner = "satthupc00";
        private const string Repo = "Mon_Keep";
        private const string AssetName = "GlowBoard_Windows_x64.zip";

        private static readonly string UpdateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mondiro", "GlowBoard", "update");

        public static Version CurrentVersion
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
        }

        public Version StagedVersion { get; private set; }
        private string _stagedDir;

        /// <summary>Kiểm tra bản mới; nếu có thì tải và giải nén sẵn. Trả về phiên bản mới, hoặc null nếu đang là bản mới nhất.</summary>
        public async Task<Version> CheckAndStageAsync()
        {
            if (StagedVersion != null) return StagedVersion;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            using (var api = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                api.DefaultRequestHeaders.UserAgent.ParseAdd("Mondiro-GlowBoard/" + CurrentVersion);

                var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                var res = await api.SendAsync(req).ConfigureAwait(false);
                if (res.StatusCode == HttpStatusCode.NotFound) return null;          // chưa có Release nào (hoặc repo chưa public)
                res.EnsureSuccessStatusCode();

                var json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                var latest = ParseVersion(json.TryGetValue("tag_name", out var t) ? t as string : null);
                if (latest == null || latest <= CurrentVersion) return null;

                var asset = (json.TryGetValue("assets", out var a) ? a as object[] : null)?
                    .OfType<Dictionary<string, object>>()
                    .FirstOrDefault(x => (x["name"] as string) == AssetName);
                if (asset == null) return null;

                // Tải file zip
                var zip = await api.GetByteArrayAsync((string)asset["browser_download_url"]).ConfigureAwait(false);

                // Giải nén vào thư mục tạm
                var dir = Path.Combine(UpdateDir, latest.ToString());
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                var zipPath = Path.Combine(UpdateDir, AssetName);
                File.WriteAllBytes(zipPath, zip);
                ZipFile.ExtractToDirectory(zipPath, dir);
                File.Delete(zipPath);
                if (!File.Exists(Path.Combine(dir, "GlowBoard.exe"))) throw new Exception("File cập nhật bị thiếu GlowBoard.exe.");

                _stagedDir = dir;
                StagedVersion = latest;
                return latest;
            }
        }

        /// <summary>Thay file của app bằng bản mới. Windows cho đổi tên file đang chạy, nên đổi file cũ thành .old rồi chép file mới vào.</summary>
        public void Apply()
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var moved = new List<string>();
            try
            {
                foreach (var src in Directory.GetFiles(_stagedDir, "*", SearchOption.AllDirectories))
                {
                    var rel = src.Substring(_stagedDir.Length).TrimStart('\\', '/');
                    var dst = Path.Combine(appDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (File.Exists(dst))
                    {
                        if (File.Exists(dst + ".old")) File.Delete(dst + ".old");
                        File.Move(dst, dst + ".old");
                        moved.Add(dst);
                    }
                    File.Copy(src, dst, true);
                }
            }
            catch
            {
                // Lỗi giữa chừng → trả lại file cũ
                foreach (var dst in moved)
                {
                    try { if (File.Exists(dst)) File.Delete(dst); File.Move(dst + ".old", dst); } catch { }
                }
                throw;
            }
            try { Directory.Delete(_stagedDir, true); } catch { }
        }

        /// <summary>Xoá file .old còn sót lại sau lần cập nhật trước.</summary>
        public static void CleanupOldFiles()
        {
            try
            {
                foreach (var f in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "*.old", SearchOption.AllDirectories))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }

        private static Version ParseVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            return Version.TryParse(tag.TrimStart('v', 'V'), out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
        }
    }
}
