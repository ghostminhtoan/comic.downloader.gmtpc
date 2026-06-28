using System;
using System.Diagnostics;
using System.IO;

namespace get_link_manga
{
    internal static class ShellFolderLauncher
    {
        private static readonly object OpenLock = new object();
        private static string LastOpenedPath;
        private static DateTime LastOpenedAtUtc = DateTime.MinValue;
        private static readonly TimeSpan OpenCooldown = TimeSpan.FromSeconds(1.25);

        internal static bool TryOpenFolder(string path, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Đường dẫn thư mục trống.";
                return false;
            }

            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(path.Trim());
            }
            catch (Exception ex)
            {
                error = $"Đường dẫn không hợp lệ: {ex.Message}";
                return false;
            }

            lock (OpenLock)
            {
                if (string.Equals(LastOpenedPath, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                    DateTime.UtcNow - LastOpenedAtUtc < OpenCooldown)
                {
                    return true;
                }

                LastOpenedPath = normalizedPath;
                LastOpenedAtUtc = DateTime.UtcNow;
            }

            try
            {
                // Open through the shell so Windows can reuse an existing Explorer window
                // when possible, instead of always forcing a fresh explorer.exe process.
                Process.Start(new ProcessStartInfo
                {
                    FileName = normalizedPath,
                    UseShellExecute = true,
                    Verb = "open",
                    WorkingDirectory = normalizedPath
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
