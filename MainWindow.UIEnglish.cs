using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private static readonly Dictionary<string, string> UiTranslations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PARAMETERS CONFIG"] = "1. Chọn nguồn và dán link",
            ["📜 HISTORY"] = "📜 Lịch sử",
            ["📌 BOOKMARKS"] = "📌 Đánh dấu",
            ["🏠 HOMEPAGE"] = "🏠 Trang chủ",
            ["DOWNLOAD MULTIPLE WITH TAG OR ARTIST"] = "Dán link danh sách, tag, thể loại hoặc tác giả",
            ["DOWNLOAD MULTIPLE WITH TAG OR BOOK OR CHAPTER"] = "Dán link danh sách, tag, truyện hoặc chapter",
            ["BASE 100% = 1360x768"] = "100% = chuẩn cho 1360x768",
            ["Ready."] = "Sẵn sàng.",
            ["Watch manga"] = "Xem manga",
            ["Watch novel"] = "Xem novel",
            ["Convert"] = "Chuyển đổi",
            ["Convert Tools"] = "Công cụ Convert",
            ["XnConvert Tool"] = "Công cụ XnConvert",
            ["CONVERT XNCONVERT"] = "CONVERT BẰNG XNCONVERT",
            ["Install App:"] = "Cài đặt ứng dụng:",
            ["XnConvertPortable.exe path:"] = "Đường dẫn XnConvertPortable.exe:",
            ["Download Manga"] = "Tải Manga",
            ["Download Novel"] = "Tải Novel",
            ["download novel"] = "Tải Novel",
            ["Open picture with"] = "Mở bằng",
            ["Open viewer"] = "Mở trình xem",
            ["Missing chapters"] = "Chương thiếu",
            ["Download queue"] = "Hàng chờ tải",
            ["Watch offline"] = "Đọc offline",
            ["About"] = "Giới thiệu",
            ["Update"] = "Cập nhật",
            ["Search"] = "Tìm kiếm",
            ["Word wrap"] = "Xuống dòng",
            ["DOWNLOAD ALL"] = "TẢI TẤT CẢ",
            ["AUTO RETRY UNTIL SETTLED"] = "TỰ THỬ LẠI ĐẾN KHI XONG",
            ["ERROR LOG"] = "NHẬT KÝ LỖI",
            ["PANEL 1 - BOOK"] = "PANEL 1 - TRUYỆN",
            ["PANEL 2 - CHAPTER"] = "PANEL 2 - CHƯƠNG",
            ["PANEL 3 - PLAIN TEXT"] = "PANEL 3 - VĂN BẢN THÔ",
            ["PANEL 4 - .MD"] = "PANEL 4 - .MD",
            ["Single comic"] = "Single comic",
            ["Multi-comic"] = "Multi-comic",
            ["TARGET TAG URL"] = "Link mục tiêu",
            ["ANALYZE TARGET PAGE"] = "1) Kiểm tra link",
            ["DELETE COOKIE"] = "Xóa cookie",
            ["TOTAL PAGES"] = "Tổng số trang tìm thấy",
            ["FROM PAGE"] = "Lấy từ trang",
            ["TO PAGE"] = "Đến trang",
            ["GET LINK"] = "2) Lấy danh sách truyện",
            ["GET MORE"] = "Lấy thêm trang",
            ["PASTE DIRECT LINK"] = "Dán link truyện trực tiếp",
            ["START CRAWLING"] = "Bắt đầu cào",
            ["CRAWL MORE"] = "Cào thêm",
            ["SORT OPTION"] = "Tùy chọn sắp xếp",
            ["Recent"] = "Mới nhất",
            ["Popular Today"] = "Phổ biến hôm nay",
            ["Popular Week"] = "Phổ biến tuần này",
            ["Popular All Time"] = "Phổ biến mọi lúc",
            ["TARGET PAGE ANALYSIS"] = "Phân tích trang đích",
            ["Total Pages Found:"] = "Tổng số trang tìm thấy:",
            ["EXTRACTED GALLERY LINKS"] = "2. Danh sách chờ tải",
            ["GALLERY DETAILS (TITLE & URL)"] = "Chi tiết truyện (tên & URL)",
            ["STATUS"] = "Trạng thái",
            ["PROCESS"] = "Tiến trình",
            ["VIEW LINK"] = "Mở nhanh",
            ["COPY LINKS"] = "Sao chép link",
            ["COPY NAME+LINK"] = "Sao chép tên + link",
            ["COPY NAME + LINK"] = "Sao chép tên + link",
            ["SAVE LIST"] = "Lưu danh sách",
            ["LOAD LIST"] = "Tải danh sách",
            ["CLEAR"] = "Xóa",
            ["REFRESH STATUS"] = "LÀM MỚI TRẠNG THÁI",
            ["WORD WRAP"] = "Xuống dòng",
            ["SEARCH"] = "Tìm kiếm",
            ["FOLDER ACTIONS"] = "Công cụ thư mục",
            ["CHAPTER SELECTION"] = "Chỉ tải chapter",
            ["CONNECTION"] = "Kết nối",
            ["DOWNLOAD MULTIPLE BOOK"] = "Tải cùng lúc",
            ["SORT BY NAME"] = "Sắp xếp theo tên",
            ["RESTORE ORDER"] = "Trả về thứ tự cũ",
            ["DUPLICATE NAME"] = "Tên trùng",
            ["NO LINK (VI-HENTAI)"] = "Không có chapter",
            ["REVERSE ORDER"] = "Đảo thứ tự",
            ["CLEAR COMPLETE BOOKS"] = "Ẩn truyện đã xong",
            ["BROWSE"] = "Duyệt",
            ["OPEN FOLDER"] = "Mở thư mục",
            ["MERGE"] = "Gộp",
            ["SPLIT"] = "Tách",
            ["COMPRESS"] = "Nén",
            ["EXTRACT"] = "Giải nén",
            ["START"] = "Tải",
            ["STOP"] = "Dừng",
            ["RETRY"] = "Thử lại",
            ["LOG"] = "Nhật ký",
            ["DOWNLOAD SETTINGS"] = "3. Thiết lập tải",
            ["QUICK ACTIONS FOR CURRENT LIST"] = "Thao tác nhanh cho danh sách hiện tại",
            ["Simple flow: choose website, paste link, review list, then download."] = "Cách dùng nhanh: chọn web, dán link, xem danh sách rồi tải.",
            ["CHOOSE SOURCE"] = "CHỌN NGUỒN",
            ["PASTE LINK"] = "DÁN LINK",
            ["DOWNLOAD"] = "TẢI VỀ",
            ["STEP 1"] = "BƯỚC 1",
            ["STEP 2"] = "BƯỚC 2",
            ["STEP 3"] = "BƯỚC 3",
            ["INTERFACE LANGUAGE"] = "NGÔN NGỮ GIAO DIỆN",
            ["DISPLAY SCALE"] = "TỶ LỆ HIỂN THỊ",
            ["100% = baseline cho 1360x768"] = "100% = chuẩn cho 1360x768",
            ["Review queue, tick what you want, then start download."] = "Kiểm tra danh sách, tích mục muốn tải, rồi bấm tải.",
            ["Check selected rows"] = "Tích chọn dòng đang bôi đen",
            ["Uncheck selected rows"] = "Bỏ tích dòng đang bôi đen",
            ["Invert checked state"] = "Đảo ngược trạng thái tích",
            ["📌 Bookmark selected row"] = "📌 Đánh dấu dòng đang chọn",
            ["🌐 Open link in browser"] = "🌐 Mở link trong trình duyệt",
            ["Copy selected links"] = "Copy link các dòng đang chọn",
            ["Delete selected rows"] = "Xóa các dòng đang bôi đen",
            ["Delete checked rows"] = "Xóa các dòng đã tích",
            ["Download selected rows"] = "Tải các dòng đang bôi đen",
            ["Download checked rows"] = "Tải các dòng đã tích",
            ["Download novel"] = "Tải novel",
            ["PANEL 1 - BOOK"] = "PANEL 1 - TÊN TRUYỆN",
            ["PANEL 2 - CHAPTER"] = "PANEL 2 - CHAPTER",
            ["PANEL 3 - PLAIN TEXT"] = "PANEL 3 - VĂN BẢN THÔ",
            ["PANEL 4 - .MD"] = "PANEL 4 - .MD",
            ["SHUTDOWN AFTER DONE"] = "TẮT MÁY SAU KHI XONG",
            ["POWER"] = "TẮT MÁY"
        };

        private static readonly Dictionary<string, string> LegacyVietnameseCleanup = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Tích chọn dòng đang bôi đen (Check Selected)"] = "Tích chọn dòng đang bôi đen",
            ["Bỏ tích dòng đang bôi đen (Uncheck Selected)"] = "Bỏ tích dòng đang bôi đen",
            ["Đảo ngược trạng thái tích (Invert Checked)"] = "Đảo ngược trạng thái tích",
            ["📌 Bookmark dòng đang chọn"] = "📌 Đánh dấu dòng đang chọn",
            ["Copy link các dòng đang chọn (Copy Selected Links)"] = "Copy link các dòng đang chọn",
            ["Xóa các dòng đang bôi đen (Delete Selected)"] = "Xóa các dòng đang bôi đen",
            ["Xóa các dòng đã tích (Delete Checked)"] = "Xóa các dòng đã tích",
            ["Tải các dòng đang bôi đen (Download highlighted lines)"] = "Tải các dòng đang bôi đen",
            ["Tải các dòng đã tích (Download selected lines)"] = "Tải các dòng đã tích"
        };

        internal bool _isVietnameseUi;

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            CheckFirstTimeRunMigration();
            _ = HandlePendingFirstRunCleanupAsync();
            ApplyCurrentUiLanguage();
            UpdateTruyenqqSpecificActions();
            PromptExtractPortableArchiveOnStartup();
        }

        private async System.Threading.Tasks.Task HandlePendingFirstRunCleanupAsync()
        {
            string[] args = Environment.GetCommandLineArgs();
            string cleanupSourceDir = null;
            string preserveDir = null;
            string cleanupExePath = null;
            string migrateNewExePath = null;
            string migrateFinalExePath = null;
            string migrateNewExeName = null;
            string migrateFinalExeName = null;
            bool migrateFinish = false;
            int killPid = -1;

            foreach (string arg in args.Skip(1))
            {
                if (arg.StartsWith("--cleanup-source=", StringComparison.OrdinalIgnoreCase))
                {
                    cleanupSourceDir = UnquoteCliValue(arg.Substring("--cleanup-source=".Length));
                }
                else if (arg.StartsWith("--preserve-dir=", StringComparison.OrdinalIgnoreCase))
                {
                    preserveDir = UnquoteCliValue(arg.Substring("--preserve-dir=".Length));
                }
                else if (arg.StartsWith("--kill-pid=", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(arg.Substring("--kill-pid=".Length), out killPid);
                }
                else if (arg.StartsWith("--cleanup-exe=", StringComparison.OrdinalIgnoreCase))
                {
                    cleanupExePath = UnquoteCliValue(arg.Substring("--cleanup-exe=".Length));
                }
                else if (string.Equals(arg, "--migrate-finish", StringComparison.OrdinalIgnoreCase))
                {
                    migrateFinish = true;
                }
                else if (arg.StartsWith("--migrate-new-exe=", StringComparison.OrdinalIgnoreCase))
                {
                    migrateNewExePath = UnquoteCliValue(arg.Substring("--migrate-new-exe=".Length));
                }
                else if (arg.StartsWith("--migrate-final-exe=", StringComparison.OrdinalIgnoreCase))
                {
                    migrateFinalExePath = UnquoteCliValue(arg.Substring("--migrate-final-exe=".Length));
                }
                else if (arg.StartsWith("--migrate-new-name=", StringComparison.OrdinalIgnoreCase))
                {
                    migrateNewExeName = UnquoteCliValue(arg.Substring("--migrate-new-name=".Length));
                }
                else if (arg.StartsWith("--migrate-final-name=", StringComparison.OrdinalIgnoreCase))
                {
                    migrateFinalExeName = UnquoteCliValue(arg.Substring("--migrate-final-name=".Length));
                }
            }

            if (migrateFinish)
            {
                await System.Threading.Tasks.Task.Delay(1200);

                if (killPid > 0)
                {
                    TryKillProcessById(killPid);
                }

                TryDeleteFileWithRetry(cleanupExePath, 8, 250);
                DeleteDirectoryEntriesExcept(cleanupSourceDir, preserveDir);
                ScheduleRenameAndRelaunch(migrateNewExePath, migrateFinalExePath, migrateNewExeName, migrateFinalExeName);
                System.Windows.Application.Current.Shutdown();
                System.Environment.Exit(0);
                return;
            }

            if (string.IsNullOrWhiteSpace(cleanupSourceDir) || string.IsNullOrWhiteSpace(preserveDir))
            {
                return;
            }

            await System.Threading.Tasks.Task.Delay(1200);

            if (killPid > 0)
            {
                TryKillProcessById(killPid);
            }

            TryDeleteFileWithRetry(cleanupExePath, 8, 250);
            if (!string.IsNullOrWhiteSpace(cleanupExePath) && File.Exists(cleanupExePath))
            {
                ScheduleDeleteFile(cleanupExePath, 4);
            }
            DeleteDirectoryEntriesExcept(cleanupSourceDir, preserveDir);
        }

        private static string UnquoteCliValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Trim('"');
        }

        private static void TryKillProcessById(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch
            {
            }
        }

        private static void DeleteDirectoryEntriesExcept(string sourceDir, string preserveDir)
        {
            try
            {
                string sourceFull = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string preserveFull = Path.GetFullPath(preserveDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (!Directory.Exists(sourceFull) || !Directory.Exists(preserveFull))
                {
                    return;
                }

                foreach (string entry in Directory.EnumerateFileSystemEntries(sourceFull))
                {
                    string entryFull = Path.GetFullPath(entry).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(entryFull, preserveFull, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    DeletePathRecursively(entryFull);
                }
            }
            catch
            {
            }
        }

        private static void DeletePathRecursively(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    return;
                }

                if (Directory.Exists(path))
                {
                    foreach (string child in Directory.GetFileSystemEntries(path))
                    {
                        DeletePathRecursively(child);
                    }

                    Directory.Delete(path, false);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteFileWithRetry(string path, int attempts, int delayMs)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            for (int i = 0; i < Math.Max(1, attempts); i++)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        return;
                    }

                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    return;
                }
                catch
                {
                    if (i + 1 >= attempts)
                    {
                        return;
                    }

                    System.Threading.Thread.Sleep(Math.Max(1, delayMs));
                }
            }
        }

        private static void ScheduleDeleteFile(string path, int delaySeconds)
        {
            try
            {
                string safePath = path.Replace("\"", "\\\"");
                string args = $"/c timeout /t {Math.Max(1, delaySeconds)} /nobreak >nul & del /f /q \"{safePath}\"";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = args,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch
            {
            }
        }

        private static void ScheduleRenameAndRelaunch(string newExePath, string finalExePath, string newExeName, string finalExeName)
        {
            if (string.IsNullOrWhiteSpace(newExePath) || string.IsNullOrWhiteSpace(finalExePath) ||
                string.IsNullOrWhiteSpace(newExeName) || string.IsNullOrWhiteSpace(finalExeName))
            {
                return;
            }

            try
            {
                string newDir = Path.GetDirectoryName(newExePath) ?? string.Empty;
                string args =
                    $"/c timeout /t 1 /nobreak >nul & " +
                    $"pushd \"{newDir}\" & " +
                    $"ren \"{newExeName}\" \"{finalExeName}\" & " +
                    $"start \"\" \"{finalExePath}\"";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = args,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch
            {
            }
        }

        private void CheckFirstTimeRunMigration()
        {
            string currentExePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            string currentExeDir = System.IO.Path.GetDirectoryName(currentExePath) ?? string.Empty;

            // Avoid prompting if the current directory is already named "Comic-GMTPC"
            if (currentExeDir.TrimEnd('\\', '/').EndsWith("Comic-GMTPC", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string targetDir = System.IO.Path.Combine(currentExeDir, "Comic-GMTPC");

            string pathRuntimes = System.IO.Path.Combine(currentExeDir, "runtimes");
            string pathPortable = System.IO.Path.Combine(currentExeDir, ".portable");
            string pathRoot = System.IO.Path.Combine(currentExeDir, "root");
            string pathAutosave = System.IO.Path.Combine(currentExeDir, "gallery-list-autosave.md");
            string pathAutosaveTypo = System.IO.Path.Combine(currentExeDir, "gallery-list-aut1osave.md");

            bool hasRuntimes = System.IO.Directory.Exists(pathRuntimes);
            bool hasPortable = System.IO.Directory.Exists(pathPortable);
            bool hasRoot = System.IO.Directory.Exists(pathRoot);
            bool hasAutosave = System.IO.File.Exists(pathAutosave) || System.IO.File.Exists(pathAutosaveTypo);

            if (!hasRuntimes || !hasPortable || !hasRoot || !hasAutosave)
            {
                string message = 
                    "Is this your first time running the tool?\n" +
                    "Đây có phải lần đầu bạn chạy tool?\n\n" +
                    "If Yes, the tool will create the target folder, copy the project, and relaunch from there.\n" +
                    "Nếu Yes, tool sẽ tự tạo thư mục, copy project và khởi động lại từ đó.";
                string title = "First Run / Chạy lần đầu";

                var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(targetDir);
                        
                        string sourceDir = AppDomain.CurrentDomain.BaseDirectory;
                        MoveOrCopyDirectoryContents(sourceDir, targetDir, currentExePath);

                        string exeName = System.IO.Path.GetFileName(currentExePath);
                        string exeBaseName = System.IO.Path.GetFileNameWithoutExtension(exeName);
                        string exeExt = System.IO.Path.GetExtension(exeName);
                        string newExeName = $"{exeBaseName}-new{exeExt}";
                        string newExePath = System.IO.Path.Combine(targetDir, newExeName);
                        string finalExePath = System.IO.Path.Combine(targetDir, exeName);
                        string copiedExePath = System.IO.Path.Combine(targetDir, exeName);

                        if (System.IO.File.Exists(newExePath))
                        {
                            try { System.IO.File.Delete(newExePath); } catch { }
                        }

                        if (System.IO.File.Exists(copiedExePath))
                        {
                            try { System.IO.File.Move(copiedExePath, newExePath); } catch { }
                        }

                        if (System.IO.File.Exists(newExePath))
                        {
                            // Create shortcut on the Desktop pointing to newExePath
                            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                            string shortcutPath = System.IO.Path.Combine(desktopPath, "Comic-GMTPC.lnk");
                            CreateShortcut(shortcutPath, newExePath);

                            // Open the new directory in Windows Explorer
                            System.Diagnostics.Process.Start("explorer.exe", $"\"{targetDir}\"");

                            // Relaunch the new exe
                            string launchArgs = $"--migrate-finish --cleanup-source=\"{sourceDir}\" --preserve-dir=\"{targetDir}\" --cleanup-exe=\"{currentExePath}\" --migrate-new-exe=\"{newExePath}\" --migrate-final-exe=\"{finalExePath}\" --migrate-new-name=\"{newExeName}\" --migrate-final-name=\"{exeName}\" --kill-pid={Process.GetCurrentProcess().Id}";
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = newExePath,
                                Arguments = launchArgs,
                                UseShellExecute = true
                            });

                            System.Windows.Application.Current.Shutdown();
                            System.Environment.Exit(0);
                        }
                        else
                        {
                            MessageBox.Show(
                                "Could not find relaunch executable.\nKhông tìm thấy file khởi chạy mới.",
                                "Error / Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Migration failed / Lỗi di chuyển: {ex.Message}",
                            "Error / Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private static void MoveOrCopyDirectoryContents(string sourceDir, string destinationDir, string currentExePath)
        {
            System.IO.Directory.CreateDirectory(destinationDir);

            foreach (string file in System.IO.Directory.GetFiles(sourceDir))
            {
                string destFile = System.IO.Path.Combine(destinationDir, System.IO.Path.GetFileName(file));
                if (string.Equals(file, currentExePath, StringComparison.OrdinalIgnoreCase))
                {
                    System.IO.File.Copy(file, destFile, true);
                }
                else
                {
                    try
                    {
                        if (System.IO.File.Exists(destFile))
                        {
                            System.IO.File.Delete(destFile);
                        }
                        System.IO.File.Move(file, destFile);
                    }
                    catch
                    {
                        try { System.IO.File.Copy(file, destFile, true); System.IO.File.Delete(file); } catch {}
                    }
                }
            }

            foreach (string subDir in System.IO.Directory.GetDirectories(sourceDir))
            {
                if (string.Equals(subDir.TrimEnd('\\', '/'), destinationDir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string destSubDir = System.IO.Path.Combine(destinationDir, System.IO.Path.GetFileName(subDir));
                try
                {
                    if (System.IO.Directory.Exists(destSubDir))
                    {
                        System.IO.Directory.Delete(destSubDir, true);
                    }
                    System.IO.Directory.Move(subDir, destSubDir);
                }
                catch
                {
                    CopyDirectoryRecursively(subDir, destSubDir);
                    try { System.IO.Directory.Delete(subDir, true); } catch {}
                }
            }
        }

        private static void CopyDirectoryRecursively(string sourceDir, string destinationDir)
        {
            System.IO.Directory.CreateDirectory(destinationDir);

            foreach (string file in System.IO.Directory.GetFiles(sourceDir))
            {
                string destFile = System.IO.Path.Combine(destinationDir, System.IO.Path.GetFileName(file));
                System.IO.File.Copy(file, destFile, true);
            }

            foreach (string subDir in System.IO.Directory.GetDirectories(sourceDir))
            {
                if (string.Equals(subDir.TrimEnd('\\'), destinationDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string destSubDir = System.IO.Path.Combine(destinationDir, System.IO.Path.GetFileName(subDir));
                CopyDirectoryRecursively(subDir, destSubDir);
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetExePath)
        {
            try
            {
                string powershellCmd = $"-NoProfile -Command \"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{shortcutPath}'); $s.TargetPath = '{targetExePath}'; $s.Save()\"";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = powershellCmd,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                })?.WaitForExit();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Failed to create shortcut: " + ex.Message);
            }
        }

        private void PromptExtractPortableArchiveOnStartup()
        {
            if (_startupArchivePromptShown)
            {
                return;
            }

            _startupArchivePromptShown = true;

            string archivePath = GetAvailablePortableArchivePath();
            if (!System.IO.File.Exists(archivePath))
            {
                return;
            }

            string fileName = System.IO.Path.GetFileName(archivePath);
            string message =
                $"Found archive file {fileName}.\n" +
                $"Tìm thấy file nén {fileName}.\n\n" +
                "Do you want to extract it now?\n" +
                "Bạn có muốn giải nén ngay bây giờ không?";

            string title = "Extract archive / Giải nén file nén";

            if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                BtnExtractBooks_Click(this, new RoutedEventArgs());
            }
        }

        private void TglLanguage_Checked(object sender, RoutedEventArgs e)
        {
            _isVietnameseUi = true;
            ApplyCurrentUiLanguage();
        }

        private void TglLanguage_Unchecked(object sender, RoutedEventArgs e)
        {
            _isVietnameseUi = false;
            ApplyCurrentUiLanguage();
        }

        private void TabManga_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != sender) return;
            UpdateTruyenqqSpecificActions();
        }

        private void ApplyCurrentUiLanguage()
        {
            Application.Current.Properties["IsVietnameseUi"] = _isVietnameseUi;

            if (_isVietnameseUi)
            {
                ApplyVietnameseUiText();
            }
            else
            {
                ApplyEnglishUiText();
            }

            UpdateTruyenqqSpecificActions();
            _bookmarkHistoryWindowInstance?.ApplyLanguage(_isVietnameseUi);
            RefreshVisibleGalleryLanguage();
            UpdateFoldButtonUi();
            UpdateWorkspaceShellLanguage();
            UpdateThemeText();
        }

        private void RefreshVisibleGalleryLanguage()
        {
            foreach (GalleryItem item in _scrapedItems)
            {
                item?.RefreshDisplayText();
            }
        }

        private void ApplyResultsGridHeaderLanguage(
            string galleryDetailsText,
            string statusText,
            string processText,
            string viewText)
        {
            SetResultsColumnHeaderText(colGalleryDetails, galleryDetailsText);
            SetResultsColumnHeaderText(colStatus, statusText);
            SetResultsColumnHeaderText(colProcess, processText);

            if (colViewLink != null)
            {
                colViewLink.Header = viewText;
            }
        }

        private void SetResultsColumnHeaderText(System.Windows.Controls.DataGridColumn column, string text)
        {
            if (column == null)
            {
                return;
            }

            if (column.Header is TextBlock textBlock)
            {
                textBlock.Text = text;
                return;
            }

            if (column.Header is DependencyObject dependencyObject)
            {
                TextBlock nestedTextBlock = FindHeaderTextBlock(dependencyObject);
                if (nestedTextBlock != null)
                {
                    nestedTextBlock.Text = text;
                    return;
                }
            }

            column.Header = text;
        }

        private TextBlock FindHeaderTextBlock(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is TextBlock textBlock)
            {
                return textBlock;
            }

            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is DependencyObject dependencyChild)
                {
                    TextBlock nested = FindHeaderTextBlock(dependencyChild);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private void UpdateTruyenqqSpecificActions()
        {
            if (btnSortByLatestChapter == null)
            {
                return;
            }

            bool isVisible =
                tabMangaRootItem != null && tabMangaRootItem.IsSelected &&
                tabTruyenqqItem != null && tabTruyenqqItem.IsSelected;

            btnSortByLatestChapter.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateLatestChapterButtonLabel()
        {
            if (btnSortByLatestChapter == null)
            {
                return;
            }

            string suffix = _lastLatestChapterSortDescending == true
                ? " ↓"
                : _lastLatestChapterSortDescending == false
                    ? " ↑"
                    : " ⇅";

            btnSortByLatestChapter.Content = _isVietnameseUi
                ? $"CHƯƠNG MỚI NHẤT{suffix}"
                : $"LATEST CHAPTER{suffix}";
        }

        private void ApplyEnglishUiText()
        {
            Title = "Comic-GMTPC v1.0 - English";

            if (txtHeaderTitle != null) txtHeaderTitle.Text = string.Empty;
            if (txtHeaderSubtitle != null) txtHeaderSubtitle.Text = string.Empty;
            if (txtHeaderStepPrimary != null) txtHeaderStepPrimary.Text = "STEP 1";
            if (txtHeaderStepPrimaryTitle != null) txtHeaderStepPrimaryTitle.Text = "Source";
            if (txtHeaderStepSecondary != null) txtHeaderStepSecondary.Text = "STEP 2";
            if (txtHeaderStepSecondaryTitle != null) txtHeaderStepSecondaryTitle.Text = "DOWNLOAD";
            if (txtLanguageLabel != null) txtLanguageLabel.Text = "ENG";
            if (txtLanguageTarget != null) txtLanguageTarget.Text = "VI";
            if (txtTotalBooksLabel != null) txtTotalBooksLabel.Text = "Total books: ";
            if (txtBooksCompleteLabel != null) txtBooksCompleteLabel.Text = "Books complete: ";
            if (txtErrorBooksLabel != null) txtErrorBooksLabel.Text = "Error books: ";
            if (txtResultsHeader != null) txtResultsHeader.Text = "EXTRACTED GALLERY LINKS";
            if (btnShutdownMenu != null) btnShutdownMenu.Content = "⏰";
            if (btnShutdownMenu != null) btnShutdownMenu.ToolTip = "Shutdown options";
            if (txtShutdownPopupHeader != null) txtShutdownPopupHeader.Text = "SHUTDOWN OPTIONS";
            if (chkShutdownAfterCompleted != null) chkShutdownAfterCompleted.Content = "shutdown after completed";
            if (txtShutdownCountdownLabel != null) txtShutdownCountdownLabel.Text = "shutdown in day:hour:minute:second";
            if (txtShutdownDaysLabel != null) txtShutdownDaysLabel.Text = "DAY";
            if (txtShutdownHoursLabel != null) txtShutdownHoursLabel.Text = "HOUR";
            if (txtShutdownMinutesLabel != null) txtShutdownMinutesLabel.Text = "MIN";
            if (txtShutdownSecondsLabel != null) txtShutdownSecondsLabel.Text = "SEC";
            if (txtShutdownPopupHint != null) txtShutdownPopupHint.Text = "0:1:30:0 = 1 hour 30 minutes. Schedule uses Windows shutdown command.";
            if (btnScheduleShutdownTimer != null) btnScheduleShutdownTimer.Content = "SCHEDULE";
            if (btnCancelShutdownTimer != null) btnCancelShutdownTimer.Content = "CANCEL";
            if (btnCloseShutdownPopup != null) btnCloseShutdownPopup.Content = "CLOSE";

            ApplyUiTextMappings(false);
            ApplyResultsGridHeaderLanguage("GALLERY DETAILS", "STATUS", "PROCESS", "VIEW");

            if (btnSaveList != null) btnSaveList.Content = "Save default";
            if (btnLoadList != null) btnLoadList.Content = "Open default";
            if (btnNewList != null) btnNewList.Content = "New list";
            if (btnSaveCustomList != null) btnSaveCustomList.Content = "Save as";
            if (btnLoadCustomList != null) btnLoadCustomList.Content = "Open file";
            if (btnRestoreOrder != null) btnRestoreOrder.Content = "Original order";
            if (btnDuplicateName != null) btnDuplicateName.Content = "Duplicate names";
            if (btnNoLinkViHentai != null) btnNoLinkViHentai.Content = "No chapters";
            if (btnReverseOrder != null) btnReverseOrder.Content = "Reverse order";
            if (btnClearComplete != null) btnClearComplete.Content = "Remove completed";

            UpdateLatestChapterButtonLabel();
        }

        internal MessageBoxResult ShowLocalizedMessageBox(
            string englishMessage,
            string vietnameseMessage,
            string englishTitle,
            string vietnameseTitle,
            MessageBoxButton buttons,
            MessageBoxImage icon)
        {
            return MessageBox.Show(
                _isVietnameseUi ? vietnameseMessage : englishMessage,
                _isVietnameseUi ? vietnameseTitle : englishTitle,
                buttons,
                icon);
        }

        private void ApplyUiTextMappings(bool vietnamese)
        {
            ApplyUiTextMappingsRecursive(this, vietnamese);

            if (dgResults?.ContextMenu != null)
            {
                ApplyUiTextMappingsRecursive(dgResults.ContextMenu, vietnamese);
            }

            if (_watchSection != null)
            {
                ApplyUiTextMappingsRecursive(_watchSection, vietnamese);
            }
        }

        private void ApplyUiTextMappingsRecursive(object node, bool vietnamese)
        {
            if (node == null)
            {
                return;
            }

            if (node is TextBlock textBlock && !string.IsNullOrWhiteSpace(textBlock.Text))
            {
                textBlock.Text = TranslateUiText(textBlock.Text, vietnamese);
            }
            else if (node is Button button && button.Content is string buttonText)
            {
                if (button.ToolTip is string stopTooltip && string.Equals(stopTooltip, "Stop download", StringComparison.Ordinal))
                {
                    button.Content = vietnamese ? "DỪNG" : "STOP";
                }
                else
                {
                    button.Content = TranslateUiText(buttonText, vietnamese);
                }

                if (button.ToolTip is string buttonToolTip)
                {
                    button.ToolTip = TranslateUiText(buttonToolTip, vietnamese);
                }
            }
            else if (node is MenuItem menuItem && menuItem.Header is string headerText)
            {
                menuItem.Header = TranslateUiText(headerText, vietnamese);
            }
            else if (node is TabItem tabItem && tabItem.Header is string tabHeader)
            {
                tabItem.Header = TranslateUiText(tabHeader, vietnamese);
            }
            else if (node is ComboBoxItem comboBoxItem && comboBoxItem.Content is string comboBoxText)
            {
                comboBoxItem.Content = TranslateUiText(comboBoxText, vietnamese);
            }

            if (node is ItemsControl itemsControl)
            {
                foreach (object item in itemsControl.Items)
                {
                    ApplyUiTextMappingsRecursive(item, vietnamese);
                }
            }

            if (node is DependencyObject dependencyObject)
            {
                foreach (object child in LogicalTreeHelper.GetChildren(dependencyObject))
                {
                    ApplyUiTextMappingsRecursive(child, vietnamese);
                }
            }
        }

        private string TranslateUiText(string currentText, bool vietnamese)
        {
            string normalizedText = NormalizeMojibakeText(currentText)?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return currentText;
            }

            if (vietnamese)
            {
                if (LegacyVietnameseCleanup.TryGetValue(normalizedText, out string cleanedVietnamese))
                {
                    return cleanedVietnamese;
                }

                if (UiTranslations.TryGetValue(normalizedText, out string translatedVietnamese))
                {
                    return translatedVietnamese;
                }

                foreach (KeyValuePair<string, string> pair in UiTranslations)
                {
                    if (string.Equals(normalizedText, pair.Value, StringComparison.Ordinal))
                    {
                        return pair.Value;
                    }
                }

                return normalizedText;
            }

            foreach (KeyValuePair<string, string> pair in UiTranslations)
            {
                if (string.Equals(normalizedText, pair.Key, StringComparison.Ordinal))
                {
                    return pair.Key;
                }

                if (string.Equals(normalizedText, pair.Value, StringComparison.Ordinal))
                {
                    return pair.Key;
                }
            }

            foreach (KeyValuePair<string, string> pair in LegacyVietnameseCleanup)
            {
                if (string.Equals(normalizedText, pair.Key, StringComparison.Ordinal) ||
                    string.Equals(normalizedText, pair.Value, StringComparison.Ordinal))
                {
                    return pair.Value;
                }
            }

            return normalizedText;
        }

        private static string NormalizeMojibakeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !LooksLikeMojibake(value))
            {
                return value;
            }

            string current = value;
            for (int i = 0; i < 2; i++)
            {
                try
                {
                    string decoded = System.Text.Encoding.UTF8.GetString(System.Text.Encoding.GetEncoding(1252).GetBytes(current));
                    if (CountMojibakeMarkers(decoded) >= CountMojibakeMarkers(current))
                    {
                        break;
                    }

                    current = decoded;
                }
                catch
                {
                    break;
                }
            }

            return current;
        }

        private static bool LooksLikeMojibake(string value)
        {
            return value.IndexOf("Ã", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("Â", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("â", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("ð", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("ï¿½", StringComparison.Ordinal) >= 0 ||
                   value.IndexOf("�", StringComparison.Ordinal) >= 0;
        }

        private static int CountMojibakeMarkers(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            return CountMarker(value, "Ã") +
                   CountMarker(value, "Â") +
                   CountMarker(value, "â") +
                   CountMarker(value, "ð") +
                   CountMarker(value, "ï¿½") +
                   CountMarker(value, "�");
        }

        private static int CountMarker(string value, string marker)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += marker.Length;
            }

            return count;
        }
    }
}
