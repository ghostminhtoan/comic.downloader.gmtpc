using System;
using System.Net;
using Microsoft.Win32;
using System.Windows;

namespace get_link_manga
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 256);
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            PortableRuntimeBootstrap.EnsurePortableRuntime();
            PortableArchiveBootstrap.EnsurePortableSevenZip();
            EnsureLongPathSupport();
            try
            {
                System.IO.Directory.SetCurrentDirectory(PortablePaths.AppRoot);
            }
            catch
            {
            }

            base.OnStartup(e);
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private static void EnsureLongPathSupport()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem"))
                {
                    if (key == null)
                    {
                        return;
                    }

                    object currentValue = key.GetValue("LongPathsEnabled", 0);
                    int enabled = currentValue is int ? (int)currentValue : Convert.ToInt32(currentValue);
                    if (enabled != 1)
                    {
                        // ponytail: HKLM switch needed for Explorer; app can only flip it if running elevated.
                        key.SetValue("LongPathsEnabled", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
            }
        }
    }
}
