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

            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
            EnsureHardwareAcceleration();

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

        private static void EnsureHardwareAcceleration()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Avalon.Graphics"))
                {
                    if (key == null)
                    {
                        return;
                    }

                    object currentValue = key.GetValue("DisableHWAcceleration", 0);
                    int disabled = currentValue is int ? (int)currentValue : Convert.ToInt32(currentValue);
                    if (disabled != 0)
                    {
                        // ponytail: force hardware acceleration (DirectX 9+) to ensure smooth rendering
                        key.SetValue("DisableHWAcceleration", 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
            }
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

