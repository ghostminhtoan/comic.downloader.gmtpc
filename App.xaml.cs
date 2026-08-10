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

            // Tự động kiểm tra và cài đặt Codec WebP nếu chưa có
            EnsureWebpCodecInstalled();

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

        private static bool IsWebpCodecInstalled()
        {
            try
            {
                // 1. Kiểm tra CLSID WebP WIC Decoder trong Registry ở cả 64-bit và 32-bit views
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64))
                using (var key = baseKey.OpenSubKey(@"CLSID\{76c69830-e080-49a1-881a-69752cfd9072}"))
                {
                    if (key != null) return true;
                }
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32))
                using (var key = baseKey.OpenSubKey(@"CLSID\{76c69830-e080-49a1-881a-69752cfd9072}"))
                {
                    if (key != null) return true;
                }

                // 2. Quét đệ quy qua các Uninstall key để tìm tên "WebP Codec" hoặc "WebP for Windows"
                string[] paths = new string[] {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };

                foreach (var path in paths)
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var key = baseKey.OpenSubKey(path))
                    {
                        if (key != null)
                        {
                            foreach (var subKeyName in key.GetSubKeyNames())
                            {
                                using (var subKey = key.OpenSubKey(subKeyName))
                                {
                                    var displayName = subKey?.GetValue("DisplayName")?.ToString();
                                    if (displayName != null && (displayName.IndexOf("WebP Codec", StringComparison.OrdinalIgnoreCase) >= 0 || displayName.IndexOf("WebP for Windows", StringComparison.OrdinalIgnoreCase) >= 0))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    using (var key = baseKey.OpenSubKey(path))
                    {
                        if (key != null)
                        {
                            foreach (var subKeyName in key.GetSubKeyNames())
                            {
                                using (var subKey = key.OpenSubKey(subKeyName))
                                {
                                    var displayName = subKey?.GetValue("DisplayName")?.ToString();
                                    if (displayName != null && (displayName.IndexOf("WebP Codec", StringComparison.OrdinalIgnoreCase) >= 0 || displayName.IndexOf("WebP for Windows", StringComparison.OrdinalIgnoreCase) >= 0))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }

                // 3. Fallback: Thử giải mã bitmap WPF
                byte[] webpBytes = new byte[] {
                    0x52, 0x49, 0x46, 0x46, 0x1e, 0x00, 0x00, 0x00,
                    0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20,
                    0x12, 0x00, 0x00, 0x00, 0x50, 0x01, 0x00, 0x9d,
                    0x01, 0x2a, 0x01, 0x00, 0x01, 0x00, 0x03, 0x00,
                    0x34, 0x25, 0xa4, 0x00, 0x03, 0x70, 0x00, 0x00
                };
                
                using (var stream = new System.IO.MemoryStream(webpBytes))
                {
                    var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                        stream,
                        System.Windows.Media.Imaging.BitmapCreateOptions.None,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    
                    return decoder != null && decoder.Frames.Count > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureWebpCodecInstalled()
        {
            try
            {
                if (IsWebpCodecInstalled())
                {
                    return;
                }

                string message = "Máy bạn chưa cài Codec giải mã ảnh WebP (Google WebP WIC Codec).\nBạn có muốn tải và cài đặt Codec WebP ngay không? (Cần thiết để hiển thị hình ảnh xem trước và tránh lỗi tải ảnh WebP)";
                string title = "Cài đặt WebP Codec / WebP Codec Check";
                var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                string url = "https://storage.googleapis.com/downloads.webmproject.org/releases/webp/WebpCodecSetup.exe";
                string tempDir = System.IO.Path.Combine(PortablePaths.PortableTempRoot ?? System.IO.Path.Combine(PortablePaths.AppRoot, ".tmp"), "installers");
                System.IO.Directory.CreateDirectory(tempDir);
                string destFile = System.IO.Path.Combine(tempDir, "WebpCodecSetup.exe");

                using (var client = new WebClient())
                {
                    client.DownloadFile(new Uri(url), destFile);
                }

                var proc = Process.Start(destFile);
                if (proc != null)
                {
                    proc.WaitForExit();
                }

                MessageBox.Show(
                    "Đã cài đặt xong WebP Codec. Ứng dụng sẽ tự động khởi động lại để áp dụng thay đổi.",
                    "Thông báo / Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Khởi động lại ứng dụng để WIC Codec được cập nhật trong WPF
                string currentExe = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(currentExe);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Không thể cài đặt WebP Codec: {ex.Message}",
                    "Lỗi / Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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

                // Hỏi ý kiến người dùng trước khi cài
                string message = "Máy bạn chưa cài Cloudflare Warp (1.1.1.1).\nBạn có muốn cài 1.1.1.1 không? Nếu không cài thì sẽ không tải được truyện trên một số website.";
                string title = "Cài đặt Cloudflare Warp / Warp Installation Check";
                var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
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
