using System;
using System.IO;
using System.Reflection;
using System.Windows;

[assembly: AssemblyTitle("DeX Bridge")]
[assembly: AssemblyDescription("USB desktop display receiver for Galaxy phones")]
[assembly: AssemblyCompany("DeX Bridge")]
[assembly: AssemblyProduct("DeX Bridge")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace DexBridge
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                string tools = null;
                for (int i = 0; i < args.Length; i++) if (args[i] == "--tools-dir" && i + 1 < args.Length) tools = Path.GetFullPath(args[++i]);
                app.DispatcherUnhandledException += (sender, e) =>
                {
                    MessageBox.Show("DeX Bridge encountered an unexpected error.\n\n" + e.Exception.Message, "DeX Bridge", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true; app.Shutdown(1);
                };
                using (var controller = new WindowController(AppDomain.CurrentDomain.BaseDirectory, true, tools))
                {
                    app.MainWindow = controller.Window;
                    app.Run(controller.Window);
                }
                return 0;
            }
            catch (Exception e)
            {
                MessageBox.Show("DeX Bridge could not start.\n\n" + e.Message, "DeX Bridge", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
