using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _appShutdownTimer;
        private DateTime _shutdownTargetTime;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)]
            public int type;
            [FieldOffset(8)]
            public MOUSEINPUT mi;
            [FieldOffset(8)]
            public KEYBDINPUT ki;
            [FieldOffset(8)]
            public HARDWAREINPUT hi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        private const ushort VK_LCONTROL = 0xA2;
        private const ushort VK_LSHIFT = 0xA0;
        private const ushort VK_LMENU = 0xA4; // Alt
        private const ushort VK_LWIN = 0x5B;

        private void InitializePostDownloadUI()
        {
            if (cmbPostMainKey == null) return;

            var keyItems = new List<string> { "None" };
            for (char c = 'A'; c <= 'Z'; c++) keyItems.Add(c.ToString());
            for (int i = 0; i <= 9; i++) keyItems.Add(i.ToString());
            for (int f = 1; f <= 12; f++) keyItems.Add($"F{f}");
            
            keyItems.AddRange(new[] { 
                "Comma", "Period", "Minus", "Plus", "Space", "Enter", "Tab", "Escape",
                "Home", "End", "PageUp", "PageDown", "Insert", "Delete",
                "Slash", "Backslash", "OpenBracket", "CloseBracket", "Backspace", "Backquote"
            });

            cmbPostMainKey.ItemsSource = keyItems;
            cmbPostMainKey.SelectedIndex = 0;
        }

        private void CmbPostMainKey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            string targetItem = null;

            if (key >= System.Windows.Input.Key.A && key <= System.Windows.Input.Key.Z)
            {
                targetItem = key.ToString();
            }
            else if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
            {
                targetItem = ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString();
            }
            else if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
            {
                targetItem = ((char)('0' + (key - System.Windows.Input.Key.NumPad0))).ToString();
            }
            else if (key >= System.Windows.Input.Key.F1 && key <= System.Windows.Input.Key.F12)
            {
                targetItem = key.ToString();
            }
            else
            {
                switch (key)
                {
                    case System.Windows.Input.Key.OemComma: case System.Windows.Input.Key.Separator: targetItem = "Comma"; break;
                    case System.Windows.Input.Key.OemPeriod: case System.Windows.Input.Key.Decimal: targetItem = "Period"; break;
                    case System.Windows.Input.Key.OemMinus: case System.Windows.Input.Key.Subtract: targetItem = "Minus"; break;
                    case System.Windows.Input.Key.OemPlus: case System.Windows.Input.Key.Add: targetItem = "Plus"; break;
                    case System.Windows.Input.Key.Space: targetItem = "Space"; break;
                    case System.Windows.Input.Key.Enter: targetItem = "Enter"; break;
                    case System.Windows.Input.Key.Tab: targetItem = "Tab"; break;
                    case System.Windows.Input.Key.Escape: targetItem = "Escape"; break;
                    case System.Windows.Input.Key.Home: targetItem = "Home"; break;
                    case System.Windows.Input.Key.End: targetItem = "End"; break;
                    case System.Windows.Input.Key.PageUp: targetItem = "PageUp"; break;
                    case System.Windows.Input.Key.PageDown: targetItem = "PageDown"; break;
                    case System.Windows.Input.Key.Insert: targetItem = "Insert"; break;
                    case System.Windows.Input.Key.Delete: targetItem = "Delete"; break;
                    case System.Windows.Input.Key.OemQuestion: case System.Windows.Input.Key.Divide: targetItem = "Slash"; break;
                    case System.Windows.Input.Key.Oem5: targetItem = "Backslash"; break;
                    case System.Windows.Input.Key.OemOpenBrackets: targetItem = "OpenBracket"; break;
                    case System.Windows.Input.Key.OemCloseBrackets: targetItem = "CloseBracket"; break;
                    case System.Windows.Input.Key.Back: targetItem = "Backspace"; break;
                    case System.Windows.Input.Key.OemTilde: targetItem = "Backquote"; break;
                }
            }

            if (targetItem != null && cmbPostMainKey.ItemsSource is List<string> items)
            {
                int index = items.FindIndex(x => string.Equals(x, targetItem, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    cmbPostMainKey.SelectedIndex = index;
                    e.Handled = true;
                }
            }
        }

        private void BtnApplyShortcut_Click(object sender, RoutedEventArgs e)
        {
            var parts = new List<string>();
            if (chkPostCtrl?.IsChecked == true) parts.Add("Ctrl");
            if (chkPostWin?.IsChecked == true) parts.Add("Win");
            if (chkPostAlt?.IsChecked == true) parts.Add("Alt");
            if (chkPostShift?.IsChecked == true) parts.Add("Shift");

            string mainKey = cmbPostMainKey?.SelectedItem as string;
            if (!string.IsNullOrEmpty(mainKey) && mainKey != "None")
            {
                parts.Add(mainKey);
            }

            if (txtPostDownloadShortcut != null)
            {
                txtPostDownloadShortcut.Text = string.Join("+", parts);
            }
        }

        private void BtnClearShortcut_Click(object sender, RoutedEventArgs e)
        {
            if (chkPostCtrl != null) chkPostCtrl.IsChecked = false;
            if (chkPostWin != null) chkPostWin.IsChecked = false;
            if (chkPostAlt != null) chkPostAlt.IsChecked = false;
            if (chkPostShift != null) chkPostShift.IsChecked = false;
            if (cmbPostMainKey != null) cmbPostMainKey.SelectedIndex = 0;
            if (txtPostDownloadShortcut != null) txtPostDownloadShortcut.Text = string.Empty;
        }

        private void ExecuteWinRCommand(string cmdLine)
        {
            if (string.IsNullOrWhiteSpace(cmdLine)) return;
            Log($"[PostDownload] Thực thi lệnh Win+R: {cmdLine}");
            try
            {
                string file = "";
                string args = "";
                cmdLine = cmdLine.Trim();

                if (cmdLine.StartsWith("\""))
                {
                    int nextQuote = cmdLine.IndexOf("\"", 1);
                    if (nextQuote > 0)
                    {
                        file = cmdLine.Substring(1, nextQuote - 1);
                        args = cmdLine.Substring(nextQuote + 1).Trim();
                    }
                    else
                    {
                        file = cmdLine.Replace("\"", "");
                    }
                }
                else
                {
                    string[] tokens = cmdLine.Split(' ');
                    bool found = false;
                    for (int i = tokens.Length; i > 0; i--)
                    {
                        string candidate = string.Join(" ", tokens.Take(i));
                        if (File.Exists(candidate) || Directory.Exists(candidate))
                        {
                            file = candidate;
                            args = string.Join(" ", tokens.Skip(i)).Trim();
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        int firstSpace = cmdLine.IndexOf(' ');
                        if (firstSpace > 0)
                        {
                            file = cmdLine.Substring(0, firstSpace);
                            args = cmdLine.Substring(firstSpace + 1).Trim();
                        }
                        else
                        {
                            file = cmdLine;
                        }
                    }
                }

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Log($"[PostDownload Error] Không thể chạy lệnh Win+R: {ex.Message}");
            }
        }

        private void SimulateShortcut(string shortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut)) return;
            Log($"[PostDownload] Chạy phím tắt (SendInput): {shortcut}");

            string[] parts = shortcut.Split('+');
            var keysToPress = new List<ushort>();
            ushort mainKey = 0;

            foreach (var part in parts)
            {
                string p = part.Trim().ToUpperInvariant();
                if (p == "CTRL") keysToPress.Add(VK_LCONTROL);
                else if (p == "WIN") keysToPress.Add(VK_LWIN);
                else if (p == "ALT") keysToPress.Add(VK_LMENU);
                else if (p == "SHIFT") keysToPress.Add(VK_LSHIFT);
                else
                {
                    mainKey = ParseVirtualKeyCode(p);
                }
            }

            var inputs = new List<INPUT>();

            // Press modifiers
            foreach (var vk in keysToPress)
            {
                inputs.Add(CreateKeyInput(vk, false));
            }

            // Press main key
            if (mainKey != 0)
            {
                inputs.Add(CreateKeyInput(mainKey, false));
            }

            // Release main key
            if (mainKey != 0)
            {
                inputs.Add(CreateKeyInput(mainKey, true));
            }

            // Release modifiers (reverse order)
            for (int i = keysToPress.Count - 1; i >= 0; i--)
            {
                inputs.Add(CreateKeyInput(keysToPress[i], true));
            }

            uint result = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
            if (result == 0)
            {
                Log($"[PostDownload Warning] SendInput trả về 0. Tiến hành sử dụng bộ mô phỏng dự phòng keybd_event.");
                SimulateShortcutFallback(keysToPress, mainKey);
            }
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        private INPUT CreateKeyInput(ushort vk, bool isKeyUp)
        {
            var input = new INPUT { type = INPUT_KEYBOARD };
            input.ki.wVk = vk;
            input.ki.wScan = 0;
            input.ki.dwFlags = isKeyUp ? KEYEVENTF_KEYUP : 0;

            // Arrow keys, home, end, pageup, pagedown, insert, delete, and Windows keys are extended keys in Win32
            if (vk == 0x24 || vk == 0x23 || vk == 0x21 || vk == 0x22 || vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x5C)
            {
                input.ki.dwFlags |= KEYEVENTF_EXTENDEDKEY;
            }

            input.ki.time = 0;
            input.ki.dwExtraInfo = IntPtr.Zero;
            return input;
        }

        private void SimulateShortcutFallback(List<ushort> keysToPress, ushort mainKey)
        {
            Log($"[PostDownload] Sử dụng bộ mô phỏng dự phòng keybd_event...");
            foreach (var vk in keysToPress)
            {
                keybd_event((byte)vk, 0, 0, 0);
            }

            if (mainKey != 0)
            {
                keybd_event((byte)mainKey, 0, 0, 0);
                System.Threading.Thread.Sleep(50);
                keybd_event((byte)mainKey, 0, KEYEVENTF_KEYUP, 0);
            }

            for (int i = keysToPress.Count - 1; i >= 0; i--)
            {
                keybd_event((byte)keysToPress[i], 0, KEYEVENTF_KEYUP, 0);
            }
        }

        private ushort ParseVirtualKeyCode(string keyStr)
        {
            if (string.IsNullOrEmpty(keyStr)) return 0;
            keyStr = keyStr.Trim().ToUpperInvariant();

            // Letters A-Z
            if (keyStr.Length == 1 && keyStr[0] >= 'A' && keyStr[0] <= 'Z')
            {
                return (ushort)keyStr[0];
            }

            // Numbers 0-9
            if (keyStr.Length == 1 && keyStr[0] >= '0' && keyStr[0] <= '9')
            {
                return (ushort)(0x30 + (keyStr[0] - '0'));
            }

            // Function keys F1-F12
            if (keyStr.StartsWith("F") && keyStr.Length > 1 && int.TryParse(keyStr.Substring(1), out int fNum) && fNum >= 1 && fNum <= 12)
            {
                return (ushort)(0x70 + (fNum - 1));
            }

            switch (keyStr)
            {
                case "COMMA": return 0xBC;
                case "PERIOD": return 0xBE;
                case "MINUS": return 0xBD;
                case "PLUS": return 0xBB;
                case "SLASH": return 0xBF;
                case "BACKSLASH": return 0xDC;
                case "OPENBRACKET": return 0xDB;
                case "CLOSEBRACKET": return 0xDD;
                case "BACKSPACE": return 0x08;
                case "BACKQUOTE": return 0xC0;
                case "SPACE": return 0x20;
                case "ENTER": return 0x0D;
                case "TAB": return 0x09;
                case "ESCAPE": return 0x1B;
                case "HOME": return 0x24;
                case "END": return 0x23;
                case "PAGEUP": return 0x21;
                case "PAGEDOWN": return 0x22;
                case "INSERT": return 0x2D;
                case "DELETE": return 0x2E;
                default: return 0;
            }
        }

        private void RunPostDownloadActions()
        {
            string command = null;
            string shortcut = null;

            Dispatcher.Invoke(() =>
            {
                command = txtPostDownloadCommand?.Text?.Trim();
                shortcut = txtPostDownloadShortcut?.Text?.Trim();
            });

            if (!string.IsNullOrEmpty(command))
            {
                ExecuteWinRCommand(command);
            }

            if (!string.IsNullOrEmpty(shortcut))
            {
                SimulateShortcut(shortcut);
            }
        }
    }
}
