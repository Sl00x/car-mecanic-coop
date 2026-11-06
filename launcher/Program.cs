using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CarMecaSim.Launcher
{
    internal static class Program
    {
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                // CLI mode: attach to the parent console so output is visible.
                AttachConsole(ATTACH_PARENT_PROCESS);
                return Cli.Run(args);
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            return 0;
        }
    }
}
