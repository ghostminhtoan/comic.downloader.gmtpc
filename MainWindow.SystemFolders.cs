using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private readonly SemaphoreSlim _folderStructureSemaphore = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _archiveCts;

        private async void BtnCompressBooks_Click(object sender, RoutedEventArgs e)
        {
            if (IsArchiveOperationBlocked())
            {
                ShowLocalizedMessageBox(
                    "Stop active downloads before compressing books.",
                    "Hãy dừng tải đang chạy trước khi nén sách.",
                    "Information",
                    "Thông tin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string sourceFolder = PortablePaths.DefaultDownloadRoot;
            string archivePath = PortablePaths.PortableArchivePath;

            if (!Directory.Exists(sourceFolder))
            {
                ShowLocalizedMessageBox(
                    $"Cannot find folder to compress:\n{sourceFolder}",
                    $"Không tìm thấy folder để nén:\n{sourceFolder}",
                    "Information",
                    "Thông tin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            StartArchiveProgress(
                _isVietnameseUi ? "Đang nén sách..." : "Compressing books...",
                false);

            try
            {
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }

                _archiveCts = new CancellationTokenSource();
                await CreateZipArchiveWithProgressAsync(sourceFolder, archivePath, _archiveCts.Token);

                Directory.Delete(sourceFolder, true);

                Log($"[Archive] Đã nén portable folder thành công: {archivePath}");
                lblStatus.Text = _isVietnameseUi ? "Đã nén xong sách." : "Books compressed successfully.";
                ShowLocalizedMessageBox(
                    $"Compressed into file:\n{archivePath}\n\nSource folder was removed.",
                    $"Đã nén xong thành file:\n{archivePath}\n\nFolder gốc đã được xóa.",
                    "Success",
                    "Thành công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                Log("[Archive] Người dùng đã hủy thao tác nén.");
                lblStatus.Text = _isVietnameseUi ? "Đã hủy nén." : "Compression cancelled.";
                if (File.Exists(archivePath))
                {
                    try { File.Delete(archivePath); } catch { }
                }
            }
            catch (Exception ex)
            {
                Log($"[Archive Error] Không thể nén sách: {ex.Message}");
                lblStatus.Text = _isVietnameseUi ? "Nén sách thất bại." : "Compress failed.";
                if (File.Exists(archivePath))
                {
                    try { File.Delete(archivePath); } catch { }
                }
                MessageBox.Show($"Lỗi khi nén sách: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ResetArchiveProgress();
                _archiveCts?.Dispose();
                _archiveCts = null;
            }
        }

        private async void BtnExtractBooks_Click(object sender, RoutedEventArgs e)
        {
            if (IsArchiveOperationBlocked())
            {
                ShowLocalizedMessageBox(
                    "Stop active downloads before extracting books.",
                    "Hãy dừng tải đang chạy trước khi giải nén sách.",
                    "Information",
                    "Thông tin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string sourceFolder = PortablePaths.DefaultDownloadRoot;
            string archivePath = GetAvailablePortableArchivePath();

            if (!File.Exists(archivePath))
            {
                ShowLocalizedMessageBox(
                    $"Cannot find archive file:\n{PortablePaths.PortableArchivePath}\n{PortablePaths.LegacyPortableArchivePath}",
                    $"Không tìm thấy file nén:\n{PortablePaths.PortableArchivePath}\n{PortablePaths.LegacyPortableArchivePath}",
                    "Information",
                    "Thông tin",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            StartArchiveProgress(
                _isVietnameseUi ? "Đang giải nén sách..." : "Extracting books...",
                false);

            try
            {
                if (Directory.Exists(sourceFolder))
                {
                    Directory.Delete(sourceFolder, true);
                }

                _archiveCts = new CancellationTokenSource();
                if (string.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await ExtractZipArchiveWithProgressAsync(archivePath, PortablePaths.AppRoot, _archiveCts.Token);
                }
                else
                {
                    if (!EnsureSevenZipReady())
                    {
                        throw new InvalidOperationException("Không thể khởi tạo 7-Zip portable.");
                    }

                    string arguments = $"x -y \"{Path.GetFileName(archivePath)}\" -o\"{PortablePaths.AppRoot}\"";
                    string output = await RunSevenZipAsync(arguments);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Log($"[Archive] 7z: {output}");
                    }
                }

                if (!Directory.Exists(sourceFolder))
                {
                    throw new InvalidOperationException("Không bung ra folder sách.");
                }

                File.Delete(archivePath);

                Log($"[Archive] Đã giải nén portable folder thành công: {sourceFolder}");
                lblStatus.Text = _isVietnameseUi ? "Đã giải nén xong sách." : "Books extracted successfully.";
                ShowLocalizedMessageBox(
                    $"Extracted folder:\n{sourceFolder}\n\nArchive file was removed.",
                    $"Đã giải nén xong folder:\n{sourceFolder}\n\nFile nén đã được xóa.",
                    "Success",
                    "Thành công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                Log("[Archive] Người dùng đã hủy thao tác giải nén.");
                lblStatus.Text = _isVietnameseUi ? "Đã hủy giải nén." : "Extraction cancelled.";
            }
            catch (Exception ex)
            {
                Log($"[Archive Error] Không thể giải nén sách: {ex.Message}");
                lblStatus.Text = _isVietnameseUi ? "Giải nén sách thất bại." : "Extract failed.";
                MessageBox.Show($"Lỗi khi giải nén sách: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ResetArchiveProgress();
                _archiveCts?.Dispose();
                _archiveCts = null;
            }
        }

        private async void BtnMergeFolders_Click(object sender, RoutedEventArgs e)
        {
            string downloadRoot = txtDownloadPath.Text.Trim();
            if (string.IsNullOrEmpty(downloadRoot))
            {
                MessageBox.Show("Vui lòng chọn thư mục lưu trước (Please select a download folder first).", "Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(downloadRoot))
            {
                MessageBox.Show("Thư mục lưu không tồn tại (Download folder does not exist).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string targetFolder = GetActiveTargetFolder(downloadRoot);

            Log($"[Merge] Bắt đầu gộp thư mục tại: {targetFolder}");
            lblStatus.Text = _isVietnameseUi ? "Đang gộp thư mục..." : "Merging folders...";

            try
            {
                int mergedCount = await MergeFoldersInTargetFolderAsync(targetFolder, CancellationToken.None);

                Log("[Merge] Đang tạm ngừng 3 giây để hệ thống ổn định và nhận biết thư mục...");
                await Task.Delay(3000);

                if (mergedCount == 0)
                {
                    Log("[Merge] Không tìm thấy nhóm thư mục nào có tên giống nhau trước dấu gạch nối.");
                    lblStatus.Text = _isVietnameseUi ? "Gộp xong. Không có thư mục nào được gộp." : "Merge completed. No folders merged.";
                    MessageBox.Show("Không tìm thấy thư mục nào có tên giống nhau trước dấu gạch nối để gộp (No matching folders found to merge).", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Log($"[Merge] Hoàn tất gộp {mergedCount} thư mục.");
                lblStatus.Text = _isVietnameseUi ? $"Đã gộp {mergedCount} thư mục." : $"Merge completed. Merged {mergedCount} folders.";
                MessageBox.Show($"Đã gộp thành công {mergedCount} thư mục!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"[Merge Error] Lỗi nghiêm trọng khi gộp thư mục: {ex.Message}");
                lblStatus.Text = _isVietnameseUi ? "Gộp thư mục thất bại." : "Merge failed.";
                MessageBox.Show($"Lỗi khi gộp thư mục: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnSplitFolders_Click(object sender, RoutedEventArgs e)
        {
            string downloadRoot = txtDownloadPath.Text.Trim();
            if (string.IsNullOrEmpty(downloadRoot))
            {
                MessageBox.Show("Vui lòng chọn thư mục lưu trước (Please select a download folder first).", "Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(downloadRoot))
            {
                MessageBox.Show("Thư mục lưu không tồn tại (Download folder does not exist).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string targetFolder = GetActiveTargetFolder(downloadRoot);

            Log($"[Split] Bắt đầu tách thư mục tại: {targetFolder}");
            lblStatus.Text = _isVietnameseUi ? "Đang tách thư mục..." : "Splitting folders...";

            try
            {
                int splitCount = await SplitFoldersInTargetFolderAsync(targetFolder, CancellationToken.None);

                Log("[Split] Đang tạm ngừng 3 giây để hệ thống ổn định và nhận biết thư mục...");
                await Task.Delay(3000);

                Log($"[Split] Hoàn tất tách {splitCount} thư mục.");
                lblStatus.Text = _isVietnameseUi ? $"Đã tách {splitCount} thư mục." : $"Split completed. Split {splitCount} folders.";
                MessageBox.Show($"Đã tách thành công {splitCount} thư mục!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"[Split Error] Lỗi nghiêm trọng khi tách thư mục: {ex.Message}");
                lblStatus.Text = _isVietnameseUi ? "Tách thư mục thất bại." : "Split failed.";
                MessageBox.Show($"Lỗi khi tách thư mục: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnSplitSingleComicFolders_Click(object sender, RoutedEventArgs e)
        {
            string downloadRoot = GetSplitSingleComicRootPath();
            if (string.IsNullOrWhiteSpace(downloadRoot))
            {
                MessageBox.Show("Vui lòng chọn thư mục lưu trước (Please select a download folder first).", "Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(downloadRoot))
            {
                MessageBox.Show("Thư mục lưu không tồn tại (Download folder does not exist).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!TryGetSplitSingleComicGroupSize(out int groupSize))
            {
                return;
            }

            string targetFolder = downloadRoot;

            Log($"[Split] Bắt đầu chia single comic tại: {targetFolder} | group={groupSize}");
            lblStatus.Text = _isVietnameseUi ? $"Đang chia folder theo {groupSize} chap..." : $"Splitting folders by {groupSize} chapters...";

            try
            {
                int splitCount = await SplitSingleComicFoldersInTargetFolderAsync(targetFolder, groupSize, CancellationToken.None);

                Log("[Split] Đang tạm ngừng 3 giây để hệ thống ổn định và nhận biết thư mục...");
                await Task.Delay(3000);

                if (splitCount == 0)
                {
                    Log("[Split] Không tìm thấy chapter folder hợp lệ để chia.");
                    lblStatus.Text = _isVietnameseUi ? "Không tìm thấy chapter folder hợp lệ." : "No valid chapter folders found.";
                    MessageBox.Show("Không tìm thấy chapter folder hợp lệ để chia.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Log($"[Split] Hoàn tất chia {splitCount} chapter folder.");
                lblStatus.Text = _isVietnameseUi
                    ? $"Đã chia {splitCount} chapter folder."
                    : $"Split completed. Split {splitCount} chapter folders.";
                MessageBox.Show($"Đã chia thành công {splitCount} chapter folder!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"[Split Error] Lỗi nghiêm trọng khi chia folder: {ex.Message}");
                lblStatus.Text = _isVietnameseUi ? "Chia folder thất bại." : "Split failed.";
                MessageBox.Show($"Lỗi khi chia folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBrowseSplitSingleComicRoot_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowser
            {
                Title = _isVietnameseUi ? "Chọn folder gốc cần split" : "Select folder to split",
                SelectedPath = GetSplitSingleComicRootPath()
            };

            if (!dialog.ShowDialog(new System.Windows.Interop.WindowInteropHelper(this).Handle))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(dialog.SelectedPath) || !Directory.Exists(dialog.SelectedPath))
            {
                MessageBox.Show("Thư mục đã chọn không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (txtSplitSingleComicRoot != null)
            {
                txtSplitSingleComicRoot.Text = dialog.SelectedPath;
            }
        }

        private void BtnBrowseMergeSingleComicRoot_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowser
            {
                Title = _isVietnameseUi ? "Chọn folder gốc cần gộp" : "Select folder to merge",
                SelectedPath = GetMergeSingleComicRootPath()
            };

            if (!dialog.ShowDialog(new System.Windows.Interop.WindowInteropHelper(this).Handle))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(dialog.SelectedPath) || !Directory.Exists(dialog.SelectedPath))
            {
                MessageBox.Show("Thư mục đã chọn không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (txtMergeSingleComicRoot != null)
            {
                txtMergeSingleComicRoot.Text = dialog.SelectedPath;
            }
        }

        private async void BtnMergeSingleComicFolders_Click(object sender, RoutedEventArgs e)
        {
            string targetFolder = GetMergeSingleComicRootPath();
            if (string.IsNullOrWhiteSpace(targetFolder))
            {
                MessageBox.Show("Vui lòng chọn folder cần gộp trước.", "Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(targetFolder))
            {
                MessageBox.Show("Thư mục đã chọn không tồn tại.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Log($"[Merge] Bắt đầu gộp chapter tại: {targetFolder}");
            lblStatus.Text = _isVietnameseUi ? "Đang gộp folder..." : "Merging folders...";

            try
            {
                int mergedCount = await MergeSingleComicFoldersInTargetFolderAsync(targetFolder, CancellationToken.None);

                Log("[Merge] Đang tạm ngừng 3 giây để hệ thống ổn định và nhận biết thư mục...");
                await Task.Delay(3000);

                if (mergedCount == 0)
                {
                    Log("[Merge] Không tìm thấy chapter folder hợp lệ để gộp.");
                    lblStatus.Text = _isVietnameseUi ? "Không tìm thấy chapter folder hợp lệ." : "No valid chapter folders found.";
                    MessageBox.Show("Không tìm thấy chapter folder hợp lệ để gộp.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Log($"[Merge] Hoàn tất gộp {mergedCount} chapter folder.");
                lblStatus.Text = _isVietnameseUi
                    ? $"Đã gộp {mergedCount} chapter folder."
                    : $"Merge completed. Merged {mergedCount} chapter folders.";
                MessageBox.Show($"Đã gộp thành công {mergedCount} chapter folder!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"[Merge Error] Lỗi nghiêm trọng khi gộp folder: {ex.Message}");
                lblStatus.Text = _isVietnameseUi ? "Gộp folder thất bại." : "Merge failed.";
                MessageBox.Show($"Lỗi khi gộp folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartArchiveProgress(string statusText, bool showPauseButton)
        {
            Dispatcher.Invoke(() =>
            {
                if (archiveProgressPanel != null)
                {
                    archiveProgressPanel.Visibility = Visibility.Visible;
                }

                if (btnArchivePause != null)
                {
                    btnArchivePause.IsEnabled = showPauseButton;
                    btnArchivePause.Content = "PAUSE";
                }

                if (btnArchiveStop != null)
                {
                    btnArchiveStop.IsEnabled = true;
                }

                if (btnCompressBooks != null)
                {
                    btnCompressBooks.IsEnabled = false;
                }

                if (btnExtractBooks != null)
                {
                    btnExtractBooks.IsEnabled = false;
                }

                UpdateArchiveProgressUi(0, 1, statusText);
            });
        }

        private void UpdateArchiveProgressUi(int completed, int total, string statusText = null)
        {
            int safeTotal = Math.Max(1, total);
            int safeCompleted = Math.Max(0, Math.Min(completed, safeTotal));
            double percent = safeTotal <= 0 ? 0 : (safeCompleted * 100d) / safeTotal;

            Dispatcher.Invoke(() =>
            {
                if (archiveProgressBar != null)
                {
                    archiveProgressBar.Minimum = 0;
                    archiveProgressBar.Maximum = 100;
                    archiveProgressBar.Value = Math.Max(0, Math.Min(100, percent));
                }

                if (txtArchiveProgressValue != null)
                {
                    txtArchiveProgressValue.Text = $"{percent:0}%";
                }

                if (!string.IsNullOrWhiteSpace(statusText))
                {
                    lblStatus.Text = statusText;
                }
            });
        }

        private void ResetArchiveProgress()
        {
            Dispatcher.Invoke(() =>
            {
                if (archiveProgressBar != null)
                {
                    archiveProgressBar.Value = 0;
                }

                if (txtArchiveProgressValue != null)
                {
                    txtArchiveProgressValue.Text = "0%";
                }

                if (archiveProgressPanel != null)
                {
                    archiveProgressPanel.Visibility = Visibility.Collapsed;
                }

                if (btnArchivePause != null)
                {
                    btnArchivePause.IsEnabled = false;
                    btnArchivePause.Content = "PAUSE";
                }

                if (btnArchiveStop != null)
                {
                    btnArchiveStop.IsEnabled = false;
                }

                if (btnCompressBooks != null)
                {
                    btnCompressBooks.IsEnabled = true;
                }

                if (btnExtractBooks != null)
                {
                    btnExtractBooks.IsEnabled = true;
                }
            });
        }

        private bool IsArchiveOperationBlocked()
        {
            return _scrapedItems.Any(item =>
                string.Equals(item.Status, "Downloading", StringComparison.OrdinalIgnoreCase));
        }

        private bool EnsureSevenZipReady()
        {
            PortableArchiveBootstrap.EnsurePortableSevenZip();
            return File.Exists(PortablePaths.SevenZipExePath);
        }

        private string GetAvailablePortableArchivePath()
        {
            if (File.Exists(PortablePaths.PortableArchivePath))
            {
                return PortablePaths.PortableArchivePath;
            }

            if (File.Exists(PortablePaths.LegacyPortableArchivePath))
            {
                return PortablePaths.LegacyPortableArchivePath;
            }

            return PortablePaths.PortableArchivePath;
        }

        private async Task<string> RunSevenZipAsync(string arguments)
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PortablePaths.SevenZipExePath,
                    Arguments = arguments,
                    WorkingDirectory = PortablePaths.AppRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process == null)
                    {
                        throw new InvalidOperationException("Không thể khởi chạy 7z.exe.");
                    }

                    string standardOutput = process.StandardOutput.ReadToEnd();
                    string standardError = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        string errorText = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                        throw new InvalidOperationException($"7z exit code {process.ExitCode}. {errorText}".Trim());
                    }

                    string mergedOutput = string.Join(Environment.NewLine,
                        new[] { standardOutput, standardError }.Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text.Trim()));

                    return CompactSingleLine(mergedOutput);
                }
            });
        }

        private async Task CreateZipArchiveWithProgressAsync(string sourceFolder, string archivePath, CancellationToken token)
        {
            await Task.Run(() =>
            {
                string rootName = Path.GetFileName(sourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                using (var stream = new FileStream(archivePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    int total = Math.Max(1, files.Count);
                    int completed = 0;

                    foreach (string filePath in files)
                    {
                        token.ThrowIfCancellationRequested();

                        string relativePath = GetRelativePathSafe(sourceFolder, filePath);
                        string entryName = Path.Combine(rootName, relativePath).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                        archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Fastest);

                        completed++;
                        UpdateArchiveProgressUi(completed, total, _isVietnameseUi
                            ? $"Đang nén: {Path.GetFileName(filePath)}"
                            : $"Compressing: {Path.GetFileName(filePath)}");
                    }
                }
            }, token);

            UpdateArchiveProgressUi(1, 1, _isVietnameseUi ? "Nén xong." : "Compression complete.");
        }

        private async Task ExtractZipArchiveWithProgressAsync(string archivePath, string destinationRoot, CancellationToken token)
        {
            await Task.Run(() =>
            {
                using (var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false))
                {
                    var entries = archive.Entries.Where(entry => !string.IsNullOrWhiteSpace(entry.FullName)).ToList();
                    int total = Math.Max(1, entries.Count);
                    int completed = 0;

                    foreach (ZipArchiveEntry entry in entries)
                    {
                        token.ThrowIfCancellationRequested();

                        string destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                        else
                        {
                            entry.ExtractToFile(destinationPath, overwrite: true);
                        }

                        completed++;
                        UpdateArchiveProgressUi(completed, total, _isVietnameseUi
                            ? $"Đang giải nén: {entry.Name}"
                            : $"Extracting: {entry.Name}");
                    }
                }
            }, token);

            UpdateArchiveProgressUi(1, 1, _isVietnameseUi ? "Giải nén xong." : "Extraction complete.");
        }

        private static string GetRelativePathSafe(string basePath, string fullPath)
        {
            string baseNormalized = Path.GetFullPath(basePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Uri baseUri = new Uri(baseNormalized);
            Uri fullUri = new Uri(Path.GetFullPath(fullPath));
            string relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString());
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string CompactSingleLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Length);
            bool lastWasWhitespace = false;
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastWasWhitespace)
                    {
                        builder.Append(' ');
                        lastWasWhitespace = true;
                    }

                    continue;
                }

                builder.Append(ch);
                lastWasWhitespace = false;
            }

            return builder.ToString().Trim();
        }

        internal async Task AutoMergeChapterFolderAsync(string unmergedPath, string mergedPath, CancellationToken token)
        {
            string source = _isSingleComicFolderType ? unmergedPath : mergedPath;
            string dest = _isSingleComicFolderType ? mergedPath : unmergedPath;

            if (string.IsNullOrWhiteSpace(source) ||
                string.IsNullOrWhiteSpace(dest) ||
                string.Equals(source, dest, StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(source))
            {
                return;
            }

            await _folderStructureSemaphore.WaitAsync(token);
            try
            {
                string destParent = Path.GetDirectoryName(dest);
                if (string.IsNullOrEmpty(destParent))
                {
                    return;
                }

                Directory.CreateDirectory(destParent);
                if (Directory.Exists(dest))
                {
                    if (string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                    MergeDirectoryContents(source, dest);
                }
                else
                {
                    Directory.Move(source, dest);
                }

                Log($"[Auto Merge] Đã gộp tự động '{Path.GetFileName(source)}' -> '{dest}'");
            }
            finally
            {
                _folderStructureSemaphore.Release();
            }
        }

        private async Task<int> MergeFoldersInTargetFolderAsync(string targetFolder, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
            {
                return 0;
            }

            await _folderStructureSemaphore.WaitAsync(token);
            try
            {
                var directories = Directory.GetDirectories(targetFolder);
                var groups = directories
                    .Select(dir => new { Path = dir, Name = Path.GetFileName(dir) })
                    .Where(d => !ShouldIgnoreFolderStructureAction(d.Name))
                    .Where(d => d.Name.Contains("-"))
                    .Select(d =>
                    {
                        int index = d.Name.IndexOf('-');
                        string prefix = d.Name.Substring(0, index).Trim();
                        string suffix = d.Name.Substring(index + 1).Trim();
                        return new { d.Path, d.Name, Prefix = prefix, Suffix = suffix };
                    })
                    .GroupBy(d => d.Prefix, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() >= 2)
                    .ToList();

                int mergedCount = 0;
                foreach (var group in groups)
                {
                    string groupPrefix = group.Key;
                    string destParentDir = Path.Combine(targetFolder, groupPrefix);
                    Directory.CreateDirectory(destParentDir);

                    foreach (var item in group)
                    {
                        string destDir = Path.Combine(destParentDir, item.Suffix);

                        try
                        {
                            if (string.Equals(item.Path, destDir, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (!Directory.Exists(destDir))
                            {
                                Directory.Move(item.Path, destDir);
                            }
                            else
                            {
                                MergeDirectoryContents(item.Path, destDir);
                            }

                            mergedCount++;
                            Log($"[Merge] Đã gộp '{item.Name}' -> '{groupPrefix}\\{item.Suffix}'");
                        }
                        catch (Exception ex)
                        {
                            Log($"[Merge Error] Không thể gộp thư mục '{item.Name}': {ex.Message}");
                        }
                    }
                }

                return mergedCount;
            }
            finally
            {
                _folderStructureSemaphore.Release();
            }
        }

        private async Task<int> SplitFoldersInTargetFolderAsync(string targetFolder, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
            {
                return 0;
            }

            await _folderStructureSemaphore.WaitAsync(token);
            try
            {
                var directories = Directory.GetDirectories(targetFolder);
                int splitCount = 0;

                foreach (var dir in directories)
                {
                    string parentName = Path.GetFileName(dir);
                    if (ShouldIgnoreFolderStructureAction(parentName))
                    {
                        continue;
                    }

                    var subDirs = Directory.GetDirectories(dir);
                    if (subDirs.Length < 2)
                    {
                        continue;
                    }

                    foreach (var subDir in subDirs)
                    {
                        string subName = Path.GetFileName(subDir);
                        string destName = $"{parentName}-{subName}";
                        string destPath = Path.Combine(targetFolder, destName);

                        try
                        {
                            if (!Directory.Exists(destPath))
                            {
                                Directory.Move(subDir, destPath);
                            }
                            else
                            {
                                MergeDirectoryContents(subDir, destPath);
                            }

                            splitCount++;
                            Log($"[Split] Đã tách '{parentName}\\{subName}' -> '{destName}'");
                        }
                        catch (Exception ex)
                        {
                            Log($"[Split Error] Không thể tách thư mục '{parentName}\\{subName}': {ex.Message}");
                        }
                    }

                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir, false);
                            Log($"[Split] Đã xóa thư mục cha trống: '{parentName}'");
                        }
                        else
                        {
                            Log($"[Split] Thư mục cha '{parentName}' vẫn còn tệp/thư mục khác nên không xóa.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[Split Warning] Không thể xóa thư mục cha '{parentName}': {ex.Message}");
                    }
                }

                return splitCount;
            }
            finally
            {
                _folderStructureSemaphore.Release();
            }
        }

        private bool TryGetSplitSingleComicGroupSize(out int groupSize)
        {
            groupSize = 200;
            if (cmbSplitChapterGroupSize == null)
            {
                return true;
            }

            string raw = null;
            if (cmbSplitChapterGroupSize.SelectedItem is ComboBoxItem item)
            {
                raw = item.Content?.ToString();
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = "200";
            }

            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out groupSize) || groupSize <= 0)
            {
                MessageBox.Show("Group size không hợp lệ.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            return true;
        }

        private string GetSplitSingleComicRootPath()
        {
            string path = txtSplitSingleComicRoot?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            return txtDownloadPath?.Text?.Trim();
        }

        private string GetMergeSingleComicRootPath()
        {
            string path = txtMergeSingleComicRoot?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            return txtDownloadPath?.Text?.Trim();
        }

        private async Task<int> SplitSingleComicFoldersInTargetFolderAsync(string targetFolder, int groupSize, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder) || groupSize <= 0)
            {
                return 0;
            }

            await _folderStructureSemaphore.WaitAsync(token);
            try
            {
                var bookFolders = CollectSingleComicChapterFolders(targetFolder)
                    .GroupBy(item => item.BookFolderPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int splitCount = 0;

                foreach (var bookGroup in bookFolders)
                {
                    string bookFolder = bookGroup.Key;
                    var chapterFolders = bookGroup
                        .OrderBy(item => item.ChapterNumber)
                        .ThenBy(item => item.FolderName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (chapterFolders.Count < 2)
                    {
                        continue;
                    }

                    foreach (var bucketGroup in chapterFolders.GroupBy(item => (Math.Max(1, item.ChapterNumber) - 1) / groupSize))
                    {
                        List<SplitSingleComicChapterItem> bucketItems = bucketGroup
                            .OrderBy(item => item.ChapterNumber)
                            .ThenBy(item => item.FolderName, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (bucketItems.Count == 0)
                        {
                            continue;
                        }

                        int start = bucketItems.Min(item => Math.Max(1, item.ChapterNumber));
                        int end = bucketItems.Max(item => Math.Max(1, item.ChapterNumber));
                        string groupFolderName = $"chap {start:0000}-{end:0000}";
                        string groupFolderPath = Path.Combine(bookFolder, groupFolderName);

                        foreach (SplitSingleComicChapterItem chapter in bucketItems)
                        {
                            string destinationPath = Path.Combine(groupFolderPath, chapter.FolderName);

                        try
                        {
                            if (string.Equals(chapter.SourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            Directory.CreateDirectory(groupFolderPath);
                            if (!Directory.Exists(destinationPath))
                            {
                                Directory.Move(chapter.SourcePath, destinationPath);
                            }
                                else
                                {
                                    MergeDirectoryContents(chapter.SourcePath, destinationPath);
                                }

                                splitCount++;
                                Log($"[Split] Đã chia '{Path.GetFileName(bookFolder)}\\{chapter.FolderName}' -> '{groupFolderName}\\{chapter.FolderName}'");
                            }
                            catch (Exception ex)
                            {
                                Log($"[Split Error] Không thể chia '{chapter.SourcePath}': {ex.Message}");
                            }
                        }
                    }
                }

                return splitCount;
            }
            finally
            {
                _folderStructureSemaphore.Release();
            }
        }

        private async Task<int> MergeSingleComicFoldersInTargetFolderAsync(string targetFolder, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
            {
                return 0;
            }

            await _folderStructureSemaphore.WaitAsync(token);
            try
            {
                List<SplitSingleComicChapterItem> items = CollectSingleComicChapterFolders(targetFolder)
                    .Where(item => !string.Equals(item.SourcePath, targetFolder, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(item => item.Depth)
                    .ThenBy(item => item.ChapterNumber)
                    .ThenBy(item => item.SourcePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int mergedCount = 0;
                foreach (SplitSingleComicChapterItem chapter in items)
                {
                    if (string.IsNullOrWhiteSpace(chapter.SourcePath) ||
                        string.IsNullOrWhiteSpace(chapter.FolderName) ||
                        string.Equals(chapter.SourcePath, targetFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string destinationPath = Path.Combine(targetFolder, chapter.FolderName);
                    try
                    {
                        if (string.Equals(chapter.SourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!Directory.Exists(destinationPath))
                        {
                            Directory.Move(chapter.SourcePath, destinationPath);
                        }
                        else
                        {
                            MergeDirectoryContents(chapter.SourcePath, destinationPath);
                        }

                        mergedCount++;
                        Log($"[Merge] Đã gộp '{chapter.SourcePath}' -> '{destinationPath}'");
                    }
                    catch (Exception ex)
                    {
                        Log($"[Merge Error] Không thể gộp '{chapter.SourcePath}': {ex.Message}");
                    }
                }

                DeleteEmptyDirectoriesBottomUp(targetFolder);
                return mergedCount;
            }
            finally
            {
                _folderStructureSemaphore.Release();
            }
        }

        private List<SplitSingleComicChapterItem> CollectSingleComicChapterFolders(string rootFolder)
        {
            var items = new List<SplitSingleComicChapterItem>();
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
            {
                return items;
            }

            foreach (string folder in EnumerateSingleComicCandidateFolders(rootFolder))
            {
                string folderName = Path.GetFileName(folder);
                if (ShouldIgnoreFolderStructureAction(folderName))
                {
                    continue;
                }

                if (!DirectoryContainsImages(folder))
                {
                    continue;
                }

                if (!TryParseReaderChapterNumber(folderName, out double chapterNumber, out _))
                {
                    continue;
                }

                string parent = Path.GetDirectoryName(folder);
                if (string.IsNullOrWhiteSpace(parent))
                {
                    continue;
                }

                items.Add(new SplitSingleComicChapterItem
                {
                    SourcePath = folder,
                    FolderName = folderName,
                    BookFolderPath = parent,
                    ChapterNumber = Math.Max(1, (int)Math.Floor(chapterNumber)),
                    Depth = folder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).Length
                });
            }

            return items;
        }

        private IEnumerable<string> EnumerateSingleComicCandidateFolders(string rootFolder)
        {
            var stack = new Stack<string>();
            stack.Push(rootFolder);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                foreach (string child in SafeGetDirectories(current))
                {
                    string childName = Path.GetFileName(child);
                    if (ShouldIgnoreFolderStructureAction(childName))
                    {
                        continue;
                    }

                    yield return child;
                    stack.Push(child);
                }
            }
        }

        private void DeleteEmptyDirectoriesBottomUp(string rootFolder)
        {
            foreach (string directory in Directory.GetDirectories(rootFolder, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                if (string.Equals(directory, rootFolder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory, false);
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Merge Warning] Không thể xóa folder rỗng '{directory}': {ex.Message}");
                }
            }
        }

        private bool ShouldIgnoreFolderStructureAction(string folderName)
        {
            return string.IsNullOrWhiteSpace(folderName) ||
                   folderName.StartsWith(".", StringComparison.OrdinalIgnoreCase) ||
                   folderName.EndsWith("-tmp", StringComparison.OrdinalIgnoreCase);
        }

        private void MergeDirectoryContents(string source, string dest)
        {
            if (string.IsNullOrWhiteSpace(source) ||
                string.IsNullOrWhiteSpace(dest) ||
                string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
            {
                MergeFileIntoDirectory(file, Path.Combine(dest, Path.GetFileName(file)));
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                string destDir = Path.Combine(dest, Path.GetFileName(dir));
                MergeDirectoryContents(dir, destDir);
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(source).Any())
                {
                    Directory.Delete(source, false);
                }
            }
            catch (Exception ex)
            {
                Log($"[Merge Warning] Không thể xóa source sau khi gộp '{source}': {ex.Message}");
            }
        }

        private void MergeFileIntoDirectory(string sourceFile, string destFile)
        {
            if (string.IsNullOrWhiteSpace(sourceFile) ||
                string.IsNullOrWhiteSpace(destFile) ||
                !File.Exists(sourceFile))
            {
                return;
            }

            Exception lastError = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    if (File.Exists(destFile))
                    {
                        long sourceLength = SafeGetFileLength(sourceFile);
                        long destLength = SafeGetFileLength(destFile);
                        if (destLength > 0 && sourceLength == destLength)
                        {
                            TryDeleteFileQuietly(sourceFile);
                            return;
                        }
                    }
                    else
                    {
                        string destParent = Path.GetDirectoryName(destFile);
                        if (!string.IsNullOrWhiteSpace(destParent))
                        {
                            Directory.CreateDirectory(destParent);
                        }

                        File.Move(sourceFile, destFile);
                        return;
                    }
                }
                catch (IOException)
                {
                    lastError = new IOException($"Không thể gộp file '{sourceFile}' vào '{destFile}'.");
                    if (File.Exists(destFile))
                    {
                        long sourceLength = SafeGetFileLength(sourceFile);
                        long destLength = SafeGetFileLength(destFile);
                        if (destLength > 0 && sourceLength == destLength)
                        {
                            TryDeleteFileQuietly(sourceFile);
                            return;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    lastError = new UnauthorizedAccessException($"Không thể truy cập file '{destFile}'.");
                    if (File.Exists(destFile))
                    {
                        long sourceLength = SafeGetFileLength(sourceFile);
                        long destLength = SafeGetFileLength(destFile);
                        if (destLength > 0 && sourceLength == destLength)
                        {
                            TryDeleteFileQuietly(sourceFile);
                            return;
                        }
                    }
                }

                if (attempt < 3)
                {
                    System.Threading.Thread.Sleep(120);
                    continue;
                }

                if (File.Exists(sourceFile))
                {
                    throw lastError ?? new IOException($"Không thể gộp file '{sourceFile}' vào '{destFile}'.");
                }
            }
        }

        private static long SafeGetFileLength(string path)
        {
            try
            {
                return File.Exists(path) ? new FileInfo(path).Length : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static void TryDeleteFileQuietly(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        return;
                    }

                    File.Delete(path);
                    return;
                }
                catch
                {
                    if (attempt < 3)
                    {
                        System.Threading.Thread.Sleep(120);
                    }
                }
            }
        }

        private sealed class SplitSingleComicChapterItem
        {
            public string SourcePath { get; set; }

            public string FolderName { get; set; }

            public string BookFolderPath { get; set; }

            public int ChapterNumber { get; set; }

            public int Depth { get; set; }
        }

        private string GetActiveTargetFolder_LegacyDoNotUse(string downloadRoot)
        {
            // Legacy placeholder only. Explorer flow moved to MainWindow.SystemExplorer.cs.
            return downloadRoot;
        }
    }
}
