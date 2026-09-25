using System;
using System.Linq;
using System.Windows.Forms;

namespace ClassFirewall
{
    internal static class Program
    {
        public static bool SilentMode { get; private set; }

        [STAThread]
        static void Main(string[] args)
        {
            SilentMode = args.Any(a =>
                a.Equals("-silent", StringComparison.OrdinalIgnoreCase));

            ApplicationConfiguration.Initialize();

            var form = new MainForm();
            if (SilentMode)
                form.WindowState = FormWindowState.Minimized;

            Application.Run(form);
        }
    }
}