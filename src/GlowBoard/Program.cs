using System;
using System.Threading;
using System.Windows.Forms;

namespace GlowBoard
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Chỉ cho chạy 1 bản GlowBoard cùng lúc
            using (var mutex = new Mutex(true, "Mondiro.GlowBoard.SingleInstance", out bool first))
            {
                if (!first)
                {
                    NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST, NativeMethods.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
            }
        }
    }
}
