using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;

namespace get_link_manga
{
    public partial class App : Application
    {
        private static Mutex _singleInstanceMutex;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            PortableRuntimeBootstrap.EnsurePortableRuntime();
            _singleInstanceMutex = new Mutex(true, BuildSingleInstanceMutexName(), out bool createdNew);
            if (!createdNew)
            {
                TryActivateExistingInstance();
                Shutdown();
                return;
            }

            // Tự động kiểm tra và cài đặt Cloudflare Warp nếu chưa có
            EnsureCloudflareWarpInstalled();

            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 256);
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;

            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
            EnsureHardwareAcceleration();

            PortableArchiveBootstrap.EnsurePortableSevenZip();
            EnsureLongPathSupport();
            try
            {
                System.IO.Directory.SetCurrentDirectory(PortablePaths.AppRoot);
            }
            catch
            {
            }

            WireShutdownPersistenceHandlers();
            base.OnStartup(e);
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                (MainWindow as MainWindow)?.DisposeMangadexBrowserDrivers();
            }
            catch
            {
            }

            TryFlushRuntimeState();
            UnwireShutdownPersistenceHandlers();
            try
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            catch
            {
            }

            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            base.OnExit(e);
        }

        private static string BuildSingleInstanceMutexName()
        {
            string normalizedRoot = PortablePaths.AppRoot.Trim().ToUpperInvariant();
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedRoot));
                var builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                Debug.Assert(builder.Length > 0, "Mutex hash must not be empty.");
                return @"Local\Comic-GMTPC-" + builder.ToString();
            }
        }

        private static void TryActivateExistingInstance()
        {
            try
            {
                string currentRoot = PortablePaths.AppRoot;
                using (Process current = Process.GetCurrentProcess())
                {
                    foreach (Process process in Process.GetProcessesByName(current.ProcessName))
                    {
                        if (process.Id == current.Id)
                        {
                            continue;
                        }

                        try
                        {
                            string processFilePath = process.MainModule != null ? process.MainModule.FileName : null;
                            string processRoot = string.IsNullOrWhiteSpace(processFilePath)
                                ? string.Empty
                                : PortablePaths.NormalizeDirectoryPath(System.IO.Path.GetDirectoryName(processFilePath));
                            if (!string.Equals(currentRoot, processRoot, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch
                        {
                            continue;
                        }

                        IntPtr handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero)
                        {
                            continue;
                        }

                        ShowWindowAsync(handle, SW_RESTORE);
                        SetForegroundWindow(handle);
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        private void WireShutdownPersistenceHandlers()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            SessionEnding += App_SessionEnding;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void UnwireShutdownPersistenceHandlers()
        {
            DispatcherUnhandledException -= App_DispatcherUnhandledException;
            SessionEnding -= App_SessionEnding;
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
            TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
        }

        private static void TryFlushRuntimeState()
        {
            try
            {
                (Current?.MainWindow as MainWindow)?.FlushRuntimeStateForShutdown();
            }
            catch
            {
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            TryWriteCrashLog("DispatcherUnhandledException", e.Exception);
            TryFlushRuntimeState();
            e.Handled = true;
        }

        private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            TryFlushRuntimeState();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            TryWriteCrashLog("UnhandledException", e.ExceptionObject as Exception);
            TryFlushRuntimeState();
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            TryFlushRuntimeState();
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            TryWriteCrashLog("UnobservedTaskException", e.Exception);
            TryFlushRuntimeState();
            e.SetObserved();
        }

        private static void TryWriteCrashLog(string source, Exception exception)
        {
            try
            {
                string dir = System.IO.Path.Combine(PortablePaths.PortableTempRoot, "crash");
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                string text = $"[{DateTime.Now:O}] {source}{Environment.NewLine}{exception}";
                System.IO.File.WriteAllText(path, text, Encoding.UTF8);
            }
            catch
            {
            }
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

        private static void EnsureCloudflareWarpInstalled()
        {
            try
            {
                if (IsCloudflareWarpRegistered())
                {
                    return;
                }

                // Cài đặt Warp tự động ngầm
                string url = "https://downloads.cloudflareclient.com/v1/download/windows/version/2026.6.880.0";
                string tempDir = System.IO.Path.Combine(PortablePaths.AppRoot, ".tmp");
                System.IO.Directory.CreateDirectory(tempDir);
                string tempFilePath = System.IO.Path.Combine(tempDir, "CloudflareWarpInstallerTemp");

                using (var client = new WebClient())
                {
                    client.DownloadFile(new Uri(url), tempFilePath);
                }

                string finalPath = tempFilePath;
                if (!System.IO.Path.HasExtension(tempFilePath) || System.IO.Path.GetExtension(tempFilePath).ToLower() != ".msi")
                {
                    finalPath = tempFilePath + ".msi";
                    if (System.IO.File.Exists(finalPath))
                    {
                        System.IO.File.Delete(finalPath);
                    }
                    System.IO.File.Move(tempFilePath, finalPath);
                }

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = finalPath,
                    Arguments = "/passive",
                    UseShellExecute = true
                });

                if (process != null)
                {
                    process.WaitForExit();
                }

                // Mở WARP sau khi cài xong
                string pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
                string pfx86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";
                
                string warpPath = System.IO.Path.Combine(pf, @"Cloudflare\Cloudflare WARP\cloudflare WARP.exe");
                if (!System.IO.File.Exists(warpPath))
                {
                    warpPath = System.IO.Path.Combine(pfx86, @"Cloudflare\Cloudflare WARP\cloudflare WARP.exe");
                }

                if (System.IO.File.Exists(warpPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c start \"\" \"{warpPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                }
            }
            catch
            {
            }
        }

        private static bool IsCloudflareWarpRegistered()
        {
            string[] uninstallKeys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var keyPath in uninstallKeys)
            {
                using (RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = localMachine.OpenSubKey(keyPath))
                {
                    if (key != null && CheckUninstallSubKeysForCloudflare(key)) return true;
                }

                using (RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (RegistryKey key = localMachine.OpenSubKey(keyPath))
                {
                    if (key != null && CheckUninstallSubKeysForCloudflare(key)) return true;
                }

                using (RegistryKey currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                using (RegistryKey key = currentUser.OpenSubKey(keyPath))
                {
                    if (key != null && CheckUninstallSubKeysForCloudflare(key)) return true;
                }

                using (RegistryKey currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32))
                using (RegistryKey key = currentUser.OpenSubKey(keyPath))
                {
                    if (key != null && CheckUninstallSubKeysForCloudflare(key)) return true;
                }
            }

            return false;
        }

        private static bool CheckUninstallSubKeysForCloudflare(RegistryKey key)
        {
            foreach (var subkeyName in key.GetSubKeyNames())
            {
                using (RegistryKey subkey = key.OpenSubKey(subkeyName))
                {
                    if (subkey == null) continue;
                    object displayName = subkey.GetValue("DisplayName");
                    if (displayName != null && displayName.ToString().IndexOf("cloudflare", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
