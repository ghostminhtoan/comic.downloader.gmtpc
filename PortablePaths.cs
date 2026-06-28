using System;
using System.IO;
using System.Runtime.InteropServices;

namespace get_link_manga
{
    internal static class PortablePaths
    {
        internal static string AppRoot
        {
            get
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.GetFullPath(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }

        internal static string PortableDataRoot => Path.Combine(AppRoot, ".portable");

        internal static string WebView2RuntimeRoot => Path.Combine(RuntimeRoot, "webview2");

        internal static string RuntimeRoot => Path.Combine(AppRoot, "runtimes");

        internal static string WebView2UserDataFolder => Path.Combine(WebView2RuntimeRoot, "userdata");

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
    }
}
