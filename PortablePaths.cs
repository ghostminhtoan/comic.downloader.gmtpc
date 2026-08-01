using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace get_link_manga
{
    internal static class PortablePaths
    {
        private const int FILE_SHARE_READ = 0x00000001;
        private const int FILE_SHARE_WRITE = 0x00000002;
        private const int FILE_SHARE_DELETE = 0x00000004;
        private const int OPEN_EXISTING = 3;
        private const int FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const int FILE_NAME_NORMALIZED = 0x0;

        private static readonly Lazy<string> _appRoot = new Lazy<string>(ResolveAppRoot);
        private static readonly Lazy<string> _webView2UserDataFolder = new Lazy<string>(ResolveWebView2UserDataFolder);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            int dwDesiredAccess,
            int dwShareMode,
            IntPtr lpSecurityAttributes,
            int dwCreationDisposition,
            int dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetFinalPathNameByHandle(
            SafeFileHandle hFile,
            StringBuilder lpszFilePath,
            int cchFilePath,
            int dwFlags);

        internal static string AppRoot
        {
            get
            {
                return _appRoot.Value;
            }
        }

        internal static string PortableDataRoot => Path.Combine(AppRoot, ".portable");

        internal static string WebView2RuntimeRoot => Path.Combine(RuntimeRoot, "webview2");

        internal static string RuntimeRoot => Path.Combine(AppRoot, "bin", "runtimes");

        internal static string WebView2UserDataFolder => _webView2UserDataFolder.Value;

        internal static string WebView2CaptchaUserDataFolder => Path.Combine(WebView2UserDataFolder, "captcha");

        internal static string DefaultDownloadRoot => Path.Combine(AppRoot, "root");

        internal static string PortableTempRoot => Path.Combine(AppRoot, ".tmp");

        internal static string PortableGalleryListPath => Path.Combine(AppRoot, "save gallery.md");

        internal static string SevenZipRoot => Path.Combine(PortableDataRoot, "7-Zip");

        internal static string SevenZipExePath => Path.Combine(SevenZipRoot, "7z.exe");

        internal static string FastStoneRoot => Path.Combine(PortableDataRoot, "FastStone Image Viewer");

        internal static string FastStoneExePath => Path.Combine(FastStoneRoot, "FSViewer.exe");

        internal static string BandiviewRoot => Path.Combine(PortableDataRoot, "Bandiview");

        internal static string BandiviewExePath => Path.Combine(BandiviewRoot, "BandiView.exe");

        internal static string XnConvertInstallerPath => Path.Combine(PortableDataRoot, "XnConvert.Portable.exe");

        internal static string KnightComicInstallerPath => Path.Combine(PortableDataRoot, "KnightComic.exe");

        internal static string PortableArchivePath => Path.Combine(AppRoot, "Comic-GMTPC.zip");

        internal static string LegacyPortableArchivePath => Path.Combine(AppRoot, "Comic-GMTPC.7z");

        internal static string NormalizeDirectoryPath(string path)
        {
            string fullPath = Path.GetFullPath((path ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            try
            {
                using (SafeFileHandle handle = CreateFile(
                    fullPath,
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero))
                {
                    if (handle == null || handle.IsInvalid)
                    {
                        return fullPath;
                    }

                    var buffer = new StringBuilder(512);
                    int length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, FILE_NAME_NORMALIZED);
                    if (length <= 0)
                    {
                        return fullPath;
                    }

                    if (length > buffer.Capacity)
                    {
                        buffer.Capacity = length;
                        length = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, FILE_NAME_NORMALIZED);
                        if (length <= 0)
                        {
                            return fullPath;
                        }
                    }

                    string normalized = buffer.ToString(0, length);
                    if (normalized.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    {
                        normalized = @"\\" + normalized.Substring(8);
                    }
                    else if (normalized.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                    {
                        normalized = normalized.Substring(4);
                    }

                    return Path.GetFullPath(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                }
            }
            catch
            {
                return fullPath;
            }
        }

        internal static string GetRuntimeNativeFolder()
        {
            string rid;
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                rid = "win-arm64";
            }
            else if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
            {
                rid = "win-x64";
            }
            else
            {
                rid = "win-x86";
            }

            return Path.Combine(RuntimeRoot, rid, "native");
        }

        private static string ResolveAppRoot()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return NormalizeDirectoryPath(baseDir);
        }

        private static string ResolveWebView2UserDataFolder()
        {
            string newPath = Path.Combine(PortableDataRoot, "webview2", "userdata");
            string legacyPath = Path.Combine(WebView2RuntimeRoot, "userdata");

            try
            {
                if (!Directory.Exists(newPath) && Directory.Exists(legacyPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                    Directory.Move(legacyPath, newPath);
                }
            }
            catch
            {
                // ponytail: migration lỗi thì giữ app chạy với path mới. Khi cần cứu phiên cũ, copy tay từ legacyPath sang newPath.
            }

            Directory.CreateDirectory(newPath);
            return newPath;
        }
    }
}
