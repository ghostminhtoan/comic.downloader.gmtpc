using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private static readonly RoutedUICommand StartLightNovelAutoCopyCommand =
            new RoutedUICommand("Start light novel auto copy", "StartLightNovelAutoCopy", typeof(MainWindow));
        private static readonly RoutedUICommand StopLightNovelAutoCopyCommand =
            new RoutedUICommand("Stop light novel auto copy", "StopLightNovelAutoCopy", typeof(MainWindow));
        private static CookieContainer _cookieContainer;
        private static HttpClientHandler _httpHandler;
        private static HttpClient _httpClient;
        private static readonly string _defaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private static readonly SemaphoreSlim _captchaSemaphore = new SemaphoreSlim(1, 1);
        private static volatile bool _isCaptchaWindowActive = false;
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
        internal ObservableCollection<GalleryItem> _scrapedItems = new ObservableCollection<GalleryItem>();
        internal ObservableCollection<GalleryItem> _lightNovelItems = new ObservableCollection<GalleryItem>();
        internal DuplicateWindow _duplicateWindowInstance;
        internal BookmarkHistoryManager _bookmarkManager = new BookmarkHistoryManager();
        private BookmarkHistoryWindow _bookmarkHistoryWindowInstance;
        private readonly System.Windows.Controls.ProgressBar progressBar = new System.Windows.Controls.ProgressBar();
        private bool _startupArchivePromptShown;
        private volatile int _currentMaxParallelBooks = 2;
        private DynamicSemaphore _activeBookSemaphore;
        private int _frameCount = 0;
        private System.Diagnostics.Stopwatch _fpsStopwatch = new System.Diagnostics.Stopwatch();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_F = 0x46;
        private const int WM_HOTKEY = 0x0312;
        private System.Windows.Interop.HwndSource _hwndSource;

        static MainWindow()
        {
            InitializeHttpClientState();
        }

        public MainWindow()
        {
            UnfreezeApplicationBrushes();
            InitializeComponent();
            InitializeWorkspaceShell();
            HookDisplaySettingsChanged();
            PreviewMouseWheel += MainWindow_PreviewMouseWheel;
            Loaded += (s, e) => ApplyAdaptiveLayout(new Size(ActualWidth, ActualHeight));
            _isVietnameseUi = true;
            ApplyCurrentUiLanguage();
            InitializeGalleryListAutosave();
            ApplyBuildInfoText();
            WirePauseButtonToggle();
            InitializeLogPanels();
            InitializeDilibDefaults();
            InitializeGlobalAutoPasteClipboard();
            dgResults.ItemsSource = _scrapedItems;
            UpdateStats();

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
                StyleComboBoxPopup(cmbNhentaiSort);
                StyleComboBoxPopup(cmbConnections);
                StyleComboBoxPopup(cmbMultiDownload);
                StyleComboBoxPopup(cmbDownloadFolderType);

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
            };

            Closing += (s, e) =>
            {
                UnhookDisplaySettingsChanged();
                DisposeLightNovelFocusTrayIcon();
                _lightNovelFloatingControlWindow?.Close();
                StopGlobalAutoPasteClipboard();
                SaveActiveGalleryListSnapshot();
                CleanupActiveTempFolders();

                if (_hwndSource != null)
                {
                    IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    UnregisterHotKey(handle, HOTKEY_ID);
                    _hwndSource.RemoveHook(HwndHook);
                    _hwndSource = null;
                }
            };

            InitializeFpsCounter();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(HwndHook);
            RegisterHotKey(handle, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_F);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Dispatcher.BeginInvoke(new Action(ToggleLightNovelFloatingControlWindow));
                handled = true;
            }
            return IntPtr.Zero;
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

        private void InitializeFpsCounter()
        {
            _fpsStopwatch.Start();
            System.Windows.Media.CompositionTarget.Rendering += (s, e) =>
            {
                _frameCount++;
                if (_fpsStopwatch.ElapsedMilliseconds >= 500)
                {
                    double fps = (_frameCount * 1000.0) / _fpsStopwatch.ElapsedMilliseconds;
                    txtFpsCounter.Text = $"FPS: {fps:0.0}";
                    _frameCount = 0;
                    _fpsStopwatch.Restart();
                }
            };
        }
    }
}
