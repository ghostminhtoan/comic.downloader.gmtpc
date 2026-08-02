using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Net;

namespace get_link_manga
{
    internal static class PortableRuntimeBootstrap
    {
        private const string LoaderResourcePrefix = "runtimes/webview2/";
        private const string LoaderFileName = "WebView2Loader.dll";

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        internal static void EnsurePortableRuntime()
        {
            try
            {
                AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
                string binFolder = Path.Combine(PortablePaths.AppRoot, "bin");
                Directory.CreateDirectory(binFolder);

                SetDllDirectory(binFolder);
                EnsureBinAssemblies();
                EnsureSqliteInterop();
                EnsureWebView2Loader();
                MigrateLegacyTempRoot();
                EnsureSeleniumManager();
                EnsureRingtones();
            }
            catch
            {
            }
        }

        private static void EnsureRingtones()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                string ringtonesDir = Path.Combine(PortablePaths.AppRoot, "bin", "ringtones");
                Directory.CreateDirectory(ringtonesDir);

                var items = new[]
                {
                    new { Url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/accessories/ringtones-download-finish.wav", Name = "download-finish.wav" },
                    new { Url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/accessories/ringtones-error.wav", Name = "error.wav" },
                    new { Url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/accessories/ringtones-Startup.wav", Name = "Startup.wav" }
                };

                using (var client = new WebClient())
                {
                    foreach (var item in items)
                    {
                        string destPath = Path.Combine(ringtonesDir, item.Name);
                        if (!File.Exists(destPath))
                        {
                            client.DownloadFile(item.Url, destPath);
                        }
                    }
                }
            }
            catch
            {
            }
        }

         private static void EnsureSeleniumManager()
         {
             try
             {
                 ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                 string binDir = Path.Combine(PortablePaths.AppRoot, "bin");
                 Directory.CreateDirectory(binDir);
                 string exePath = Path.Combine(binDir, "selenium-manager.exe");

                 if (!File.Exists(exePath))
                 {
                     string url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/accessories/selenium-manager.exe";
                     using (var client = new WebClient())
                     {
                         client.DownloadFile(url, exePath);
                     }
                 }

                 if (File.Exists(exePath))
                 {
                     Environment.SetEnvironmentVariable("SE_MANAGER_PATH", exePath);
                 }

                 // Clean up legacy .bin directory if it exists
                 string legacyBin = Path.Combine(PortablePaths.AppRoot, ".bin");
                 if (Directory.Exists(legacyBin))
                 {
                     try
                     {
                         Directory.Delete(legacyBin, true);
                     }
                     catch {}
                 }
             }
             catch
             {
             }
         }

        private static void EnsureBinAssemblies()
        {
            try
            {
                string binFolder = Path.Combine(PortablePaths.AppRoot, "bin");
                Directory.CreateDirectory(binFolder);

                var items = new[]
                {
                    new { Name = "WebDriver.dll", Url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/runtimes/WebDriver.dll" },
                    new { Name = "Newtonsoft.Json.dll", Url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/runtimes/Newtonsoft.Json.dll" },
                    new { Name = "Microsoft.Web.WebView2.Core.dll", Url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/runtimes/Microsoft.Web.WebView2.Core.dll" },
                    new { Name = "Microsoft.Web.WebView2.WinForms.dll", Url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/runtimes/Microsoft.Web.WebView2.WinForms.dll" },
                    new { Name = "Microsoft.Web.WebView2.Wpf.dll", Url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/runtimes/Microsoft.Web.WebView2.Wpf.dll" },
                    new { Name = "System.Data.SQLite.dll", Url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/runtimes/System.Data.SQLite.dll" }
                };

                using (var client = new WebClient())
                {
                    foreach (var item in items)
                    {
                        string destination = Path.Combine(binFolder, item.Name);
                        if (!File.Exists(destination))
                        {
                            client.DownloadFile(item.Url, destination);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void EnsureSqliteInterop()
        {
            try
            {
                string binFolder = Path.Combine(PortablePaths.AppRoot, "bin");
                string destination = Path.Combine(binFolder, "SQLite.Interop.dll");

                if (!File.Exists(destination))
                {
                    string url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/runtimes/SQLite.Interop.dll";
                    using (var client = new WebClient())
                    {
                        client.DownloadFile(url, destination);
                    }
                }
            }
            catch
            {
            }
        }

        private static void EnsureWebView2Loader()
        {
            try
            {
                string binFolder = Path.Combine(PortablePaths.AppRoot, "bin");
                string destination = Path.Combine(binFolder, "WebView2Loader.dll");

                if (!File.Exists(destination))
                {
                    string url = "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/runtimes/WebView2Loader.dll";
                    using (var client = new WebClient())
                    {
                        client.DownloadFile(url, destination);
                    }
                }
            }
            catch
            {
            }
        }

        internal static void ResetPortableRuntimeStorage()
        {
            TryDeleteDirectory(PortablePaths.RuntimeRoot);
        }

        private static void MigrateLegacyTempRoot()
        {
            string legacyTempRoot = Path.Combine(PortablePaths.AppRoot, "root", ".tmp");
            string portableTempRoot = PortablePaths.PortableTempRoot;

            if (!Directory.Exists(legacyTempRoot) || string.Equals(legacyTempRoot, portableTempRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(portableTempRoot);
                foreach (string file in Directory.GetFiles(legacyTempRoot, "*", SearchOption.AllDirectories))
                {
                    string relative = file.Substring(legacyTempRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string target = Path.Combine(portableTempRoot, relative);
                    string targetDir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrWhiteSpace(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }
                    if (!File.Exists(target))
                    {
                        File.Copy(file, target, false);
                    }
                }

                TryDeleteDirectory(legacyTempRoot);
            }
            catch
            {
            }
        }

        private static string GetRuntimeResourceName()
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                return $"{LoaderResourcePrefix}win-arm64/native/{LoaderFileName}";
            }

            if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                return $"{LoaderResourcePrefix}win-x64/native/{LoaderFileName}";
            }

            return $"{LoaderResourcePrefix}win-x86/native/{LoaderFileName}";
        }

        private static void ExtractEmbeddedResource(string resourceName, string destinationPath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return;
                }

                string directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var fileStream = File.Open(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    stream.CopyTo(fileStream);
                }
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                DeleteDirectoryRecursive(path);
            }
            catch
            {
            }
        }

        private static void DeleteDirectoryRecursive(string path)
        {
            foreach (string file in Directory.GetFiles(path))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    try
                    {
                        string tempDir = PortablePaths.PortableTempRoot;
                        Directory.CreateDirectory(tempDir);
                        string tempPath = Path.Combine(tempDir, Guid.NewGuid().ToString() + ".bak");
                        File.Move(file, tempPath);
                    }
                    catch
                    {
                        try
                        {
                            File.Move(file, file + "." + Guid.NewGuid().ToString() + ".bak");
                        }
                        catch { }
                    }
                }
            }

            foreach (string dir in Directory.GetDirectories(path))
            {
                DeleteDirectoryRecursive(dir);
            }

            try
            {
                Directory.Delete(path, false);
            }
            catch
            {
                try
                {
                    string tempDir = PortablePaths.PortableTempRoot;
                    Directory.CreateDirectory(tempDir);
                    Directory.Move(path, Path.Combine(tempDir, Guid.NewGuid().ToString() + "_dir.bak"));
                }
                catch
                {
                }
            }
        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var assemblyName = new AssemblyName(args.Name);
                string dllPath = Path.Combine(PortablePaths.AppRoot, "bin", assemblyName.Name + ".dll");
                if (File.Exists(dllPath))
                {
                    return Assembly.LoadFrom(dllPath);
                }
            }
            catch
            {
            }
            return null;
        }
    }
}
