using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace GlowBoard
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Sau khi tự cập nhật: chờ bản cũ thoát hẳn rồi mới chạy
            int waitIdx = Array.IndexOf(args, "--wait");
            if (waitIdx >= 0 && waitIdx + 1 < args.Length && int.TryParse(args[waitIdx + 1], out int pid))
            {
                try { Process.GetProcessById(pid).WaitForExit(15000); } catch { /* đã thoát */ }
            }
            bool updated = args.Contains("--updated");
            string file = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || args[i - 1] != "--wait")).FirstOrDefault();

            // Chỉ cho chạy 1 bản GlowBoard cùng lúc
            using (var mutex = new Mutex(true, "Mondiro.GlowBoard.SingleInstance", out bool first))
            {
                if (!first)
                {
                    NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST, NativeMethods.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Updater.CleanupOldFiles();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(file, updated));
            }
        }
    }
}
