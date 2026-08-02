using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

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

                string resourceName = GetRuntimeResourceName();
                if (!string.IsNullOrWhiteSpace(resourceName))
                {
                    string destination = Path.Combine(binFolder, LoaderFileName);
                    ExtractEmbeddedResource(resourceName, destination);
                }

                SetDllDirectory(binFolder);
                EnsureBinAssemblies();
                EnsureSqliteInterop();
                EnsureSeleniumManager();
                MigrateLegacyTempRoot();
            }
            catch
            {
                // Best effort only. If the native loader cannot be extracted,
                // WebView2 will fall back to whatever runtime is available.
            }
        }

        private static void EnsureSeleniumManager()
        {
            try
            {
                string binFolder = Path.Combine(PortablePaths.AppRoot, "bin");
                string destination = Path.Combine(binFolder, "selenium-manager", "windows", "selenium-manager.exe");

                if (!File.Exists(destination))
                {
                    string resourceName = "selenium-manager/windows/selenium-manager.exe";
                    ExtractEmbeddedResource(resourceName, destination);
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

                string[] assemblies = new[]
                {
                    "WebDriver.dll",
                    "Newtonsoft.Json.dll",
                    "Microsoft.Web.WebView2.Core.dll",
                    "Microsoft.Web.WebView2.WinForms.dll",
                    "Microsoft.Web.WebView2.Wpf.dll",
                    "System.Data.SQLite.dll"
                };

                foreach (string dll in assemblies)
                {
                    string destination = Path.Combine(binFolder, dll);
                    if (!File.Exists(destination))
                    {
                        ExtractEmbeddedResource(dll, destination);
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
                string rid = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" : "x86";
                string binFolder = Path.Combine(PortablePaths.AppRoot, "bin");
                string destination = Path.Combine(binFolder, "SQLite.Interop.dll");

                if (!File.Exists(destination))
                {
                    string resourceName = $"{rid}/SQLite.Interop.dll";
                    ExtractEmbeddedResource(resourceName, destination);
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
