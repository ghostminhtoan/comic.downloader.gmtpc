using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }
        private static readonly RoutedUICommand StartLightNovelAutoCopyCommand =
            new RoutedUICommand("Start light novel auto copy", "StartLightNovelAutoCopy", typeof(MainWindow));
        private static readonly RoutedUICommand StopLightNovelAutoCopyCommand =
            new RoutedUICommand("Stop light novel auto copy", "StopLightNovelAutoCopy", typeof(MainWindow));
        private static CookieContainer _cookieContainer;
        private static readonly ConcurrentDictionary<string, CookieContainer> _cookieContainersByHost = new ConcurrentDictionary<string, CookieContainer>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _userAgentsByHost = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, HttpClientHandler> _scopedHandlers = new ConcurrentDictionary<string, HttpClientHandler>(StringComparer.OrdinalIgnoreCase);
        private static HttpClientHandler _httpHandler;
        private static HttpClient _httpClient;
        private static readonly string _defaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private static readonly SemaphoreSlim _captchaSemaphore = new SemaphoreSlim(1, 1);
        private static volatile bool _isCaptchaWindowActive = false;
        private static readonly ConcurrentDictionary<string, DateTime> _captchaSolvedAtUtc = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private const int CaptchaCooldownSeconds = 120;
        private bool _hakoCaptchaSessionReady;
        private bool _displaySettingsHooked;
        internal string _truyenqqPreferredBaseUrl;
        private CancellationTokenSource _cts;
        private DispatcherTimer _globalAutoPasteTimer;
        private bool _globalAutoPasteEnabled;
        private bool _globalAutoPasteBusy;
        private string _globalAutoPasteLastClipboardText;
        private int _detectedMaxPage = 1;
        private bool _usePagePathSegment;
        internal ThrottledObservableCollection<GalleryItem> _scrapedItems = new ThrottledObservableCollection<GalleryItem>();
        internal ObservableCollection<GalleryItem> _lightNovelItems = new ObservableCollection<GalleryItem>();
        internal DuplicateWindow _duplicateWindowInstance;
        internal BookmarkHistoryManager _bookmarkManager = new BookmarkHistoryManager();
        private BookmarkHistoryWindow _bookmarkHistoryWindowInstance;
        private readonly System.Windows.Controls.ProgressBar progressBar = new System.Windows.Controls.ProgressBar();
        private bool _startupArchivePromptShown;
        private int _nettruyenTechRedirectProbeStarted;
        private int _damconuongRedirectProbeStarted;
        private volatile int _currentMaxParallelBooks = 2;
        private DynamicSemaphore _activeBookSemaphore;
        private int _shutdownPersistenceStarted;
        private DataGrid dgLightNovelBooks => lightNovelPreviewPanel?.LightNovelBooksGrid;
        private ListBox lbLightNovelChapters => lightNovelPreviewPanel?.LightNovelChaptersList;
        private TextBox txtLightNovelSelectedChapter => lightNovelPreviewPanel?.LightNovelSelectedChapterTextBox;
        private TextBox txtLightNovelPlainText => lightNovelPreviewPanel?.LightNovelPlainTextTextBox;
        private TextBox txtLightNovelMarkdown => lightNovelPreviewPanel?.LightNovelMarkdownTextBox;
        private ToggleButton btnStartCopyText => lightNovelPreviewPanel?.StartCopyTextToggleButton;
        private TextBox txtSplitSingleComicRoot => singleComicFolderToolsView?.SplitSingleComicRootTextBox;
        private ComboBox cmbSplitChapterGroupSize => singleComicFolderToolsView?.SplitChapterGroupSizeComboBox;
        private ComboBox cmbSplitSingleComicFolderType => singleComicFolderToolsView?.SplitSingleComicFolderTypeComboBox;
        private TextBox txtMergeSingleComicRoot => singleComicFolderToolsView?.MergeSingleComicRootTextBox;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int HOTKEY_ID = 9000;
        private const int HOTKEY_GLOBAL_SHOW_ID = 9001;
        private const int HOTKEY_GLOBAL_PIN_ID = 9002;
        private const int HOTKEY_GLOBAL_AUTOPASTE_ID = 9003;
        private const int HOTKEY_GLOBAL_FOCUS_ID = 9004;
        private const int HOTKEY_GLOBAL_DOWNLOAD_ID = 9005;
        private const int HOTKEY_GLOBAL_RETRY_ID = 9006;
        private const int HOTKEY_GLOBAL_COPY_ID = 9007;
        private const int HOTKEY_GLOBAL_OPENFOLDER_ID = 9008;
        private const int HOTKEY_GLOBAL_SHUTDOWN_ID = 9009;
        private const int HOTKEY_GLOBAL_DELETECOOKIE_ID = 9010;
        private const int HOTKEY_GLOBAL_CLEANTEMP_ID = 9011;
        private const int HOTKEY_GLOBAL_TWEAK_ID = 9012;
        private const int HOTKEY_PROJECT_OPEN_FOLDER_ID = 9013;
        private const int HOTKEY_PROJECT_NEW_LIST_ID = 9014;
        private const int HOTKEY_PROJECT_SAVE_LIST_ID = 9015;
        private const int HOTKEY_PROJECT_OPEN_LIST_ID = 9016;
        private const int HOTKEY_PROJECT_START_COPY_ID = 9017;
        private const int HOTKEY_PROJECT_STOP_COPY_ID = 9018;
        private const int HOTKEY_PROJECT_CHOOSE_SOURCE_ID = 9019;
        private const int HOTKEY_PROJECT_DOWNLOAD_SECTION_ID = 9020;
        private const int HOTKEY_PROJECT_WATCH_SECTION_ID = 9021;
        private const int HOTKEY_PROJECT_ABOUT_SECTION_ID = 9022;
        private const int HOTKEY_PROJECT_TRACELOG_SECTION_ID = 9026;
        private const int HOTKEY_PROJECT_UPDATE_SECTION_ID = 9023;
        private const int HOTKEY_PROJECT_DOWNLOAD_TOGGLE_ID = 9024;
        private const int HOTKEY_PROJECT_RETRY_ID = 9025;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_F = 0x46;
        private const uint VK_A = 0x41;
        private const uint VK_C = 0x43;
        private const uint VK_D = 0x44;
        private const uint VK_N = 0x4E;
        private const uint VK_L = 0x4C;
        private const uint VK_M = 0x4D;
        private const uint VK_O = 0x4F;
        private const uint VK_P = 0x50;
        private const uint VK_R = 0x52;
        private const uint VK_S = 0x53;
        private const uint VK_T = 0x54;
        private const uint VK_U = 0x55;
        private const uint VK_W = 0x57;
        private const uint VK_X = 0x58;
        private const uint VK_F2 = 0x71;
        private const uint VK_G = 0x47;
        private const int WM_HOTKEY = 0x0312;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private System.Windows.Interop.HwndSource _hwndSource;
        private bool _lightNovelGlobalHotkeysEnabled;
        private IntPtr _globalToggleKeyboardHook = IntPtr.Zero;
        private LowLevelKeyboardProc _globalToggleKeyboardProc;
        private bool _globalToggleKeyConsumed;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        static MainWindow()
        {
            InitializeHttpClientState();
            RunHaibabaChapterExtractionSelfCheck();
        }

        public MainWindow()
        {
            Instance = this;
            try
            {
                var currentProc = System.Diagnostics.Process.GetCurrentProcess();
                currentProc.PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
                currentProc.ProcessorAffinity = (IntPtr)((1L << Environment.ProcessorCount) - 1);

                // Tránh nghẽn ThreadPool (starvation) trên CPU ít nhân khi chạy nhiều luồng tải sách song song
                System.Threading.ThreadPool.SetMinThreads(128, 128);

                // Tăng giới hạn kết nối mạng đồng thời tối đa đến 1 host để tránh nghẽn luồng socket
                System.Net.ServicePointManager.DefaultConnectionLimit = 512;
            }
            catch { }

            UnfreezeApplicationBrushes();
            InitializeComponent();
            InitializeWorkspaceShell();
            HookDisplaySettingsChanged();
            PreviewMouseWheel += MainWindow_PreviewMouseWheel;
            Loaded += (s, e) => ApplyAdaptiveLayout(new Size(ActualWidth, ActualHeight));
            ContentRendered += MainWindow_ContentRendered;
            _isVietnameseUi = true;
            ApplyCurrentUiLanguage();
            InitializeGalleryListAutosave();
            InitializePasswordManagerControls();
            ApplyBuildInfoText();
            WirePauseButtonToggle();
            InitializeLogPanels();
            InitializeDilibDefaults();
            InitializeMangadexControls();
            InitializeGlobalAutoPasteClipboard();
            dgResults.ItemsSource = _scrapedItems;
            dgResults.LoadingRow += DgResults_LoadingRow;
            _scrapedItems.CollectionChanged += ResultsThumbnailItems_CollectionChanged;
            SetResultsPresentationMode(false, false);
            UpdateStats();
            InitializeDownloadMissingChapterTab();
            InitializeWebviewCpuControls();

            try
            {
                txtDownloadPath.Text = PortablePaths.DefaultDownloadRoot;
            }
            catch
            {
            }

            Log("System initialized. Ready for commands.");

            Loaded += (s, e) =>
            {
                StyleComboBoxPopup(cmbCreateSubfolderDomain);
                StyleComboBoxPopup(cmbConnections);
                StyleComboBoxPopup(cmbMultiDownload);
                StyleComboBoxPopup(cmbDownloadFolderType);
                StyleComboBoxPopup(cmbWebviewCpuAffinity);
                StyleComboBoxPopup(cmbWebviewCpuPriority);

                CommandBindings.Add(new CommandBinding(ApplicationCommands.New, WindowNew_Executed));
                CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, WindowSave_Executed));
                CommandBindings.Add(new CommandBinding(ApplicationCommands.Open, WindowOpen_Executed));
                CommandBindings.Add(new CommandBinding(StartLightNovelAutoCopyCommand, BtnStartLightNovelCopy_Click));
                CommandBindings.Add(new CommandBinding(StopLightNovelAutoCopyCommand, BtnStopLightNovelCopy_Click));
                InputBindings.Add(new KeyBinding(ApplicationCommands.New, new KeyGesture(Key.N, ModifierKeys.Control)));
                InputBindings.Add(new KeyBinding(ApplicationCommands.Save, new KeyGesture(Key.S, ModifierKeys.Control)));
                InputBindings.Add(new KeyBinding(ApplicationCommands.Open, new KeyGesture(Key.O, ModifierKeys.Control)));
                InputBindings.Add(new KeyBinding(StartLightNovelAutoCopyCommand, new KeyGesture(Key.F2, ModifierKeys.Control)));
                InputBindings.Add(new KeyBinding(StopLightNovelAutoCopyCommand, new KeyGesture(Key.F2, ModifierKeys.Alt)));

                var view = ResultsView;
                if (view != null && view.SortDescriptions.Count == 0)
                {
                    view.SortDescriptions.Add(new SortDescription("OriginalIndex", ListSortDirection.Ascending));
                }

                EnsureLightNovelFloatingControlWindow();
                if (_lightNovelFloatingControlWindow != null && !_lightNovelFloatingControlWindow.IsVisible)
                {
                    _lightNovelFloatingControlWindow.ShowWithoutActivationSafe();
                }

                UpdateLightNovelFloatingControlState();

                GetCurrentConnectionLimit();
                GetCurrentMultiDownloadLimit();
            };

            Closing += (s, e) =>
            {
                UnhookDisplaySettingsChanged();
                DisposeLightNovelFocusTrayIcon();
                _lightNovelFloatingControlWindow?.Close();
                StopGlobalAutoPasteClipboard();
                FlushRuntimeStateForShutdown();

                if (_hwndSource != null)
                {
                    IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    UnregisterLightNovelGlobalHotkeys(handle);
                    UnregisterHotKey(handle, HOTKEY_ID);
                    _hwndSource.RemoveHook(HwndHook);
                    _hwndSource = null;
                }

                UninstallGlobalToggleKeyboardHook();

                // Diệt toàn bộ các tiến trình WebView2/Chrome con đang chạy
                try
                {
                    var currentProc = System.Diagnostics.Process.GetCurrentProcess();
                    int currentPid = currentProc.Id;
                    var descendants = new System.Collections.Generic.HashSet<int>();
                    GetDescendantProcessIds(currentPid, descendants);
                    foreach (int pid in descendants)
                    {
                        try
                        {
                            using (var p = System.Diagnostics.Process.GetProcessById(pid))
                            {
                                p.Kill();
                            }
                        }
                        catch { }
                    }
                }
                catch { }

                // Cưỡng chế thoát ứng dụng tránh bị deadlock chạy ngầm
                try
                {
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                }
                catch { }
            };

        }

        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            ContentRendered -= MainWindow_ContentRendered;
            Dispatcher.BeginInvoke(new Action(PrimeStartupInputRouting), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Dispatcher.BeginInvoke(new Action(() => _ = RefreshNettruyenTechRedirectDomainAsync()), System.Windows.Threading.DispatcherPriority.Background);
            Dispatcher.BeginInvoke(new Action(() => _ = EnsureDamconuongRedirectDomainAsync()), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void PrimeStartupInputRouting()
        {
            AllowDrop = true;
            if (rootLayout != null)
            {
                rootLayout.AllowDrop = true;
            }

            if (dgResults != null)
            {
                dgResults.AllowDrop = true;
            }

            if (!IsActive)
            {
                Activate();
            }

            Focus();
            if (dgResults != null && dgResults.IsVisible && dgResults.IsEnabled)
            {
                dgResults.Focus();
            }
            else
            {
                System.Windows.Input.Keyboard.Focus(this);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(HwndHook);
            RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_F);
            InstallGlobalToggleKeyboardHook();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                if (hotkeyId == HOTKEY_ID)
                {
                    // Ctrl+Shift+F toggle float luôn hoạt động kể cả khi đang gõ
                    Dispatcher.BeginInvoke(new Action(ToggleLightNovelFloatingControlWindow));
                    handled = true;
                    return IntPtr.Zero;
                }

                // Không kích hoạt hotkey khi người dùng đang gõ trong ô nhập liệu
                if (IsTextInputFocused()) return IntPtr.Zero;

                if (_lightNovelGlobalHotkeysEnabled && HandleLightNovelGlobalHotkey(hotkeyId))
                {
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            return IntPtr.Zero;
        }

        private static bool IsTextInputFocused()
        {
            var focused = Keyboard.FocusedElement;
            if (focused is TextBox tb && tb.IsEnabled) return true;
            if (focused is PasswordBox pb && pb.IsEnabled) return true;
            if (focused is RichTextBox rtb && rtb.IsEnabled && !rtb.IsReadOnly) return true;
            if (focused is ComboBox cb && cb.IsEditable && cb.IsEnabled) return true;
            return false;
        }

        private void ToggleLightNovelGlobalHotkeys()
        {
            SetLightNovelGlobalHotkeysEnabled(!_lightNovelGlobalHotkeysEnabled);
        }

        private void SetLightNovelGlobalHotkeysEnabled(bool enabled)
        {
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                _lightNovelGlobalHotkeysEnabled = enabled;
                UpdateLightNovelFloatingControlState();
                return;
            }

            if (enabled)
            {
                RegisterLightNovelGlobalHotkeys(handle);
            }
            else
            {
                UnregisterLightNovelGlobalHotkeys(handle);
            }

            _lightNovelGlobalHotkeysEnabled = enabled;
            lblStatus.Text = enabled
                ? "Global key: ON"
                : "Global key: OFF";
            PlayGlobalKeyToggleBeep(enabled);
            UpdateLightNovelFloatingControlState();
        }

        private void InstallGlobalToggleKeyboardHook()
        {
            if (_globalToggleKeyboardHook != IntPtr.Zero)
            {
                return;
            }

            _globalToggleKeyboardProc = GlobalToggleKeyboardHookProc;
            IntPtr moduleHandle = GetModuleHandle(null);
            _globalToggleKeyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _globalToggleKeyboardProc, moduleHandle, 0);
            if (_globalToggleKeyboardHook == IntPtr.Zero)
            {
                Log("[GlobalKey] Không cài được hook Alt+Shift+G.");
            }
        }

        private void UninstallGlobalToggleKeyboardHook()
        {
            if (_globalToggleKeyboardHook == IntPtr.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(_globalToggleKeyboardHook);
            _globalToggleKeyboardHook = IntPtr.Zero;
            _globalToggleKeyboardProc = null;
            _globalToggleKeyConsumed = false;
        }

        private IntPtr GlobalToggleKeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN || message == WM_KEYUP || message == WM_SYSKEYUP)
                {
                    KBDLLHOOKSTRUCT keyboardData = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    if (keyboardData.vkCode == VK_G)
                    {
                        bool altDown = (GetAsyncKeyState(0x12) & 0x8000) != 0;
                        bool shiftDown = (GetAsyncKeyState(0x10) & 0x8000) != 0;

                        if ((message == WM_KEYDOWN || message == WM_SYSKEYDOWN) && altDown && shiftDown)
                        {
                            if (!_globalToggleKeyConsumed)
                            {
                                _globalToggleKeyConsumed = true;
                                Dispatcher.BeginInvoke(new Action(ToggleLightNovelGlobalHotkeys));
                            }
                        }
                        else if (message == WM_KEYUP || message == WM_SYSKEYUP || !altDown || !shiftDown)
                        {
                            _globalToggleKeyConsumed = false;
                        }
                    }
                }
            }

            return CallNextHookEx(_globalToggleKeyboardHook, nCode, wParam, lParam);
        }

        private static void PlayGlobalKeyToggleBeep(bool enabled)
        {
            try
            {
                int frequency = enabled ? 1320 : 820;
                int duration = enabled ? 140 : 110;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Console.Beep(frequency, duration);
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }

        private void RegisterLightNovelGlobalHotkeys(IntPtr handle)
        {
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_SHOW_ID, VK_M, "float");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_PIN_ID, VK_P, "pin");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_AUTOPASTE_ID, VK_A, "auto paste");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_FOCUS_ID, VK_F, "focus");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_DOWNLOAD_ID, VK_D, "download");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_RETRY_ID, VK_R, "retry");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_COPY_ID, VK_C, "copy text");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_OPENFOLDER_ID, VK_O, "open folder");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_SHUTDOWN_ID, VK_S, "shutdown");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_DELETECOOKIE_ID, VK_L, "delete cookie");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_CLEANTEMP_ID, VK_X, "clean temp");
            TryRegisterLightNovelGlobalHotkey(handle, HOTKEY_GLOBAL_TWEAK_ID, VK_T, "tweak");

            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_OPEN_FOLDER_ID, MOD_CONTROL | MOD_SHIFT, VK_O, "Ctrl+Shift+O");
            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_START_COPY_ID, MOD_CONTROL, VK_F2, "Ctrl+F2");
            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_STOP_COPY_ID, MOD_ALT, VK_F2, "Alt+F2");
            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_CHOOSE_SOURCE_ID, MOD_CONTROL | MOD_SHIFT, VK_S, "Ctrl+Shift+S");
            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_DOWNLOAD_SECTION_ID, MOD_CONTROL | MOD_SHIFT, VK_D, "Ctrl+Shift+D");
            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_WATCH_SECTION_ID, MOD_CONTROL | MOD_SHIFT, VK_W, "Ctrl+Shift+W");
            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_ABOUT_SECTION_ID, MOD_CONTROL | MOD_SHIFT, VK_A, "Ctrl+Shift+A");
            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_TRACELOG_SECTION_ID, MOD_CONTROL | MOD_SHIFT, VK_L, "Ctrl+Shift+L");
            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_UPDATE_SECTION_ID, MOD_CONTROL | MOD_SHIFT, VK_U, "Ctrl+Shift+U");
            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_DOWNLOAD_TOGGLE_ID, MOD_CONTROL | MOD_SHIFT, VK_T, "Ctrl+Shift+T");
            TryRegisterProjectHotkey(handle, HOTKEY_PROJECT_RETRY_ID, MOD_CONTROL | MOD_SHIFT, VK_R, "Ctrl+Shift+R");
        }

        private void UnregisterLightNovelGlobalHotkeys(IntPtr handle)
        {
            UnregisterHotKey(handle, HOTKEY_GLOBAL_SHOW_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_PIN_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_AUTOPASTE_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_FOCUS_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_DOWNLOAD_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_RETRY_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_COPY_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_OPENFOLDER_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_SHUTDOWN_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_DELETECOOKIE_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_CLEANTEMP_ID);
            UnregisterHotKey(handle, HOTKEY_GLOBAL_TWEAK_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_OPEN_FOLDER_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_NEW_LIST_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_SAVE_LIST_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_OPEN_LIST_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_START_COPY_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_STOP_COPY_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_CHOOSE_SOURCE_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_DOWNLOAD_SECTION_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_WATCH_SECTION_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_ABOUT_SECTION_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_TRACELOG_SECTION_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_UPDATE_SECTION_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_DOWNLOAD_TOGGLE_ID);
            UnregisterHotKey(handle, HOTKEY_PROJECT_RETRY_ID);
        }

        private void TryRegisterLightNovelGlobalHotkey(IntPtr handle, int id, uint vk, string label)
        {
            UnregisterHotKey(handle, id);
            if (!RegisterHotKey(handle, id, MOD_CONTROL | MOD_ALT, vk))
            {
                Log($"[GlobalKey] Không đăng ký được Ctrl+Alt+{label.ToUpperInvariant()}.");
            }
        }

        private void TryRegisterProjectHotkey(IntPtr handle, int id, uint modifiers, uint vk, string combo)
        {
            UnregisterHotKey(handle, id);
            if (!RegisterHotKey(handle, id, modifiers, vk))
            {
                Log($"[GlobalKey] Không đăng ký được {combo}.");
            }
        }

        private bool HandleLightNovelGlobalHotkey(int hotkeyId)
        {
            switch (hotkeyId)
            {
                case HOTKEY_GLOBAL_SHOW_ID:
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        EnsureLightNovelFloatingControlWindow();
                        _lightNovelFloatingControlWindow?.ShowForMoveFromGlobalKey();
                    }));
                    return true;
                case HOTKEY_GLOBAL_PIN_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.TogglePinFromGlobalKey()));
                    return true;
                case HOTKEY_GLOBAL_AUTOPASTE_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.ToggleAutoPasteFromGlobalKey()));
                    return true;
                case HOTKEY_GLOBAL_FOCUS_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.ToggleFocusFromGlobalKey()));
                    return true;
                case HOTKEY_GLOBAL_DOWNLOAD_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.ToggleDownloadFromGlobalKey()));
                    return true;
                case HOTKEY_GLOBAL_RETRY_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.ToggleRetryFromGlobalKey()));
                    return true;
                case HOTKEY_GLOBAL_COPY_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.ToggleCopyFromGlobalKey()));
                    return true;
                case HOTKEY_GLOBAL_OPENFOLDER_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.OpenFolderFromGlobalKey()));
                    return true;
                case HOTKEY_GLOBAL_SHUTDOWN_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.ToggleShutdownFromGlobalKey()));
                    return true;
                case HOTKEY_GLOBAL_DELETECOOKIE_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.DeleteCookiesFromGlobalKey()));
                    return true;
                case HOTKEY_GLOBAL_CLEANTEMP_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.ClearTempFromGlobalKey()));
                    return true;
                case HOTKEY_GLOBAL_TWEAK_ID:
                    Dispatcher.BeginInvoke(new Action(() => _lightNovelFloatingControlWindow?.OpenTweakFromGlobalKey()));
                    return true;
                case HOTKEY_PROJECT_OPEN_FOLDER_ID:
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        EnsureProjectWindowVisible();
                        BtnOpenFolder_Click(this, new RoutedEventArgs());
                    }));
                    return true;
                case HOTKEY_PROJECT_NEW_LIST_ID:
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        EnsureProjectWindowVisible();
                        WindowNew_Executed(this, null);
                    }));
                    return true;
                case HOTKEY_PROJECT_SAVE_LIST_ID:
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        EnsureProjectWindowVisible();
                        WindowSave_Executed(this, null);
                    }));
                    return true;
                case HOTKEY_PROJECT_OPEN_LIST_ID:
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        EnsureProjectWindowVisible();
                        WindowOpen_Executed(this, null);
                    }));
                    return true;
                case HOTKEY_PROJECT_START_COPY_ID:
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        EnsureProjectWindowVisible();
                        await StartLightNovelAutoCopyAsync();
                    }));
                    return true;
                case HOTKEY_PROJECT_STOP_COPY_ID:
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        EnsureProjectWindowVisible();
                        StopLightNovelAutoCopy();
                    }));
                    return true;
                case HOTKEY_PROJECT_CHOOSE_SOURCE_ID:
                    Dispatcher.BeginInvoke(new Action(() => ShowProjectWindowForSection(AppSection.ChooseSource)));
                    return true;
                case HOTKEY_PROJECT_DOWNLOAD_SECTION_ID:
                    Dispatcher.BeginInvoke(new Action(() => ShowProjectWindowForSection(AppSection.Download)));
                    return true;
                case HOTKEY_PROJECT_WATCH_SECTION_ID:
                    Dispatcher.BeginInvoke(new Action(() => ShowProjectWindowForSection(AppSection.Watch)));
                    return true;
                case HOTKEY_PROJECT_ABOUT_SECTION_ID:
                    Dispatcher.BeginInvoke(new Action(() => ShowProjectWindowForSection(AppSection.About)));
                    return true;
                case HOTKEY_PROJECT_TRACELOG_SECTION_ID:
                    Dispatcher.BeginInvoke(new Action(() => ShowProjectWindowForSection(AppSection.TraceLog)));
                    return true;
                case HOTKEY_PROJECT_UPDATE_SECTION_ID:
                    Dispatcher.BeginInvoke(new Action(() => ShowProjectWindowForSection(AppSection.Update)));
                    return true;
                case HOTKEY_PROJECT_DOWNLOAD_TOGGLE_ID:
                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        EnsureProjectWindowVisible();
                        if (_downloadCts == null && btnStartDownload?.IsChecked != true)
                        {
                            await StartPictureDownloadFromFloatingAsync();
                        }
                        else
                        {
                            StopPictureDownloadFromFloating();
                        }
                    }));
                    return true;
                case HOTKEY_PROJECT_RETRY_ID:
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        EnsureProjectWindowVisible();
                        BtnRetryErrors_Click(this, new RoutedEventArgs());
                    }));
                    return true;
                default:
                    return false;
            }
        }

        private void EnsureProjectWindowVisible()
        {
            if (_lightNovelFocusTrayHidden)
            {
                RestoreMainWindowFromFocusTray(activateWindow: true);
            }
            else
            {
                if (!IsVisible)
                {
                    Show();
                }

                if (!ShowInTaskbar)
                {
                    ShowInTaskbar = true;
                }

                if (Opacity <= 0d)
                {
                    Opacity = 1d;
                }

                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }

                Topmost = true;
                Activate();
                Focus();
                Topmost = false;
            }
        }

        private void ShowProjectWindowForSection(AppSection section)
        {
            EnsureProjectWindowVisible();
            SelectAppSection(section);
        }

        private static void InitializeHttpClientState()
        {
            _cookieContainer = new CookieContainer();
            _httpHandler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                CookieContainer = _cookieContainer,
                UseCookies = true
            };
            _httpClient = new HttpClient(_httpHandler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", _defaultUserAgent);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        internal void FlushRuntimeStateForShutdown()
        {
            if (Interlocked.Exchange(ref _shutdownPersistenceStarted, 1) != 0)
            {
                return;
            }

            Action persist = () =>
            {
                try
                {
                    SaveActiveGalleryListSnapshot();
                }
                catch
                {
                }

                try
                {
                    CleanupActiveTempFolders();
                }
                catch
                {
                }
            };

            try
            {
                if (Dispatcher != null && !Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    if (Dispatcher.CheckAccess())
                    {
                        persist();
                    }
                    else
                    {
                        Dispatcher.Invoke(persist, DispatcherPriority.Send);
                    }
                }
                else
                {
                    persist();
                }
            }
            catch
            {
                persist();
            }
        }

        /// <summary>Trả true nếu domain vừa solve captcha thành công trong CaptchaCooldownSeconds giây qua.</summary>
        private static bool IsCaptchaCooldownActive(string url)
        {
            string host = NormalizeCookieHostKey(url);
            if (string.IsNullOrEmpty(host)) return false;
            if (_captchaSolvedAtUtc.TryGetValue(host, out DateTime solvedAt))
            {
                return (DateTime.UtcNow - solvedAt).TotalSeconds < CaptchaCooldownSeconds;
            }
            return false;
        }

        /// <summary>Ghi nhớ thời điểm solve captcha thành công cho domain.</summary>
        private static void MarkCaptchaSolved(string url)
        {
            string host = NormalizeCookieHostKey(url);
            if (!string.IsNullOrEmpty(host))
            {
                _captchaSolvedAtUtc[host] = DateTime.UtcNow;
            }
        }

        private static string NormalizeCookieHostKey(string urlOrHost)
        {
            if (string.IsNullOrWhiteSpace(urlOrHost))
            {
                return string.Empty;
            }

            try
            {
                string host = new Uri(urlOrHost).Host ?? string.Empty;
                // Map nhentai CDN subdomains (i1/i2/t1/t2...) to their root domain
                // so they share the same cookie container (Cloudflare cookies)
                if (host.EndsWith(".nhentai.net", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".nhentai.xxx", StringComparison.OrdinalIgnoreCase))
                    return "nhentai.net";
                if (host.EndsWith(".nhentaimg.com", StringComparison.OrdinalIgnoreCase))
                    return "nhentaimg.com";
                return host;
            }
            catch
            {
                return urlOrHost.Trim().Trim('/');
            }
        }

        private static CookieContainer GetScopedCookieContainer(string urlOrHost)
        {
            string host = NormalizeCookieHostKey(urlOrHost);
            if (string.IsNullOrWhiteSpace(host))
            {
                return _cookieContainer;
            }

            return _cookieContainersByHost.GetOrAdd(host, _ => new CookieContainer());
        }

        private void RememberScopedUserAgent(string urlOrHost, string userAgent)
        {
            string host = NormalizeCookieHostKey(urlOrHost);
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(userAgent))
            {
                return;
            }

            _userAgentsByHost[host] = userAgent;
        }

        private string GetScopedUserAgent(string urlOrHost)
        {
            string host = NormalizeCookieHostKey(urlOrHost);
            if (!string.IsNullOrWhiteSpace(host) &&
                _userAgentsByHost.TryGetValue(host, out string userAgent) &&
                !string.IsNullOrWhiteSpace(userAgent))
            {
                return userAgent;
            }

            return _defaultUserAgent;
        }

        private static Uri TryBuildCookieUri(string urlOrHost, string cookieDomain, Uri fallbackUri)
        {
            Uri targetUri = fallbackUri;
            if (!string.IsNullOrWhiteSpace(cookieDomain))
            {
                Uri.TryCreate("https://" + cookieDomain.TrimStart('.'), UriKind.Absolute, out targetUri);
            }
            else if (targetUri == null && !string.IsNullOrWhiteSpace(urlOrHost))
            {
                string host = NormalizeCookieHostKey(urlOrHost);
                if (!string.IsNullOrWhiteSpace(host))
                {
                    Uri.TryCreate("https://" + host, UriKind.Absolute, out targetUri);
                }
            }

            return targetUri;
        }

        private static Cookie CloneCookie(Cookie cookie, string fallbackDomain)
        {
            string domain = string.IsNullOrWhiteSpace(cookie?.Domain) ? fallbackDomain : cookie.Domain;
            var clone = new Cookie(cookie?.Name ?? string.Empty, cookie?.Value ?? string.Empty, string.IsNullOrWhiteSpace(cookie?.Path) ? "/" : cookie.Path, domain ?? string.Empty)
            {
                HttpOnly = cookie != null && cookie.HttpOnly,
                Secure = cookie != null && cookie.Secure
            };

            if (cookie != null && cookie.Expires != DateTime.MinValue)
            {
                clone.Expires = cookie.Expires;
            }

            return clone;
        }

        private void MergeCookiesIntoScopedContainer(string urlOrHost, Uri fallbackUri, System.Collections.Generic.IEnumerable<System.Net.Cookie> cookies)
        {
            if (cookies == null)
            {
                return;
            }

            CookieContainer container = GetScopedCookieContainer(urlOrHost);
            foreach (System.Net.Cookie cookie in cookies)
            {
                try
                {
                    Uri targetUri = TryBuildCookieUri(urlOrHost, cookie?.Domain, fallbackUri);
                    if (targetUri == null || string.IsNullOrWhiteSpace(cookie?.Name))
                    {
                        continue;
                    }

                    container.Add(targetUri, CloneCookie(cookie, targetUri.Host));
                }
                catch
                {
                }
            }
        }

        private HttpClient CreateScopedHttpClient(string urlOrHost)
        {
            string host = NormalizeCookieHostKey(urlOrHost) ?? "default";
            var handler = _scopedHandlers.GetOrAdd(host, h =>
            {
                bool useCookies = true;
                if (!string.IsNullOrWhiteSpace(urlOrHost) && urlOrHost.IndexOf("haibabamanga.somee.com", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    useCookies = false;
                }
                if (!string.IsNullOrWhiteSpace(urlOrHost) && urlOrHost.IndexOf("mangadex.org", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    useCookies = false;
                }

                var newHandler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    UseCookies = useCookies
                };

                if (useCookies)
                {
                    newHandler.CookieContainer = GetScopedCookieContainer(urlOrHost);
                }
                return newHandler;
            });

            var client = new HttpClient(handler, disposeHandler: false);
            client.Timeout = _httpClient != null ? _httpClient.Timeout : TimeSpan.FromSeconds(30);
            try
            {
                string ua = GetScopedUserAgent(urlOrHost);
                if (!string.IsNullOrWhiteSpace(ua))
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(ua);
                }
            }
            catch {}
            if (!string.IsNullOrWhiteSpace(urlOrHost) && urlOrHost.IndexOf("mangadex.org", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                client.DefaultRequestHeaders.ConnectionClose = true;
                try
                {
                    if (Uri.TryCreate(urlOrHost, UriKind.Absolute, out Uri uri))
                    {
                        ServicePoint point = ServicePointManager.FindServicePoint(uri);
                        if (point != null)
                        {
                            point.ConnectionLeaseTimeout = 0;
                            point.MaxIdleTime = 1000;
                        }
                    }
                }
                catch
                {
                }
            }
            string userAgent = GetScopedUserAgent(urlOrHost);
            if (!string.IsNullOrWhiteSpace(userAgent))
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            }

            return client;
        }

        private void HookDisplaySettingsChanged()
        {
            if (_displaySettingsHooked)
            {
                return;
            }

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            _displaySettingsHooked = true;
        }

        private void UnhookDisplaySettingsChanged()
        {
            if (!_displaySettingsHooked)
            {
                return;
            }

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            _displaySettingsHooked = false;
        }

        private void WindowNew_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            BtnNewList_Click(sender, new RoutedEventArgs());
        }

        private void WindowSave_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            BtnSaveCustom_Click(sender, new RoutedEventArgs());
        }

        private void WindowOpen_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            BtnLoadCustom_Click(sender, new RoutedEventArgs());
        }

        private void InitializeGlobalAutoPasteClipboard()
        {
            if (_globalAutoPasteTimer != null)
            {
                return;
            }

            _globalAutoPasteTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _globalAutoPasteTimer.Tick += async (s, e) => await GlobalAutoPasteClipboardTickAsync();
        }

        private async System.Threading.Tasks.Task GlobalAutoPasteClipboardTickAsync()
        {
            if (!_globalAutoPasteEnabled || _globalAutoPasteBusy)
            {
                return;
            }

            string text;
            try
            {
                if (!Clipboard.ContainsText())
                {
                    return;
                }

                text = Clipboard.GetText();
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text) || string.Equals(text, _globalAutoPasteLastClipboardText, StringComparison.Ordinal))
            {
                return;
            }

            var supportedLines = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || !IsSupportedDomain(line) || !seen.Add(line))
                {
                    continue;
                }

                supportedLines.Add(line);
            }

            _globalAutoPasteLastClipboardText = text;
            if (supportedLines.Count == 0)
            {
                return;
            }

            _globalAutoPasteBusy = true;
            try
            {
                await AppendSupportedInputLinks(string.Join(Environment.NewLine, supportedLines));
            }
            finally
            {
                _globalAutoPasteBusy = false;
            }
        }

        private void ToggleGlobalAutoPasteClipboard()
        {
            SetGlobalAutoPasteClipboardEnabled(!_globalAutoPasteEnabled);
        }

        private void SetGlobalAutoPasteClipboardEnabled(bool enabled)
        {
            _globalAutoPasteEnabled = enabled;
            if (_globalAutoPasteTimer == null)
            {
                InitializeGlobalAutoPasteClipboard();
            }

            if (enabled)
            {
                _globalAutoPasteLastClipboardText = null;
                _globalAutoPasteTimer.Start();
                lblStatus.Text = _isVietnameseUi ? "Auto paste clipboard bật." : "Clipboard auto paste on.";
            }
            else
            {
                StopGlobalAutoPasteClipboard();
            }

            UpdateLightNovelFloatingControlState();
        }

        private void StopGlobalAutoPasteClipboard()
        {
            _globalAutoPasteEnabled = false;
            _globalAutoPasteBusy = false;
            _globalAutoPasteLastClipboardText = null;
            _globalAutoPasteTimer?.Stop();
            UpdateLightNovelFloatingControlState();
        }

        private void PlaySoundResource(string filename)
        {
            Task.Run(() =>
            {
                try
                {
                    string localPath = Path.Combine(PortablePaths.AppRoot, "bin", "ringtones", filename);
                    if (File.Exists(localPath))
                    {
                        using (var player = new System.Media.SoundPlayer(localPath))
                        {
                            player.PlaySync();
                        }
                        return;
                    }

                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    string[] resources = assembly.GetManifestResourceNames();
                    string resourceName = resources.FirstOrDefault(r => r.IndexOf(filename, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (resourceName != null)
                    {
                        using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                        {
                            if (stream != null)
                            {
                                using (var player = new System.Media.SoundPlayer(stream))
                                {
                                    player.PlaySync();
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[Sound] Error playing sound {filename}: {ex.Message}");
                }
            });
        }
    }
}
