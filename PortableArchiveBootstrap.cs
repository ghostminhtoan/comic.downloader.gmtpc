using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Net;

namespace get_link_manga
{
    internal static class PortableArchiveBootstrap
    {
        private const string FastStoneResourcePrefix = "FastStone Image Viewer/";
        private const string SevenZipDownloadUrl = "https://github.com/ghostminhtoan/getlink210-GMTPC/releases/download/accessories/7-Zip.zip";

        internal static void EnsurePortableSevenZip()
        {
            if (File.Exists(PortablePaths.SevenZipExePath))
            {
                return;
            }

            try
            {
                DownloadAndExtractSevenZip();
            }
            catch
            {
                // Best effort only. Compression features will validate the tool again
                // when the user clicks the archive buttons.
            }
        }

        internal static void EnsurePortableFastStone()
        {
            try
            {
                string localSource = Path.Combine(PortablePaths.AppRoot, "FastStone Image Viewer");
                if (Directory.Exists(localSource))
                {
                    CopyDirectory(localSource, PortablePaths.FastStoneRoot);
                    return;
                }

                ExtractEmbeddedResourceTree(FastStoneResourcePrefix, PortablePaths.FastStoneRoot);
            }
            catch
            {
                // Best effort only. Reader actions will validate the tool again
                // when the user opens FastStone Image Viewer.
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                if (!File.Exists(destFile) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(destFile))
                {
                    File.Copy(file, destFile, true);
                }
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }

        private static void ExtractEmbeddedResourceTree(string resourcePrefix, string destinationRoot)
        {
            Directory.CreateDirectory(destinationRoot);

            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly
                .GetManifestResourceNames()
                .Where(name => name.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (string resourceName in resourceNames)
            {
                string relativePath = resourceName.Substring(resourcePrefix.Length)
                    .Replace('/', Path.DirectorySeparatorChar);
                string destinationPath = Path.Combine(destinationRoot, relativePath);
                ExtractEmbeddedResource(assembly, resourceName, destinationPath);
            }
        }

        private static void DownloadAndExtractSevenZip()
        {
            Directory.CreateDirectory(PortablePaths.PortableDataRoot);

            if (Directory.Exists(PortablePaths.SevenZipRoot))
            {
                Directory.Delete(PortablePaths.SevenZipRoot, true);
            }

            string tempZipPath = Path.Combine(Path.GetTempPath(), $"7-Zip-{Guid.NewGuid():N}.zip");
            try
            {
                using (var client = new WebClient())
                {
                    client.DownloadFile(SevenZipDownloadUrl, tempZipPath);
                }

                ZipFile.ExtractToDirectory(tempZipPath, PortablePaths.PortableDataRoot);
            }
            finally
            {
                if (File.Exists(tempZipPath))
                {
                    File.Delete(tempZipPath);
                }
            }
        }

        private static void ExtractEmbeddedResource(Assembly assembly, string resourceName, string destinationPath)
        {
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
    }
}
