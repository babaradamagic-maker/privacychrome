using System;
using System.Windows.Forms;

namespace PrivacyChrome
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            using (var main = new MainForm())
            {
                // run with the created form so WinForms lifetime is clear
                Application.Run(main);
            }
        }
    }
}