using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Windows;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private readonly ConcurrentDictionary<string, byte> _activeTempFolders = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        internal void RegisterTempFolder(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                _activeTempFolders.TryAdd(path, 0);
            }
        }

        internal void UnregisterTempFolder(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                _activeTempFolders.TryRemove(path, out _);
            }
        }

        internal void CleanupActiveTempFolders()
        {
            foreach (var path in _activeTempFolders.Keys.ToList())
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(path) && System.IO.Directory.Exists(path))
                    {
                        HandleDownloadStopOrInterruption(path);
                        Log($"[Cleanup] Đã di chuyển thư mục dở dang về .tmp: {path}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Cleanup Warning] Không thể xử lý thư mục tạm '{path}': {ex.Message}");
                }
                finally
                {
                    _activeTempFolders.TryRemove(path, out _);
                }
            }
        }
    }
}
