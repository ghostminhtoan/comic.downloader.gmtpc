using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        internal enum ReaderFitMode
        {
            FitWidth,
            FitHeight,
            ActualSize,
            VerticalScroll,
            HorizontalScrollLTR,
            HorizontalScrollRTL
        }

        private enum ReaderWatchSortField
        {
            DateModified,
            Name
        }

        private enum ReaderWatchExternalApp
        {
            FastStone,
            Bandiview
        }

        private sealed class ReaderWatchSortState
        {
            public ReaderWatchSortField Field { get; set; }

            public bool Ascending { get; set; }
        }

        private sealed class ReaderSortKeyComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x == null)
                {
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                string[] leftParts = Regex.Split(x, "(\\d+)");
                string[] rightParts = Regex.Split(y, "(\\d+)");
                int max = Math.Max(leftParts.Length, rightParts.Length);

                for (int i = 0; i < max; i++)
                {
                    if (i >= leftParts.Length)
                    {
                        return -1;
                    }

                    if (i >= rightParts.Length)
                    {
                        return 1;
                    }

                    bool leftIsNumber = int.TryParse(leftParts[i], out int leftNumber);
                    bool rightIsNumber = int.TryParse(rightParts[i], out int rightNumber);

                    int result;
                    if (leftIsNumber && rightIsNumber)
                    {
                        result = leftNumber.CompareTo(rightNumber);
                    }
                    else
                    {
                        result = string.Compare(leftParts[i], rightParts[i], StringComparison.OrdinalIgnoreCase);
                    }

                    if (result != 0)
                    {
                        return result;
                    }
                }

                return 0;
            }
        }

        private readonly ReaderSortKeyComparer _readerSortComparer = new ReaderSortKeyComparer();
        private readonly ReaderWatchSortState _readerMangaDomainSortState = new ReaderWatchSortState { Field = ReaderWatchSortField.DateModified, Ascending = false };
        private readonly ReaderWatchSortState _readerMangaBookSortState = new ReaderWatchSortState { Field = ReaderWatchSortField.DateModified, Ascending = false };
        private readonly ReaderWatchSortState _readerNovelDomainSortState = new ReaderWatchSortState { Field = ReaderWatchSortField.DateModified, Ascending = false };
        private readonly ReaderWatchSortState _readerNovelBookSortState = new ReaderWatchSortState { Field = ReaderWatchSortField.DateModified, Ascending = false };
        private List<ReaderMangaItem> _readerLibrary = new List<ReaderMangaItem>();
        private List<ReaderDomainItem> _readerDomains = new List<ReaderDomainItem>();
        private ReaderDomainItem _currentReaderDomain;
        private ReaderMangaItem _currentReaderManga;
        private ReaderChapterItem _currentReaderChapter;
        private ReaderPageItem _currentReaderPage;
        internal ReaderFitMode _readerFitMode = ReaderFitMode.FitWidth;
        private ReaderFullscreenWindow _fullscreenWindow = null;
        private string _lastReaderLibraryRoot;
        private bool _readerSelectionGuard;
#pragma warning disable 0649,0169
        private ScrollViewer _readerScrollViewer;
        private Border _readerViewportHost;
        private StackPanel _readerStagePanel;
        private readonly ScaleTransform _readerScaleTransform = new ScaleTransform(1d, 1d);
        private readonly List<FrameworkElement> _readerPageElements = new List<FrameworkElement>();
        private double _readerZoom = 1d;
        private bool _readerSyncingFromViewport;
        private bool _readerIsMousePanning;
        private Point _readerMousePanStartPoint;
        private double _readerMousePanStartHorizontalOffset;
        private double _readerMousePanStartVerticalOffset;
        private Cursor _readerMousePanPreviousCursor;
        private ListBox _readerMangaList;
        private ListBox _readerChapterList;
        private TextBlock _readerChapterStatsText;
        private ContentControl _readerChapterContentHost;
        private Border _readerChapterIssuePanel;
        private DataGrid _readerChapterIssueGrid;
        private ComboBox _readerPageCombo;
        private ListBox _readerFileList;
        private ListBox _readerDomainList;
        private TextBlock _readerSummaryText;
        private TextBlock _readerStatusText;
        private ProgressBar _readerStatusProgressBar;
        private TextBlock _readerStatusProgressText;
        private TextBlock _readerCurrentTitleText;
        private Button _readerPrevChapterButton;
        private Button _readerPrevPageButton;
        private Button _readerNextPageButton;
        private Button _readerNextChapterButton;
        private ComboBox _readerFitCombo;
        private Button _readerFullscreenButton;
        private Button _readerMangaDomainSortDateButton;
        private Button _readerMangaDomainSortNameButton;
        private Button _readerMangaBookSortDateButton;
        private Button _readerMangaBookSortNameButton;
        private Border _readerSidebarBorder;
        private Grid _readerRootGrid;
#pragma warning restore 0649,0169
        private bool _isReaderFullscreen = false;
        private Button _readerOtherFolderButton;
        private Button _readerRootFolderButton;
        private Button _readerWatchWithButton;
        private Button _readerChapterMissingButton;
        private Button _readerCopyMissingButton;
        private Button _readerCopyAllMissingButton;
        private Button _readerGoogleAllMissingButton;
        private string _readerLibraryRootOverride;
        private bool _forceReaderRenderOnNextPageOpen;
        private string _lastRenderedReaderChapterPath;
        private ReaderFitMode _lastRenderedReaderFitMode = ReaderFitMode.FitWidth;
        private bool _readerUsesFastStone = true;
        private ReaderWatchExternalApp _readerWatchExternalApp = ReaderWatchExternalApp.FastStone;
        private bool _readerWatchAppSelected;
        private bool _readerHasUserClickedInWatch;
        private bool _readerAutoRefreshInProgress;
        private bool _readerSuppressAutoLaunch;
        private DateTime _lastReaderAutoRefreshUtc = DateTime.MinValue;
        private DispatcherTimer _readerAutoRefreshTimer;
        private FileSystemWatcher _readerLibraryWatcher;
        private FileSystemWatcher _readerNovelLibraryWatcher;
        private DispatcherTimer _readerLibraryWatcherDebounceTimer;
        private DispatcherTimer _readerNovelLibraryWatcherDebounceTimer;
        private bool _readerLibraryRefreshInProgress;
        private bool _readerNovelLibraryRefreshInProgress;
        private string _readerWatcherRoot;
        private string _readerNovelWatcherRoot;
        private List<ReaderNovelBookItem> _readerNovelLibrary = new List<ReaderNovelBookItem>();
        private List<ReaderNovelDomainItem> _readerNovelDomains = new List<ReaderNovelDomainItem>();
        private ReaderNovelDomainItem _currentReaderNovelDomain;
        private ReaderNovelBookItem _currentReaderNovelBook;
        private ReaderNovelChapterItem _currentReaderNovelChapter;
        private ReaderMarkdownItem _currentReaderNovelFile;
        private ListBox _readerNovelDomainList;
        private ListBox _readerNovelBookList;
        private ListBox _readerNovelChapterList;
        private ListBox _readerNovelFileList;
        private TextBlock _readerNovelSummaryText;
        private TextBlock _readerNovelStatusText;
        private ProgressBar _readerNovelStatusProgressBar;
        private TextBlock _readerNovelStatusProgressText;
        private TextBlock _readerNovelCurrentTitleText;
        private Button _readerNovelOtherFolderButton;
        private Button _readerNovelRootFolderButton;
        private Button _readerNovelInstallMdReaderButton;
        private Button _readerNovelDomainSortDateButton;
        private Button _readerNovelDomainSortNameButton;
        private Button _readerNovelBookSortDateButton;
        private Button _readerNovelBookSortNameButton;
        private string _readerNovelLibraryRootOverride;
        private TextBox _readerNovelPreviewTextBox;
        private sealed class ReaderChapterAnalysis
        {
            public int IntegerCount { get; set; }

            public int DecimalCount { get; set; }

            public int UnknownCount { get; set; }

            public List<string> MissingRanges { get; } = new List<string>();
        }

        private sealed class ReaderDownloadWatchState
        {
            private readonly Dictionary<string, string> _stateByBook = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public void Add(string bookName, string stateText)
            {
                string key = NormalizeReaderCompletionKey(bookName);
                string value = NormalizeReaderDownloadStateText(stateText);

                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                _stateByBook[key] = value;
            }

            public string Get(string bookName)
            {
                string key = NormalizeReaderCompletionKey(bookName);
                if (string.IsNullOrWhiteSpace(key))
                {
                    return null;
                }

                return _stateByBook.TryGetValue(key, out string value) ? value : null;
            }
        }

        private FrameworkElement CreateWatchSection()
        {
#if DEBUG
            RunReaderChapterAnalysisSelfCheck();
            RunReaderDownloadWatchStateSelfCheck();
#endif
            _readerRootGrid = new Grid();

            var watchTabs = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };

            watchTabs.Items.Add(new TabItem
            {
                Header = "Watch manga",
                Content = CreateWatchMangaTabContent()
            });

            watchTabs.Items.Add(new TabItem
            {
                Header = "Watch novel",
                Content = CreateWatchNovelTabContent()
            });

            watchTabs.Items.Add(new TabItem
            {
                Header = "Convert",
                Content = CreateWatchConvertTabContent()
            });

            ApplyUiTextMappingsRecursive(watchTabs, _isVietnameseUi);

            _readerRootGrid.Children.Add(watchTabs);

            UpdateReaderStatus(_isVietnameseUi
                ? "Bấm Refresh library để quét root manga."
                : "Use Refresh library to scan manga root.");
            UpdateReaderNovelStatus(_isVietnameseUi
                ? "Bấm Refresh library để quét root novel."
                : "Use Refresh library to scan novel root.");
            UpdateReaderNavigationState();
            EnsureReaderAutoRefreshTimer();

            return _readerRootGrid;
        }

        private FrameworkElement CreateWatchMangaTabContent()
        {
            var mainCard = CreateWatchMainCard();
            var mainGrid = CreateWatchContentGrid();
            mainCard.Child = mainGrid;

            var watchToolbar = CreateWatchToolbar(
                () => RefreshReaderLibraryIfNeeded(forceRefresh: true),
                out _readerOtherFolderButton,
                OpenOtherReaderFolder_Click,
                OpenRootReaderFolder_Click,
                out _readerRootFolderButton,
                out _readerCurrentTitleText);

            _readerWatchWithButton = CreateReaderMiniButton("OPEN PICTURE WITH", ReaderWatchWith_Click, 160);
            _readerWatchWithButton.Style = TryFindResource("CompactPinkButton") as Style;
            _readerWatchWithButton.Height = 24;
            _readerWatchWithButton.FontSize = 10.5;
            _readerWatchWithButton.FontWeight = FontWeights.Bold;
            Grid.SetColumn(_readerWatchWithButton, 3);
            _readerWatchWithButton.HorizontalAlignment = HorizontalAlignment.Left;
            _readerWatchWithButton.Margin = new Thickness(0, 0, 6, 4);
            watchToolbar.Children.Add(_readerWatchWithButton);
            UpdateReaderWatchWithButtonLabel();


            _readerSummaryText = CreateWatchSummaryText();
            _readerDomainList = CreateWatchListBox();
            _readerMangaList = CreateWatchListBox();
            _readerMangaList.ItemContainerStyle = CreateReaderWatchItemContainerStyle(nameof(ReaderMangaItem.DisplayForeground));
            _readerChapterList = CreateWatchChapterListBox();
            _readerChapterStatsText = CreateWatchChapterStatsText();
            _readerFileList = CreateWatchListBox();

            _readerDomainList.SelectionChanged += ReaderDomainList_SelectionChanged;
            _readerMangaList.SelectionChanged += ReaderMangaList_SelectionChanged;
            _readerChapterList.SelectionChanged += ReaderChapterList_SelectionChanged;
            _readerFileList.SelectionChanged += ReaderFileList_SelectionChanged;
            _readerFileList.MouseDoubleClick += ReaderFileList_MouseDoubleClick;

            _readerMangaDomainSortDateButton = CreateWatchSortButton("DATE", ReaderMangaDomainSortDate_Click);
            _readerMangaDomainSortNameButton = CreateWatchSortButton("NAME", ReaderMangaDomainSortName_Click);
            _readerMangaBookSortDateButton = CreateWatchSortButton("DATE", ReaderMangaBookSortDate_Click);
            _readerMangaBookSortNameButton = CreateWatchSortButton("NAME", ReaderMangaBookSortName_Click);
            RefreshReaderSortButtonLabel(_readerMangaDomainSortDateButton, _readerMangaDomainSortState, ReaderWatchSortField.DateModified, "DATE");
            RefreshReaderSortButtonLabel(_readerMangaDomainSortNameButton, _readerMangaDomainSortState, ReaderWatchSortField.Name, "NAME");
            RefreshReaderSortButtonLabel(_readerMangaBookSortDateButton, _readerMangaBookSortState, ReaderWatchSortField.DateModified, "DATE");
            RefreshReaderSortButtonLabel(_readerMangaBookSortNameButton, _readerMangaBookSortState, ReaderWatchSortField.Name, "NAME");
            _readerChapterMissingButton = CreateReaderMiniButton(_isVietnameseUi ? "Chương thiếu" : "Missing chapters", ReaderChapterMissing_Click, 140);
            _readerCopyMissingButton = CreateReaderMiniButton(_isVietnameseUi ? "Sao chép chap thiếu" : "Copy missing chapter", CopyReaderMissingChapters_Click, 150);
            _readerCopyAllMissingButton = CreateReaderMiniButton(_isVietnameseUi ? "Sao chép chap thiếu của mọi truyện" : "Copy all book's missing chapter", CopyAllBooksMissingChapters_Click, 230);
            _readerGoogleAllMissingButton = CreateReaderMiniButton(_isVietnameseUi ? "Google chap thiếu của mọi truyện" : "Google all book's missing chapter", GoogleAllBooksMissingChapters_Click, 240);

            var btnConvertDomain = CreateReaderMiniButton("CONVERT XNCONVERT", BtnConvertDomainXnConvert_Click, 160);
            btnConvertDomain.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x00));
            btnConvertDomain.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x66));
            btnConvertDomain.Foreground = Brushes.Black;
            btnConvertDomain.FontWeight = FontWeights.Bold;

            var btnConvertBook = CreateReaderMiniButton("CONVERT XNCONVERT", BtnConvertBookXnConvert_Click, 160);
            btnConvertBook.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x00));
            btnConvertBook.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x66));
            btnConvertBook.Foreground = Brushes.Black;
            btnConvertBook.FontWeight = FontWeights.Bold;

            var panelBoard = CreateWatchPanelBoard(
                CreateReaderWatchPanel("Root / Domain", _readerDomainList, _readerMangaDomainSortDateButton, _readerMangaDomainSortNameButton, btnConvertDomain),
                CreateReaderWatchPanel("Domain / Book", _readerMangaList, _readerMangaBookSortDateButton, _readerMangaBookSortNameButton, btnConvertBook),
                CreateReaderWatchPanel(_isVietnameseUi ? "Truyện / Chương" : "Book / Chapter", CreateReaderChapterPanelContent(), _readerChapterMissingButton, _readerCopyMissingButton, _readerCopyAllMissingButton, _readerGoogleAllMissingButton),
                CreateReaderWatchPanel(_isVietnameseUi ? "Chapter / Ảnh" : "Chapter / Image", _readerFileList));

            _readerFullscreenButton = CreateReaderMiniButton("Open viewer", ReaderFullscreen_Click, 92);
            var readerStatusPanel = CreateWatchStatusPanel(out _readerStatusText, out _readerStatusProgressBar, out _readerStatusProgressText);

            Grid.SetRow(watchToolbar, 0);
            Grid.SetRow(_readerSummaryText, 1);
            Grid.SetRow(panelBoard, 2);
            Grid.SetRow(readerStatusPanel, 3);
            mainGrid.Children.Add(watchToolbar);
            mainGrid.Children.Add(_readerSummaryText);
            mainGrid.Children.Add(panelBoard);
            mainGrid.Children.Add(readerStatusPanel);
            return mainCard;
        }

        private FrameworkElement CreateWatchNovelTabContent()
        {
            var mainCard = CreateWatchMainCard();
            var mainGrid = CreateWatchContentGrid();
            mainCard.Child = mainGrid;

            var watchToolbar = CreateWatchToolbar(
                () => RefreshReaderNovelLibraryIfNeeded(forceRefresh: true),
                out _readerNovelOtherFolderButton,
                OpenOtherReaderNovelFolder_Click,
                OpenRootReaderNovelFolder_Click,
                out _readerNovelRootFolderButton,
                out _readerNovelCurrentTitleText);

            _readerNovelInstallMdReaderButton = CreateReaderMiniButton("CÀI MD READER", InstallMdReader_Click, 134);
            Grid.SetColumn(_readerNovelInstallMdReaderButton, 3);
            _readerNovelInstallMdReaderButton.HorizontalAlignment = HorizontalAlignment.Left;
            _readerNovelInstallMdReaderButton.Margin = new Thickness(0, 0, 6, 4);
            watchToolbar.Children.Add(_readerNovelInstallMdReaderButton);

            _readerNovelSummaryText = CreateWatchSummaryText();
            _readerNovelDomainList = CreateWatchListBox();
            _readerNovelBookList = CreateWatchListBox();
            _readerNovelBookList.ItemContainerStyle = CreateReaderWatchItemContainerStyle(nameof(ReaderNovelBookItem.DisplayForeground));
            _readerNovelChapterList = CreateWatchListBox();
            _readerNovelFileList = CreateWatchListBox();
            _readerNovelPreviewTextBox = CreateWatchPreviewTextBox();

            _readerNovelDomainList.SelectionChanged += ReaderNovelDomainList_SelectionChanged;
            _readerNovelBookList.SelectionChanged += ReaderNovelBookList_SelectionChanged;
            _readerNovelChapterList.SelectionChanged += ReaderNovelChapterList_SelectionChanged;
            _readerNovelFileList.SelectionChanged += ReaderNovelFileList_SelectionChanged;
            _readerNovelFileList.MouseDoubleClick += ReaderNovelFileList_MouseDoubleClick;

            _readerNovelDomainSortDateButton = CreateWatchSortButton("DATE", ReaderNovelDomainSortDate_Click);
            _readerNovelDomainSortNameButton = CreateWatchSortButton("NAME", ReaderNovelDomainSortName_Click);
            _readerNovelBookSortDateButton = CreateWatchSortButton("DATE", ReaderNovelBookSortDate_Click);
            _readerNovelBookSortNameButton = CreateWatchSortButton("NAME", ReaderNovelBookSortName_Click);
            RefreshReaderSortButtonLabel(_readerNovelDomainSortDateButton, _readerNovelDomainSortState, ReaderWatchSortField.DateModified, "DATE");
            RefreshReaderSortButtonLabel(_readerNovelDomainSortNameButton, _readerNovelDomainSortState, ReaderWatchSortField.Name, "NAME");
            RefreshReaderSortButtonLabel(_readerNovelBookSortDateButton, _readerNovelBookSortState, ReaderWatchSortField.DateModified, "DATE");
            RefreshReaderSortButtonLabel(_readerNovelBookSortNameButton, _readerNovelBookSortState, ReaderWatchSortField.Name, "NAME");

            var panelBoard = CreateWatchPanelBoard(
                CreateReaderWatchPanel("Root / Domain", _readerNovelDomainList, _readerNovelDomainSortDateButton, _readerNovelDomainSortNameButton),
                CreateReaderWatchPanel("Domain / Book", _readerNovelBookList, _readerNovelBookSortDateButton, _readerNovelBookSortNameButton),
                CreateReaderWatchPanel("Book / Chapter", _readerNovelChapterList),
                CreateReaderWatchPanel("Chapter / MD", CreateWatchNovelPreviewPanel()));

            var readerNovelStatusPanel = CreateWatchStatusPanel(out _readerNovelStatusText, out _readerNovelStatusProgressBar, out _readerNovelStatusProgressText);

            Grid.SetRow(watchToolbar, 0);
            Grid.SetRow(_readerNovelSummaryText, 1);
            Grid.SetRow(panelBoard, 2);
            Grid.SetRow(readerNovelStatusPanel, 3);
            mainGrid.Children.Add(watchToolbar);
            mainGrid.Children.Add(_readerNovelSummaryText);
            mainGrid.Children.Add(panelBoard);
            mainGrid.Children.Add(readerNovelStatusPanel);
            return mainCard;
        }

        private Border CreateWatchMainCard()
        {
            return new Border
            {
                Background = (Brush)TryFindResource("CyberpunkCardBrush") ?? new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
        }

        private Grid CreateWatchContentGrid()
        {
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            return mainGrid;
        }

        private Grid CreateWatchToolbar(Action refreshAction, out Button otherFolderButton, RoutedEventHandler otherFolderClick, RoutedEventHandler rootFolderClick, out Button rootFolderButton, out TextBlock titleText)
        {
            var watchToolbar = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            watchToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            watchToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            watchToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            watchToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            watchToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            watchToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            watchToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var refreshLibraryButton = CreateReaderMiniButton("Refresh library", (sender, args) => refreshAction(), 118);
            Grid.SetColumn(refreshLibraryButton, 0);
            watchToolbar.Children.Add(refreshLibraryButton);

            otherFolderButton = CreateReaderMiniButton("Load other folder", otherFolderClick, 132);
            Grid.SetColumn(otherFolderButton, 1);
            watchToolbar.Children.Add(otherFolderButton);

            rootFolderButton = CreateReaderMiniButton(_isVietnameseUi ? "Mở thư mục gốc" : "Load root folder", rootFolderClick, 132);
            rootFolderButton.FontSize = 11.5;
            rootFolderButton.FontWeight = FontWeights.Bold;
            rootFolderButton.Foreground = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow;
            Grid.SetColumn(rootFolderButton, 2);
            watchToolbar.Children.Add(rootFolderButton);

            titleText = new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow,
                FontSize = 12.5,
                FontWeight = FontWeights.ExtraBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = System.Windows.TextAlignment.Left,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 0, 0, 4)
            };
            Grid.SetColumn(titleText, 6);
            watchToolbar.Children.Add(titleText);
            return watchToolbar;
        }

        private Button CreateWatchSortButton(string label, RoutedEventHandler clickHandler)
        {
            var button = CreateReaderMiniButton(label, clickHandler, 74);
            button.Margin = new Thickness(4, 0, 0, 4);
            button.Padding = new Thickness(6, 2, 6, 2);
            button.ToolTip = label;
            return button;
        }

        private void RefreshReaderSortButtonLabel(Button button, ReaderWatchSortState state, ReaderWatchSortField field, string label)
        {
            if (button == null || state == null)
            {
                return;
            }

            bool isActive = state.Field == field;
            string direction = !isActive ? "⇅" : (state.Ascending ? "↑" : "↓");
            button.Content = label + " " + direction;
            button.Opacity = isActive ? 1d : 0.8d;
            button.ToolTip = label + (field == ReaderWatchSortField.DateModified ? " modified time" : " name");
        }

        private TextBlock CreateWatchSummaryText()
        {
            return new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkMutedTextBrush"),
                FontSize = 10,
                Margin = new Thickness(0, 10, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private TextBlock CreateWatchStatusText()
        {
            return new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkCyanBrush") ?? Brushes.Cyan,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap
            };
        }

        private FrameworkElement CreateWatchStatusPanel(out TextBlock statusText, out ProgressBar progressBar, out TextBlock progressText)
        {
            statusText = CreateWatchStatusText();
            statusText.FontSize = 14;
            progressText = new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 5, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Text = "0%",
                Visibility = Visibility.Collapsed
            };
            progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Height = 12,
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = (Brush)TryFindResource("CyberpunkCyanBrush") ?? Brushes.Cyan,
                Style = TryFindResource("ProgressBarSuccess") as Style,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };

            var progressRow = new Grid();
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(progressBar, 0);
            Grid.SetColumn(progressText, 1);
            progressRow.Children.Add(progressBar);
            progressRow.Children.Add(progressText);

            return new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    statusText,
                    progressRow
                }
            };
        }

        private ListBox CreateWatchListBox(string foregroundProperty = null)
        {
            var listBox = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0D, 0x16)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                Foreground = (Brush)TryFindResource("CyberpunkTextBrush"),
                MinHeight = 140,
                SelectionMode = SelectionMode.Extended,
                ItemTemplate = CreateReaderWatchItemTemplate()
            };
            listBox.ItemContainerStyle = CreateReaderWatchItemContainerStyle(foregroundProperty);
            ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(listBox, false);
            listBox.PreviewMouseLeftButtonDown += (sender, args) => _readerHasUserClickedInWatch = true;
            listBox.PreviewKeyDown += ReaderWatchList_PreviewKeyDown;
            return listBox;
        }

        private ListBox CreateWatchChapterListBox()
        {
            return CreateWatchListBox(nameof(ReaderChapterItem.DisplayForeground));
        }

        private Style CreateReaderWatchItemContainerStyle(string foregroundProperty = null)
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new EventSetter(FrameworkElement.ContextMenuOpeningEvent, new ContextMenuEventHandler(ReaderWatchItem_ContextMenuOpening)));
            style.Setters.Add(new EventSetter(FrameworkElement.PreviewMouseRightButtonDownEvent, new MouseButtonEventHandler(ReaderWatchItem_PreviewMouseRightButtonDown)));
            if (!string.IsNullOrWhiteSpace(foregroundProperty))
            {
                style.Setters.Add(new Setter(Control.ForegroundProperty, new Binding(foregroundProperty)
                {
                    FallbackValue = (Brush)TryFindResource("CyberpunkTextBrush") ?? Brushes.White,
                    TargetNullValue = (Brush)TryFindResource("CyberpunkTextBrush") ?? Brushes.White
                }));
            }
            return style;
        }

        private DataTemplate CreateReaderWatchItemTemplate()
        {
            var template = new DataTemplate();
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var checkBox = new FrameworkElementFactory(typeof(CheckBox));
            checkBox.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center);
            checkBox.SetValue(CheckBox.MarginProperty, new Thickness(0, 0, 8, 0));
            checkBox.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(IReaderWatchCheckable.IsChecked))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetBinding(TextBlock.TextProperty, new Binding("DisplayLabel"));

            panel.AppendChild(checkBox);
            panel.AppendChild(text);
            template.VisualTree = panel;
            return template;
        }

        private ContextMenu CreateReaderWatchItemContextMenu(object dataContext)
        {
            var menu = new ContextMenu();
            menu.Items.Add(CreateReaderWatchMenuItem(_isVietnameseUi ? "Chọn" : "Check", ReaderWatchMenuCheckSelected_Click));
            menu.Items.Add(CreateReaderWatchMenuItem(_isVietnameseUi ? "Bỏ chọn" : "Uncheck", ReaderWatchMenuUncheckSelected_Click));
            menu.Items.Add(CreateReaderWatchMenuItem(_isVietnameseUi ? "Đảo chọn" : "Toggle", ReaderWatchMenuToggleSelected_Click));
            menu.Items.Add(CreateReaderWatchMenuItem(_isVietnameseUi ? "Chọn tất cả" : "Check all", ReaderWatchMenuCheckAll_Click));
            menu.Items.Add(CreateReaderWatchMenuItem(_isVietnameseUi ? "Bỏ chọn tất cả" : "Uncheck all", ReaderWatchMenuUncheckAll_Click));
            menu.Items.Add(CreateReaderWatchMenuItem(_isVietnameseUi ? "Copy nhãn" : "Copy labels", ReaderWatchMenuCopySelected_Click));
            menu.Items.Add(CreateReaderWatchMenuItem(_isVietnameseUi ? "Xóa" : "Delete", ReaderWatchMenuDeleteSelected_Click));
            menu.Items.Add(CreateReaderWatchMenuItem(_isVietnameseUi ? "Xóa tất cả" : "Delete all", ReaderWatchMenuDeleteAll_Click));

            return menu;
        }

        private MenuItem CreateReaderWatchMenuItem(string text, RoutedEventHandler clickHandler)
        {
            var item = new MenuItem { Header = text };
            item.Click += clickHandler;
            return item;
        }

        private void ReaderWatchItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is ListBoxItem item) || !(ItemsControl.ItemsControlFromItemContainer(item) is ListBox listBox))
            {
                return;
            }

            item.IsSelected = true;
            listBox.Focus();
        }

        private void ReaderWatchItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (!(sender is ListBoxItem item) || !(item.DataContext is IReaderWatchCheckable))
            {
                return;
            }

            item.ContextMenu = CreateReaderWatchItemContextMenu(item.DataContext);
            item.ContextMenu.PlacementTarget = item;
        }

        private void ReaderWatchList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is ListBox listBox) || listBox.SelectedItems.Count == 0)
            {
                return;
            }

            if (e.Key == Key.Space)
            {
                ToggleReaderWatchSelection(listBox);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                CopyReaderWatchSelection(listBox);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                listBox.SelectAll();
                e.Handled = true;
            }
        }

        private IEnumerable<IReaderWatchCheckable> GetReaderWatchSelectedItems(ListBox listBox)
        {
            if (listBox == null)
            {
                return Enumerable.Empty<IReaderWatchCheckable>();
            }

            return listBox.SelectedItems.Cast<object>().OfType<IReaderWatchCheckable>();
        }

        private void ToggleReaderWatchSelection(ListBox listBox)
        {
            var items = GetReaderWatchSelectedItems(listBox).ToList();
            if (items.Count == 0)
            {
                return;
            }

            bool target = !items[0].IsChecked;
            foreach (var item in items)
            {
                item.IsChecked = target;
            }
        }

        private void SetReaderWatchSelection(ListBox listBox, bool value)
        {
            foreach (var item in GetReaderWatchSelectedItems(listBox))
            {
                item.IsChecked = value;
            }
        }

        private void SetReaderWatchAllItemsChecked(ListBox listBox, bool value)
        {
            if (listBox == null)
            {
                return;
            }

            foreach (var item in listBox.Items.Cast<object>().OfType<IReaderWatchCheckable>())
            {
                item.IsChecked = value;
            }
        }

        private void CopyReaderWatchSelection(ListBox listBox)
        {
            var labels = listBox.SelectedItems.Cast<object>()
                .Select(item => item?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            if (labels.Count == 0)
            {
                return;
            }

            Clipboard.SetText(string.Join("\r\n", labels));
        }

        private void ReaderWatchMenuCheckSelected_Click(object sender, RoutedEventArgs e)
        {
            ApplyReaderWatchMenuAction(sender, item => item.IsChecked = true);
        }

        private void ReaderWatchMenuUncheckSelected_Click(object sender, RoutedEventArgs e)
        {
            ApplyReaderWatchMenuAction(sender, item => item.IsChecked = false);
        }

        private void ReaderWatchMenuToggleSelected_Click(object sender, RoutedEventArgs e)
        {
            ApplyReaderWatchMenuAction(sender, item => item.IsChecked = !item.IsChecked);
        }

        private void ReaderWatchMenuCheckAll_Click(object sender, RoutedEventArgs e)
        {
            ApplyReaderWatchMenuAll(sender, true);
        }

        private void ReaderWatchMenuUncheckAll_Click(object sender, RoutedEventArgs e)
        {
            ApplyReaderWatchMenuAll(sender, false);
        }

        private void ReaderWatchMenuCopySelected_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetReaderWatchListBox(sender, out ListBox listBox))
            {
                return;
            }

            CopyReaderWatchSelection(listBox);
        }

        private async void ReaderWatchMenuDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            await DeleteReaderWatchItemsAsync(sender, deleteAll: false);
        }

        private async void ReaderWatchMenuDeleteAll_Click(object sender, RoutedEventArgs e)
        {
            await DeleteReaderWatchItemsAsync(sender, deleteAll: true);
        }

        private async Task DeleteReaderWatchItemsAsync(object sender, bool deleteAll)
        {
            if (!TryGetReaderWatchListBox(sender, out ListBox listBox))
            {
                return;
            }

            List<IReaderWatchCheckable> items = deleteAll
                ? listBox.Items.Cast<object>().OfType<IReaderWatchCheckable>().ToList()
                : GetReaderWatchSelectedItems(listBox).ToList();

            List<string> targets = GetReaderWatchDeletionTargets(items);
            if (targets.Count == 0)
            {
                return;
            }

            string prompt = deleteAll
                ? (_isVietnameseUi ? "Xóa toàn bộ mục trong panel này?" : "Delete all items in this panel?")
                : (_isVietnameseUi ? "Xóa mục đang chọn?" : "Delete selected item(s)?");

            if (!ShowConfirm(prompt))
            {
                return;
            }

            UpdateReaderStatus(_isVietnameseUi ? "Đang xóa dữ liệu..." : "Deleting items...");
            this.Cursor = System.Windows.Input.Cursors.Wait;

            int deletedCount = 0;
            int skippedCount = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (string target in targets.OrderByDescending(path => path.Length))
                    {
                        if (TryDeleteReaderWatchPath(target))
                        {
                            deletedCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }
                });
            }
            finally
            {
                this.Cursor = System.Windows.Input.Cursors.Arrow;
            }

            UpdateReaderStatus(_isVietnameseUi
                ? $"Đã xóa {deletedCount} mục."
                : $"Deleted {deletedCount} item(s).");

            // ponytail: refresh whole manga tree after delete; simple, safe, enough for current library size.
            await RefreshReaderLibraryAsync(forceRefresh: true);

            if (skippedCount > 0)
            {
                Log($"[Reader] Skipped {skippedCount} delete target(s).");
            }
        }

        private void ApplyReaderWatchMenuAction(object sender, Action<IReaderWatchCheckable> action)
        {
            if (!TryGetReaderWatchListBox(sender, out ListBox listBox))
            {
                return;
            }

            foreach (var item in GetReaderWatchSelectedItems(listBox))
            {
                action(item);
            }
        }

        private void ApplyReaderWatchMenuAll(object sender, bool value)
        {
            if (!TryGetReaderWatchListBox(sender, out ListBox listBox))
            {
                return;
            }

            SetReaderWatchAllItemsChecked(listBox, value);
        }

        private List<string> GetReaderWatchDeletionTargets(IEnumerable<IReaderWatchCheckable> items)
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (items == null)
            {
                return targets.ToList();
            }

            foreach (IReaderWatchCheckable item in items)
            {
                string target = GetReaderWatchDeletionTarget(item);
                if (!string.IsNullOrWhiteSpace(target))
                {
                    targets.Add(target);
                }
            }

            return targets.ToList();
        }

        private static string GetReaderWatchDeletionTarget(IReaderWatchCheckable item)
        {
            if (item == null)
            {
                return null;
            }

            if (item is ReaderPageItem page)
            {
                return page.FilePath;
            }

            if (item is ReaderMarkdownItem file)
            {
                return file.FilePath;
            }

            if (item is ReaderChapterItem chapter)
            {
                return chapter.FolderPath;
            }

            if (item is ReaderMangaItem manga)
            {
                return manga.FolderPath;
            }

            if (item is ReaderDomainItem domain)
            {
                return domain.FolderPath;
            }

            if (item is ReaderNovelChapterItem novelChapter)
            {
                return novelChapter.FolderPath;
            }

            if (item is ReaderNovelBookItem novelBook)
            {
                return novelBook.FolderPath;
            }

            if (item is ReaderNovelDomainItem novelDomain)
            {
                return novelDomain.FolderPath;
            }

            return null;
        }

        private bool TryDeleteReaderWatchPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }

                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"[Reader] Delete skip '{path}': {ex.Message}");
            }

            return false;
        }

        private bool TryGetReaderWatchListBox(object sender, out ListBox listBox)
        {
            listBox = null;
            if (!(sender is MenuItem menuItem) || !(menuItem.Parent is ContextMenu contextMenu) || !(contextMenu.PlacementTarget is ListBoxItem container) || !(container.DataContext is IReaderWatchCheckable))
            {
                return false;
            }

            listBox = ItemsControl.ItemsControlFromItemContainer(container) as ListBox;
            if (listBox == null)
            {
                return false;
            }

            container.IsSelected = true;
            listBox.Focus();
            return true;
        }

        private TextBlock CreateWatchChapterStatsText()
        {
            return new TextBlock
            {
                Foreground = (Brush)TryFindResource("CyberpunkMutedTextBrush"),
                FontSize = 9.5,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private UIElement CreateReaderChapterPanelContent()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(_readerChapterStatsText, 0);
            grid.Children.Add(_readerChapterStatsText);

            _readerChapterContentHost = new ContentControl();
            Grid.SetRow(_readerChapterContentHost, 1);
            grid.Children.Add(_readerChapterContentHost);

            _readerChapterContentHost.Content = _readerChapterList;

            _readerChapterIssueGrid = CreateWatchChapterIssueGrid();
            _readerChapterIssuePanel = new Border
            {
                Background = (Brush)TryFindResource("CyberpunkCardBrush") ?? new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Child = _readerChapterIssueGrid
            };

            return grid;
        }

        private DataGrid CreateWatchChapterIssueGrid()
        {
            var grid = new DataGrid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0D, 0x16)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                Foreground = (Brush)TryFindResource("CyberpunkTextBrush"),
                Style = TryFindResource("CyberpunkDataGrid") as Style,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeRows = false,
                CanUserReorderColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                RowHeaderWidth = 0
            };

            grid.ColumnHeaderStyle = TryFindResource("CyberpunkDataGridColumnHeader") as Style;
            grid.RowStyle = TryFindResource("CyberpunkDataGridRow") as Style;
            grid.CellStyle = TryFindResource("CyberpunkDataGridCell") as Style;

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = _isVietnameseUi ? "Truyện" : "Book",
                Binding = new Binding(nameof(ReaderChapterIssueItem.BookName))
            });
            grid.Columns.Add(CreateIssueButtonColumn(_isVietnameseUi ? "Chương" : "Chapter", nameof(ReaderChapterIssueItem.ChapterLabel), nameof(ReaderChapterIssueItem.ChapterTarget), ReaderChapterIssueChapter_Click));
            grid.Columns.Add(CreateIssueButtonColumn(_isVietnameseUi ? "Chương thiếu" : "Missing chapter", nameof(ReaderChapterIssueItem.MissingChapterLabel), nameof(ReaderChapterIssueItem.MissingTarget), ReaderChapterIssueMissing_Click));
            grid.Columns.Add(CreateIssueButtonColumn(_isVietnameseUi ? "Chap thập phân" : "Decimal chapter", nameof(ReaderChapterIssueItem.DecimalChapterLabel), nameof(ReaderChapterIssueItem.DecimalTarget), ReaderChapterIssueDecimal_Click));

            return grid;
        }

        private DataGridTemplateColumn CreateIssueButtonColumn(string header, string labelPath, string targetPath, RoutedEventHandler clickHandler)
        {
            var template = new DataTemplate();
            var button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(Button.PaddingProperty, new Thickness(4, 1, 4, 1));
            button.SetValue(Button.MarginProperty, new Thickness(0, 1, 0, 1));
            button.SetValue(Button.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            button.SetValue(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
            button.SetValue(Button.BackgroundProperty, Brushes.Transparent);
            button.SetValue(Button.BorderBrushProperty, Brushes.Transparent);
            button.SetValue(Button.ForegroundProperty, Brushes.Cyan);
            button.SetValue(Button.CursorProperty, Cursors.Hand);
            button.SetBinding(ContentControl.ContentProperty, new Binding(labelPath));
            button.SetBinding(Button.TagProperty, new Binding(targetPath));
            button.AddHandler(Button.ClickEvent, clickHandler);
            template.VisualTree = button;

            return new DataGridTemplateColumn
            {
                Header = header,
                CellTemplate = template
            };
        }

        private void RefreshReaderChapterIssueGrid(ReaderMangaItem manga)
        {
            if (_readerChapterIssueGrid == null)
            {
                return;
            }

            _readerChapterIssueGrid.ItemsSource = BuildReaderChapterIssueRows(manga);
        }

        private void ShowReaderChapterIssues()
        {
            RefreshReaderChapterIssueGrid(_currentReaderManga);
            if (_readerChapterContentHost != null && _readerChapterIssuePanel != null)
            {
                _readerChapterContentHost.Content = _readerChapterIssuePanel;
            }
        }

        private void ShowReaderChapterList()
        {
            if (_readerChapterContentHost != null)
            {
                _readerChapterContentHost.Content = _readerChapterList;
            }
        }

        private void GoToReaderChapterIssueTarget(ReaderChapterItem target)
        {
            if (target == null)
            {
                return;
            }

            SelectReaderChapter(target);
            ShowReaderChapterList();
        }

        private List<ReaderChapterIssueItem> BuildReaderChapterIssueRows(ReaderMangaItem manga)
        {
            var rows = new List<ReaderChapterIssueItem>();
            if (manga == null || manga.Chapters == null || manga.Chapters.Count == 0)
            {
                return rows;
            }

            var parsed = manga.Chapters
                .Select(chapter =>
                {
                    bool ok = TryParseReaderChapterNumber(chapter?.Name, out double number, out bool isDecimal);
                    return new
                    {
                        Chapter = chapter,
                        Parsed = ok,
                        Number = number,
                        IsDecimal = isDecimal,
                        IntegerNumber = ok && !isDecimal ? (int)Math.Round(number) : (int?)null
                    };
                })
                .Where(item => item.Chapter != null)
                .ToList();

            foreach (var item in parsed.Where(item => item.Parsed && item.IsDecimal))
            {
                rows.Add(new ReaderChapterIssueItem
                {
                    BookName = manga.Name,
                    ChapterLabel = item.Chapter.Name,
                    DecimalChapterLabel = item.Chapter.Name,
                    DecimalTarget = item.Chapter,
                    ChapterTarget = item.Chapter
                });
            }

            var integerItems = parsed
                .Where(item => item.Parsed && !item.IsDecimal && item.IntegerNumber.HasValue)
                .GroupBy(item => item.IntegerNumber.Value)
                .OrderBy(group => group.Key)
                .ToList();

            for (int i = 1; i < integerItems.Count; i++)
            {
                int previous = integerItems[i - 1].Key;
                int current = integerItems[i].Key;
                if (current - previous <= 1)
                {
                    continue;
                }

                ReaderChapterItem previousChapter = integerItems[i - 1].OrderBy(item => item.Chapter.Name, _readerSortComparer).First().Chapter;
                string missingLabel = current - previous == 2
                    ? FormatReaderChapterNumber(previous + 1)
                    : FormatReaderChapterNumber(previous + 1) + "-" + FormatReaderChapterNumber(current - 1);

                rows.Add(new ReaderChapterIssueItem
                {
                    BookName = manga.Name,
                    ChapterLabel = previousChapter?.Name ?? FormatReaderChapterNumber(previous),
                    MissingChapterLabel = missingLabel,
                    ChapterTarget = previousChapter,
                    MissingTarget = previousChapter
                });
            }

            return rows
                .OrderBy(row => row.ChapterTarget?.ParsedChapterNumber ?? double.MaxValue)
                .ThenBy(row => row.DecimalTarget == null ? 0 : 1)
                .ToList();
        }

        private static string FormatReaderChapterNumber(int value)
        {
            return value.ToString("00", CultureInfo.InvariantCulture);
        }

        private void ReaderChapterMissing_Click(object sender, RoutedEventArgs e)
        {
            if (_readerChapterContentHost == null || _readerChapterIssuePanel == null)
            {
                return;
            }

            if (ReferenceEquals(_readerChapterContentHost.Content, _readerChapterIssuePanel))
            {
                ShowReaderChapterList();
                return;
            }

            ShowReaderChapterIssues();
        }

        private void CopyReaderMissingChapters_Click(object sender, RoutedEventArgs e)
        {
            if (_currentReaderManga == null || _currentReaderManga.Chapters == null || _currentReaderManga.Chapters.Count == 0)
            {
                UpdateReaderStatus(_isVietnameseUi ? "Chưa có chap để copy." : "No chapters to copy.");
                return;
            }

            string missingText = string.Join("; ", AnalyzeReaderChapterNumbers(_currentReaderManga.Chapters).MissingRanges);
            if (string.IsNullOrWhiteSpace(missingText))
            {
                UpdateReaderStatus(_isVietnameseUi ? "Không có chap thiếu." : "No missing chapters.");
                return;
            }

            string formattedText = $"{_currentReaderManga.Name}: {missingText}";
            Clipboard.SetText(formattedText);
            UpdateReaderStatus(_isVietnameseUi ? $"Đã copy chap thiếu: {formattedText}" : $"Copied missing chapters: {formattedText}");
        }

        private void CopyAllBooksMissingChapters_Click(object sender, RoutedEventArgs e)
        {
            if (_readerLibrary == null || _readerLibrary.Count == 0)
            {
                UpdateReaderStatus(_isVietnameseUi ? "Thư viện chưa có truyện để quét." : "No books in library to scan.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (var book in _readerLibrary)
            {
                if (book.Chapters == null || book.Chapters.Count == 0)
                {
                    continue;
                }

                var analysis = AnalyzeReaderChapterNumbers(book.Chapters);
                if (analysis.MissingRanges.Count > 0)
                {
                    string missingText = string.Join("; ", analysis.MissingRanges);
                    sb.AppendLine($"{book.Name}: {missingText}");
                }
            }

            string result = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(result))
            {
                UpdateReaderStatus(_isVietnameseUi ? "Không có truyện nào bị thiếu chap." : "No books have missing chapters.");
                return;
            }

            Clipboard.SetText(result);
            UpdateReaderStatus(_isVietnameseUi ? "Đã copy toàn bộ chap thiếu của mọi truyện." : "Copied all missing chapters from all books.");
        }

        private void GoogleAllBooksMissingChapters_Click(object sender, RoutedEventArgs e)
        {
            if (_readerLibrary == null || _readerLibrary.Count == 0)
            {
                UpdateReaderStatus(_isVietnameseUi ? "Thư viện chưa có truyện để quét." : "No books in library to scan.");
                return;
            }

            int count = 0;
            foreach (var book in _readerLibrary)
            {
                if (book.Chapters == null || book.Chapters.Count == 0)
                {
                    continue;
                }

                var analysis = AnalyzeReaderChapterNumbers(book.Chapters);
                if (analysis.MissingRanges.Count > 0)
                {
                    try
                    {
                        string query = $"{book.Name} chap 1";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "https://www.google.com/search?q=" + Uri.EscapeDataString(query),
                            UseShellExecute = true
                        });
                        count++;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to open browser: " + ex.Message);
                    }
                }
            }

            if (count == 0)
            {
                UpdateReaderStatus(_isVietnameseUi ? "Không có truyện nào bị thiếu chap để tìm kiếm." : "No books have missing chapters to search.");
            }
            else
            {
                UpdateReaderStatus(_isVietnameseUi ? $"Đã mở {count} trang tìm kiếm Google." : $"Opened {count} Google search pages.");
            }
        }

        private void ReaderChapterIssueChapter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ReaderChapterItem target)
            {
                GoToReaderChapterIssueTarget(target);
            }
        }

        private void ReaderChapterIssueMissing_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ReaderChapterItem target)
            {
                GoToReaderChapterIssueTarget(target);
            }
        }

        private void ReaderChapterIssueDecimal_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ReaderChapterItem target)
            {
                GoToReaderChapterIssueTarget(target);
            }
        }

        private TextBox CreateWatchPreviewTextBox()
        {
            return new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0D, 0x16)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                Foreground = (Brush)TryFindResource("CyberpunkTextBrush"),
                IsReadOnly = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                MinHeight = 120
            };
        }

        private UIElement CreateWatchNovelPreviewPanel()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(110) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid.SetRow(_readerNovelFileList, 0);
            grid.Children.Add(_readerNovelFileList);

            Grid.SetRow(_readerNovelPreviewTextBox, 2);
            grid.Children.Add(_readerNovelPreviewTextBox);
            return grid;
        }

#if DEBUG
        private static void RunReaderChapterAnalysisSelfCheck()
        {
            var chapters = new List<ReaderChapterItem>
            {
                new ReaderChapterItem { Name = "chap 01" },
                new ReaderChapterItem { Name = "chap 02" },
                new ReaderChapterItem { Name = "chap 02.5" },
                new ReaderChapterItem { Name = "chap 04" }
            };

            ReaderChapterAnalysis analysis = AnalyzeReaderChapterNumbers(chapters);

            Debug.Assert(analysis.IntegerCount == 3);
            Debug.Assert(analysis.DecimalCount == 1);
            Debug.Assert(analysis.MissingRanges.Count == 1 && analysis.MissingRanges[0] == "3");
            Debug.Assert(chapters[1].HasMissingIntegerGap);
            Debug.Assert(chapters[3].HasMissingIntegerGap);
            Debug.Assert(chapters[2].IsDecimalChapter);
            Debug.Assert(new ReaderMangaItem { HasMissingIntegerGap = true }.DisplayForeground == Brushes.Cyan);
        }

        private static void RunReaderDownloadWatchStateSelfCheck()
        {
            Debug.Assert(string.Equals(SummarizeReaderDownloadWatchState(new[] { "Done" }), "completed", StringComparison.OrdinalIgnoreCase));
            Debug.Assert(string.Equals(SummarizeReaderDownloadWatchState(new[] { "Done", "Pending" }), "downloading", StringComparison.OrdinalIgnoreCase));
            Debug.Assert(string.Equals(NormalizeReaderDownloadStateText("hoan tat"), "completed", StringComparison.OrdinalIgnoreCase));
        }
#endif

        private Grid CreateWatchPanelBoard(Border rootDomainPanel, Border domainBookPanel, Border bookChapterPanel, Border chapterFilePanel)
        {
            var panelBoard = new Grid
            {
                Margin = new Thickness(0)
            };
            panelBoard.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panelBoard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panelBoard.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var bottomRow = new Grid();
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid.SetColumn(rootDomainPanel, 0);
            Grid.SetColumn(domainBookPanel, 2);
            Grid.SetColumn(bookChapterPanel, 0);
            Grid.SetColumn(chapterFilePanel, 2);

            var topVerticalSplitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ShowsPreview = true,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetColumn(topVerticalSplitter, 1);

            var bottomVerticalSplitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ShowsPreview = true,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetColumn(bottomVerticalSplitter, 1);

            topRow.Children.Add(rootDomainPanel);
            topRow.Children.Add(domainBookPanel);
            topRow.Children.Add(topVerticalSplitter);

            bottomRow.Children.Add(bookChapterPanel);
            bottomRow.Children.Add(chapterFilePanel);
            bottomRow.Children.Add(bottomVerticalSplitter);

            Grid.SetRow(topRow, 0);
            Grid.SetRow(bottomRow, 2);

            var horizontalSplitter = new GridSplitter
            {
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ShowsPreview = true,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetRow(horizontalSplitter, 1);

            panelBoard.Children.Add(topRow);
            panelBoard.Children.Add(horizontalSplitter);
            panelBoard.Children.Add(bottomRow);
            return panelBoard;
        }
        private void ScrollReaderChapterIntoView(ReaderChapterItem chapter)
        {
            if (_readerChapterList == null || chapter == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => _readerChapterList.ScrollIntoView(chapter)), DispatcherPriority.Background);
        }

        private void ScrollReaderNovelChapterIntoView(ReaderNovelChapterItem chapter)
        {
            if (_readerNovelChapterList == null || chapter == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => _readerNovelChapterList.ScrollIntoView(chapter)), DispatcherPriority.Background);
        }

        private Button CreateReaderMiniButton(string text, RoutedEventHandler clickHandler, double minWidth = 54)
        {
            var button = new Button
            {
                Content = text,
                Style = TryFindResource("CompactCyanButton") as Style,
                Margin = new Thickness(0, 0, 6, 4),
                MinWidth = minWidth
            };

            button.Click += clickHandler;
            return button;
        }

        private void EnsureReaderAutoRefreshTimer()
        {
            if (_readerAutoRefreshTimer != null)
            {
                return;
            }

            _readerAutoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(25)
            };
            _readerAutoRefreshTimer.Tick += async (sender, args) =>
            {
                if (_currentSection != AppSection.Watch ||
                    _readerAutoRefreshInProgress ||
                    _readerLibraryRefreshInProgress ||
                    _readerNovelLibraryRefreshInProgress ||
                    IsReaderRefreshBlockedByActiveDownload() ||
                    _isReaderFullscreen)
                {
                    return;
                }

                if ((DateTime.UtcNow - _lastReaderAutoRefreshUtc).TotalSeconds < 20)
                {
                    return;
                }

                _readerAutoRefreshInProgress = true;
                _readerSuppressAutoLaunch = true;
                try
                {
                    _lastReaderAutoRefreshUtc = DateTime.UtcNow;
                    await RefreshReaderLibraryAsync(forceRefresh: true);
                    await RefreshReaderNovelLibraryAsync(forceRefresh: true);
                }
                finally
                {
                    _readerSuppressAutoLaunch = false;
                    _readerAutoRefreshInProgress = false;
                }
            };
        }

        private bool IsReaderRefreshBlockedByActiveDownload()
        {
            return _downloadCts != null;
        }

        private void StartReaderAutoRefresh()
        {
            EnsureReaderAutoRefreshTimer();
            _readerAutoRefreshTimer?.Start();
        }

        private void StopReaderAutoRefresh()
        {
            _readerAutoRefreshTimer?.Stop();
            DisposeReaderLibraryWatcher(ref _readerLibraryWatcher, ref _readerWatcherRoot);
            DisposeReaderLibraryWatcher(ref _readerNovelLibraryWatcher, ref _readerNovelWatcherRoot);
            _readerLibraryWatcherDebounceTimer?.Stop();
            _readerNovelLibraryWatcherDebounceTimer?.Stop();
        }

        private void EnsureReaderLibraryWatcherTimers()
        {
            if (_readerLibraryWatcherDebounceTimer == null)
            {
                _readerLibraryWatcherDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _readerLibraryWatcherDebounceTimer.Tick += async (sender, args) =>
                {
                    _readerLibraryWatcherDebounceTimer.Stop();
                    if (_currentSection == AppSection.Watch &&
                        !_isReaderFullscreen &&
                        !_readerLibraryRefreshInProgress &&
                        !IsReaderRefreshBlockedByActiveDownload())
                    {
                        await RefreshReaderLibraryAsync(forceRefresh: true);
                    }
                };
            }

            if (_readerNovelLibraryWatcherDebounceTimer == null)
            {
                _readerNovelLibraryWatcherDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _readerNovelLibraryWatcherDebounceTimer.Tick += async (sender, args) =>
                {
                    _readerNovelLibraryWatcherDebounceTimer.Stop();
                    if (_currentSection == AppSection.Watch &&
                        !_isReaderFullscreen &&
                        !_readerNovelLibraryRefreshInProgress &&
                        !IsReaderRefreshBlockedByActiveDownload())
                    {
                        await RefreshReaderNovelLibraryAsync(forceRefresh: true);
                    }
                };
            }
        }

        private void EnsureReaderLibraryWatcher(string root, bool isNovel)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                if (isNovel)
                {
                    DisposeReaderLibraryWatcher(ref _readerNovelLibraryWatcher, ref _readerNovelWatcherRoot);
                }
                else
                {
                    DisposeReaderLibraryWatcher(ref _readerLibraryWatcher, ref _readerWatcherRoot);
                }
                return;
            }

            EnsureReaderLibraryWatcherTimers();

            if (isNovel)
            {
                if (string.Equals(_readerNovelWatcherRoot, root, StringComparison.OrdinalIgnoreCase) && _readerNovelLibraryWatcher != null)
                {
                    return;
                }

                DisposeReaderLibraryWatcher(ref _readerNovelLibraryWatcher, ref _readerNovelWatcherRoot);
                _readerNovelLibraryWatcher = CreateReaderLibraryWatcher(root, () =>
                {
                    _readerNovelLibraryWatcherDebounceTimer.Stop();
                    _readerNovelLibraryWatcherDebounceTimer.Start();
                });
                _readerNovelWatcherRoot = root;
                return;
            }

            if (string.Equals(_readerWatcherRoot, root, StringComparison.OrdinalIgnoreCase) && _readerLibraryWatcher != null)
            {
                return;
            }

            DisposeReaderLibraryWatcher(ref _readerLibraryWatcher, ref _readerWatcherRoot);
            _readerLibraryWatcher = CreateReaderLibraryWatcher(root, () =>
            {
                _readerLibraryWatcherDebounceTimer.Stop();
                _readerLibraryWatcherDebounceTimer.Start();
            });
            _readerWatcherRoot = root;
        }

        private FileSystemWatcher CreateReaderLibraryWatcher(string root, Action onChanged)
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName
            };

            FileSystemEventHandler changedHandler = (sender, args) => 
            {
                if (_readerAutoRefreshInProgress || _readerLibraryRefreshInProgress || _readerNovelLibraryRefreshInProgress)
                {
                    return;
                }

                if (IsReaderRefreshBlockedByActiveDownload())
                {
                    return;
                }

                if (args.Name != null)
                {
                    string ext = Path.GetExtension(args.Name).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".png" || ext == ".gif" || ext == ".webp" || ext == ".tmp" || ext == ".download")
                    {
                        return;
                    }
                }

                Dispatcher.BeginInvoke(onChanged);
            };

            RenamedEventHandler renamedHandler = (sender, args) => 
            {
                if (_readerAutoRefreshInProgress || _readerLibraryRefreshInProgress || _readerNovelLibraryRefreshInProgress)
                {
                    return;
                }

                if (args.Name != null)
                {
                    string ext = Path.GetExtension(args.Name).ToLowerInvariant();
                    if (ext == ".jpg" || ext == ".png" || ext == ".gif" || ext == ".webp" || ext == ".tmp" || ext == ".download")
                    {
                        return;
                    }
                }
                Dispatcher.BeginInvoke(onChanged);
            };

            watcher.Created += changedHandler;
            watcher.Deleted += changedHandler;
            watcher.Renamed += renamedHandler;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private static void DisposeReaderLibraryWatcher(ref FileSystemWatcher watcher, ref string root)
        {
            if (watcher != null)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                watcher = null;
            }

            root = null;
        }

        private string GetReaderWatchAppDisplayName(ReaderWatchExternalApp app)
        {
            return app == ReaderWatchExternalApp.Bandiview ? "Bandiview" : "FastStone Image Viewer";
        }

        private string GetReaderWatchCurrentAppDisplayName()
        {
            return GetReaderWatchAppDisplayName(_readerWatchExternalApp);
        }

        private string GetReaderWatchAppDownloadUrl(ReaderWatchExternalApp app)
        {
            return app == ReaderWatchExternalApp.Bandiview
                ? "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/accessories/Bandiview.zip"
                : "https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/accessories/FastStone.Image.Viewer.zip";
        }

        private string GetReaderWatchAppRootPath(ReaderWatchExternalApp app)
        {
            return app == ReaderWatchExternalApp.Bandiview
                ? PortablePaths.BandiviewRoot
                : PortablePaths.FastStoneRoot;
        }

        private string GetReaderWatchAppExecutablePath(ReaderWatchExternalApp app)
        {
            return app == ReaderWatchExternalApp.Bandiview
                ? PortablePaths.BandiviewExePath
                : PortablePaths.FastStoneExePath;
        }

        private void UpdateReaderWatchWithButtonLabel()
        {
            if (_readerWatchWithButton == null)
            {
                return;
            }

            _readerWatchWithButton.Content = "OPEN PICTURE WITH";
            _readerWatchWithButton.ToolTip = GetReaderWatchCurrentAppDisplayName();
        }

        private async Task EnsureReaderWatchAppReadyAsync(ReaderWatchExternalApp app)
        {
            string exePath = GetReaderWatchAppExecutablePath(app);
            if (File.Exists(exePath))
            {
                return;
            }

            string appName = GetReaderWatchAppDisplayName(app);
            string appRoot = GetReaderWatchAppRootPath(app);
            string zipFileName = app == ReaderWatchExternalApp.Bandiview ? "Bandiview.zip" : "FastStone.Image.Viewer.zip";
            string zipPath = Path.Combine(PortablePaths.PortableDataRoot, zipFileName);

            Directory.CreateDirectory(PortablePaths.PortableDataRoot);
            UpdateReaderStatus((_isVietnameseUi ? "Đang tải " : "Downloading ") + appName + "...");

            using (var response = await _httpClient.GetAsync(GetReaderWatchAppDownloadUrl(app), HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = File.Open(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output);
                }
            }

            if (Directory.Exists(appRoot))
            {
                Directory.Delete(appRoot, true);
            }

            // ponytail: zip already contains app folder; extract to .portable, not nested folder again.
            ZipFile.ExtractToDirectory(zipPath, PortablePaths.PortableDataRoot);
            File.Delete(zipPath);

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException("Viewer executable was not found after extraction.", exePath);
            }
        }

        private string ResolveFastStoneExecutablePath()
        {
            return GetReaderWatchAppExecutablePath(_readerWatchExternalApp);
        }

        private bool TryLaunchFastStone(string targetPath, out string errorMessage)
        {
            errorMessage = null;

            string fastStoneExePath = ResolveFastStoneExecutablePath();
            if (string.IsNullOrWhiteSpace(fastStoneExePath) || !File.Exists(fastStoneExePath))
            {
                errorMessage = _isVietnameseUi
                    ? $"Không tìm thấy {GetReaderWatchCurrentAppDisplayName()} trong bundle portable."
                    : $"{GetReaderWatchCurrentAppDisplayName()} was not found in the portable bundle.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetPath) || (!File.Exists(targetPath) && !Directory.Exists(targetPath)))
            {
                errorMessage = _isVietnameseUi
                    ? "Trang hoặc chapter đã chọn không còn tồn tại."
                    : "The selected page or chapter no longer exists.";
                return false;
            }

            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fastStoneExePath,
                    Arguments = "\"" + targetPath + "\"",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(fastStoneExePath)
                };

                System.Diagnostics.Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = (_isVietnameseUi ? "Không thể mở " : "Failed to open ") + GetReaderWatchCurrentAppDisplayName() + ": " + ex.Message;
                return false;
            }
        }

        private async void LaunchCurrentReaderTargetInFastStone(bool preferChapterFolder)
        {
            string targetPath = null;

            if (preferChapterFolder && _currentReaderChapter != null && Directory.Exists(_currentReaderChapter.FolderPath))
            {
                targetPath = _currentReaderChapter.FolderPath;
            }
            else if (_currentReaderPage != null && File.Exists(_currentReaderPage.FilePath))
            {
                targetPath = _currentReaderPage.FilePath;
            }
            else if (_currentReaderChapter != null && Directory.Exists(_currentReaderChapter.FolderPath))
            {
                targetPath = _currentReaderChapter.FolderPath;
            }
            else if (_currentReaderManga != null && Directory.Exists(_currentReaderManga.FolderPath))
            {
                targetPath = _currentReaderManga.FolderPath;
            }

            try
            {
                await EnsureReaderWatchAppReadyAsync(_readerWatchExternalApp);
            }
            catch (Exception ex)
            {
                UpdateReaderStatus((_isVietnameseUi ? "Không thể tải app watch: " : "Failed to download watch app: ") + ex.Message);
                return;
            }

            if (!TryLaunchFastStone(targetPath, out string errorMessage))
            {
                UpdateReaderStatus(errorMessage);
                return;
            }

            string appName = GetReaderWatchCurrentAppDisplayName();
            string targetLabel = preferChapterFolder
                ? (_currentReaderChapter?.Name ?? _currentReaderManga?.Name ?? appName)
                : (_currentReaderPage?.DisplayLabel ?? _currentReaderChapter?.Name ?? appName);
            UpdateReaderStatus((_isVietnameseUi ? "Đã mở bằng " : "Opened in ") + appName + ": " + targetLabel);
        }

        private void ReaderScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                return;
            }

            e.Handled = true;
            double nextZoom = _readerZoom + (e.Delta > 0 ? 0.12d : -0.12d);
            SetReaderZoom(nextZoom, keepCurrentPageVisible: true);
        }

        private void ReaderScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_readerScrollViewer == null || !IsReaderScrollMode())
            {
                return;
            }

            if (_readerScrollViewer.ScrollableWidth <= 0 && _readerScrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            _readerIsMousePanning = true;
            _readerMousePanStartPoint = e.GetPosition(_readerScrollViewer);
            _readerMousePanStartHorizontalOffset = _readerScrollViewer.HorizontalOffset;
            _readerMousePanStartVerticalOffset = _readerScrollViewer.VerticalOffset;
            _readerMousePanPreviousCursor = Cursor;
            Cursor = Cursors.SizeAll;
            _readerScrollViewer.CaptureMouse();
            _readerScrollViewer.Focus();
            e.Handled = true;
        }

        private void ReaderScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_readerIsMousePanning || _readerScrollViewer == null)
            {
                return;
            }

            Point currentPoint = e.GetPosition(_readerScrollViewer);
            Vector delta = currentPoint - _readerMousePanStartPoint;
            _readerSyncingFromViewport = true;
            try
            {
                _readerScrollViewer.ScrollToHorizontalOffset(ClampOffset(_readerMousePanStartHorizontalOffset - delta.X, _readerScrollViewer.ScrollableWidth));
                _readerScrollViewer.ScrollToVerticalOffset(ClampOffset(_readerMousePanStartVerticalOffset - delta.Y, _readerScrollViewer.ScrollableHeight));
            }
            finally
            {
                _readerSyncingFromViewport = false;
            }

            UpdateReaderPageFromViewport();
            e.Handled = true;
        }

        private void ReaderScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_readerIsMousePanning)
            {
                return;
            }

            EndReaderMousePan();
            e.Handled = true;
        }

        private void ReaderScrollViewer_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndReaderMousePan();
        }

        private void ReaderScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_readerSyncingFromViewport || !IsReaderScrollMode() || _readerPageElements.Count == 0 || _currentReaderChapter == null)
            {
                return;
            }

            UpdateReaderPageFromViewport();
        }

        #if false
        private void EnsureReaderReady()
        {
            if (true)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(PortablePaths.WebView2UserDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(
                    null,
                    PortablePaths.WebView2UserDataFolder,
                    new CoreWebView2EnvironmentOptions());
                await _readerWebView.EnsureCoreWebView2Async(env);
                if (_readerWebView.CoreWebView2 != null)
                {
                    _readerWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _readerWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    _readerWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    _readerWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    _readerWebView.CoreWebView2.WebMessageReceived += (sender, args) =>
                    {
                        try
                        {
                            HandleReaderWebMessage(args.TryGetWebMessageAsString());
                        }
                        catch
                        {
                        }
                    };
                }

                _readerWebViewReady = true;
                if (_currentReaderPage != null)
                {
                    RenderReaderPage();
                }
            }
            catch (Exception ex)
            {
                UpdateReaderStatus((_isVietnameseUi ? "Không thể khởi tạo WebView2: " : "Failed to initialize WebView2: ") + ex.Message);
            }
        }

        #endif

        private void EnsureReaderReady()
        {
        }

        private string GetCurrentReaderLibraryRoot()
        {
            if (!string.IsNullOrWhiteSpace(_readerLibraryRootOverride))
            {
                return _readerLibraryRootOverride;
            }

            return txtDownloadPath != null && !string.IsNullOrWhiteSpace(txtDownloadPath.Text)
                ? txtDownloadPath.Text.Trim()
                : PortablePaths.DefaultDownloadRoot;
        }

        private async void RefreshReaderLibraryIfNeeded(bool forceRefresh)
        {
            await RefreshReaderLibraryAsync(forceRefresh);
        }

        private async void RefreshReaderNovelLibraryIfNeeded(bool forceRefresh)
        {
            await RefreshReaderNovelLibraryAsync(forceRefresh);
        }

        private async Task RefreshReaderLibraryAsync(bool forceRefresh)
        {
            if (_readerMangaList == null)
            {
                return;
            }

            if ((_readerAutoRefreshInProgress && !forceRefresh) || _readerLibraryRefreshInProgress)
            {
                return;
            }

            string root = GetCurrentReaderLibraryRoot();
            EnsureReaderLibraryWatcher(root, isNovel: false);

            if (!forceRefresh && string.Equals(root, _lastReaderLibraryRoot, StringComparison.OrdinalIgnoreCase) && _readerLibrary.Count > 0)
            {
                return;
            }

            UpdateReaderStatus(_isVietnameseUi ? "Đang quét root/domain/book..." : "Scanning root/domain/book...");
            _lastReaderAutoRefreshUtc = DateTime.UtcNow;

            int totalUnits = CountReaderLibraryScanUnits(root);
            int currentUnits = 0;
            UpdateReaderWatchLoadProgress(_readerStatusProgressBar, _readerStatusProgressText, 0, totalUnits);

            _readerLibraryRefreshInProgress = true;
            try
            {
                List<ReaderMangaItem> library = await Task.Run(() => ScanReaderLibrary(root, totalUnits, () =>
                {
                    System.Threading.Interlocked.Increment(ref currentUnits);
                    if (currentUnits % 10 == 0 || currentUnits >= totalUnits)
                    {
                        Dispatcher.BeginInvoke(new Action(() => UpdateReaderWatchLoadProgress(_readerStatusProgressBar, _readerStatusProgressText, currentUnits, totalUnits)));
                    }
                }));
                _lastReaderLibraryRoot = root;
                _readerLibrary = library;
                _readerDomains = BuildReaderDomainItems(_readerLibrary);
                ApplyReaderMangaWatchSorts(keepSelection: true);
                _readerSummaryText.Text = _isVietnameseUi
                    ? $"Root: {root}\nTìm thấy {_readerDomains.Count} domain / {_readerLibrary.Count} book."
                    : $"Root: {root}\nFound {_readerDomains.Count} domains / {_readerLibrary.Count} books.";

                if (_readerLibrary.Count == 0)
                {
                    ClearReaderDomainList();
                    ClearReaderBookList();
                    ClearReaderChapterList();
                    _readerChapterList.ItemsSource = null;
                    ClearReaderFileList();
                    _currentReaderManga = null;
                    _currentReaderDomain = null;
                    _currentReaderChapter = null;
                    _currentReaderPage = null;
                    RenderReaderPlaceholder();
                    UpdateReaderNavigationState();
                    UpdateReaderStatus(_isVietnameseUi
                        ? "Chưa tìm thấy thư mục manga hợp lệ trong download root."
                        : "No valid manga folders were found in the current download root.");
                    return;
                }

                ReaderDomainItem selectedDomain = _currentReaderDomain != null
                    ? _readerDomains.FirstOrDefault(item => string.Equals(item.Name, _currentReaderDomain.Name, StringComparison.OrdinalIgnoreCase))
                    : null;

                if (selectedDomain == null && _currentReaderManga != null)
                {
                    selectedDomain = _readerDomains.FirstOrDefault(item =>
                        item.Books.Any(book => string.Equals(book.FolderPath, _currentReaderManga.FolderPath, StringComparison.OrdinalIgnoreCase)));
                }

                if (selectedDomain == null)
                {
                    selectedDomain = _readerDomains.First();
                }

                OpenReaderDomain(selectedDomain, keepBookSelection: true);
            }
            finally
            {
                _readerLibraryRefreshInProgress = false;
                HideReaderWatchLoadProgress(_readerStatusProgressBar, _readerStatusProgressText);
            }
        }

        private async Task RefreshReaderLibraryWhenIdleAsync(bool forceRefresh)
        {
            int waitedMs = 0;
            while (_readerLibraryRefreshInProgress && waitedMs < 10000)
            {
                await Task.Delay(50);
                waitedMs += 50;
            }

            await RefreshReaderLibraryAsync(forceRefresh);
        }

        private string GetCurrentReaderNovelLibraryRoot()
        {
            if (!string.IsNullOrWhiteSpace(_readerNovelLibraryRootOverride))
            {
                return _readerNovelLibraryRootOverride;
            }

            return txtDownloadPath != null && !string.IsNullOrWhiteSpace(txtDownloadPath.Text)
                ? txtDownloadPath.Text.Trim()
                : PortablePaths.DefaultDownloadRoot;
        }

        private void SetReaderCurrentTitle()
        {
            if (_readerCurrentTitleText == null)
            {
                return;
            }

            string bookName = _currentReaderManga?.Name ?? string.Empty;
            string chapterName = _currentReaderChapter?.Name ?? string.Empty;
            _readerCurrentTitleText.Text = string.IsNullOrWhiteSpace(chapterName)
                ? bookName
                : bookName + Environment.NewLine + chapterName;
        }

        private void SetReaderNovelCurrentTitle()
        {
            if (_readerNovelCurrentTitleText == null)
            {
                return;
            }

            string bookName = _currentReaderNovelBook?.Name ?? string.Empty;
            string chapterName = _currentReaderNovelChapter?.Name ?? string.Empty;
            _readerNovelCurrentTitleText.Text = string.IsNullOrWhiteSpace(chapterName)
                ? bookName
                : bookName + Environment.NewLine + chapterName;
        }

        private async Task RefreshReaderNovelLibraryAsync(bool forceRefresh)
        {
            if (_readerNovelBookList == null)
            {
                return;
            }

            if (_readerNovelLibraryRefreshInProgress)
            {
                return;
            }

            string root = GetCurrentReaderNovelLibraryRoot();
            EnsureReaderLibraryWatcher(root, isNovel: true);
            UpdateReaderNovelStatus(_isVietnameseUi ? "Đang quét root/domain/book novel..." : "Scanning novel root/domain/book...");

            int totalUnits = CountReaderNovelLibraryScanUnits(root);
            int currentUnits = 0;
            UpdateReaderWatchLoadProgress(_readerNovelStatusProgressBar, _readerNovelStatusProgressText, 0, totalUnits);

            _readerNovelLibraryRefreshInProgress = true;
            try
            {
                List<ReaderNovelBookItem> library = await Task.Run(() => ScanReaderNovelLibrary(root, totalUnits, () =>
                {
                    System.Threading.Interlocked.Increment(ref currentUnits);
                    if (currentUnits % 10 == 0 || currentUnits >= totalUnits)
                    {
                        Dispatcher.BeginInvoke(new Action(() => UpdateReaderWatchLoadProgress(_readerNovelStatusProgressBar, _readerNovelStatusProgressText, currentUnits, totalUnits)));
                    }
                }));
                _readerNovelLibrary = library;
                _readerNovelDomains = BuildReaderNovelDomainItems(_readerNovelLibrary);
                ApplyReaderNovelWatchSorts(keepSelection: true);
                _readerNovelSummaryText.Text = _isVietnameseUi
                    ? $"Root: {root}\nTìm thấy {_readerNovelDomains.Count} domain / {_readerNovelLibrary.Count} book."
                    : $"Root: {root}\nFound {_readerNovelDomains.Count} domains / {_readerNovelLibrary.Count} books.";

                if (_readerNovelLibrary.Count == 0)
                {
                    ClearReaderNovelDomainList();
                    ClearReaderNovelBookList();
                    ClearReaderNovelChapterList();
                    ClearReaderNovelFileList();
                    _currentReaderNovelDomain = null;
                    _currentReaderNovelBook = null;
                    _currentReaderNovelChapter = null;
                    _currentReaderNovelFile = null;
                    UpdateReaderNovelStatus(_isVietnameseUi
                        ? "Chưa tìm thấy thư mục novel có file .md."
                        : "No novel folders with .md files were found.");
                    return;
                }

                ReaderNovelDomainItem selectedDomain = _currentReaderNovelDomain != null
                    ? _readerNovelDomains.FirstOrDefault(item => string.Equals(item.Name, _currentReaderNovelDomain.Name, StringComparison.OrdinalIgnoreCase))
                    : _readerNovelDomains.FirstOrDefault();

                OpenReaderNovelDomain(selectedDomain, keepBookSelection: true);
            }
            finally
            {
                _readerNovelLibraryRefreshInProgress = false;
                HideReaderWatchLoadProgress(_readerNovelStatusProgressBar, _readerNovelStatusProgressText);
            }
        }

        private async Task RefreshReaderNovelLibraryWhenIdleAsync(bool forceRefresh)
        {
            int waitedMs = 0;
            while (_readerNovelLibraryRefreshInProgress && waitedMs < 10000)
            {
                await Task.Delay(50);
                waitedMs += 50;
            }

            await RefreshReaderNovelLibraryAsync(forceRefresh);
        }
        private List<ReaderMangaItem> ScanReaderLibrary(string root, int totalUnits, Action onUnitProcessed)
        {
            var result = new System.Collections.Concurrent.ConcurrentBag<ReaderMangaItem>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return new List<ReaderMangaItem>();
            }

            ReaderCompletionState completionState = ShouldShowReaderCompletionBadges(root)
                ? LoadReaderCompletionState(root)
                : null;
            ReaderDownloadWatchState downloadState = LoadReaderDownloadWatchState(root);

            var itemsToScan = new List<(string bookFolder, string domainName)>();
            foreach (string domainFolder in SafeGetDirectories(root))
            {
                onUnitProcessed?.Invoke();
                string domainName = Path.GetFileName(domainFolder);
                if (string.IsNullOrWhiteSpace(domainName))
                {
                    continue;
                }

                foreach (string bookFolder in SafeGetDirectories(domainFolder))
                {
                    itemsToScan.Add((bookFolder, domainName));
                }
            }

            System.Threading.ThreadPool.SetMinThreads(128, 128);
            var parallelOptions = new System.Threading.Tasks.ParallelOptions
            {
                MaxDegreeOfParallelism = 128
            };

            System.Threading.Tasks.Parallel.ForEach(itemsToScan, parallelOptions, pair =>
            {
                ReaderMangaItem book = BuildReaderMangaItem(pair.bookFolder, pair.domainName, 1, completionState, downloadState);
                if (book != null)
                {
                    result.Add(book);
                }
                onUnitProcessed?.Invoke();
            });

            return result
                .OrderBy(item => item.SourceGroup ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, _readerSortComparer)
                .ToList();
        }

        private ReaderMangaItem BuildReaderMangaItem(string folderPath, string sourceGroup, int depth, ReaderCompletionState completionState, ReaderDownloadWatchState downloadState)
        {
            var chapters = new List<ReaderChapterItem>();
            string bookName = Path.GetFileName(folderPath);

            CollectReaderChapterItems(folderPath, 0, chapters);

            if (chapters.Count == 0)
            {
                return null;
            }

            bool isCompleted = false;
            if (completionState != null)
            {
                completionState.ApplyCompletionState(bookName, chapters);
                isCompleted = chapters.All(chapter => chapter != null && chapter.IsCompleted);
            }

            ReaderChapterAnalysis analysis = AnalyzeReaderChapterNumbers(chapters);
            string downloadStateText = downloadState != null ? downloadState.Get(bookName) : null;

            return new ReaderMangaItem
            {
                Name = bookName,
                SourceGroup = sourceGroup,
                FolderPath = folderPath,
                FolderDepth = depth,
                LastModifiedUtc = chapters.Max(item => item.LastModifiedUtc),
                Chapters = chapters,
                IsCompleted = isCompleted,
                DownloadStateText = downloadStateText,
                HasMissingIntegerGap = analysis.MissingRanges.Count > 0
            };
        }

        private void CollectReaderChapterItems(string folderPath, int depth, ICollection<ReaderChapterItem> chapters)
        {
            string folderName = Path.GetFileName(folderPath);
            if (chapters == null || string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(folderName) || folderName.StartsWith(".", StringComparison.Ordinal))
            {
                return;
            }

            if (DirectoryContainsImages(folderPath))
            {
                ReaderChapterItem chapter = BuildReaderChapterItem(folderPath, Path.GetFileName(folderPath), depth);
                if (chapter != null)
                {
                    chapters.Add(chapter);
                }
            }

            foreach (string childFolder in SafeGetDirectories(folderPath))
            {
                CollectReaderChapterItems(childFolder, depth + 1, chapters);
            }
        }

        private static bool TryParseReaderChapterNumber(string chapterName, out double number, out bool isDecimal)
        {
            number = 0d;
            isDecimal = false;

            if (string.IsNullOrWhiteSpace(chapterName))
            {
                return false;
            }

            Match match = Regex.Match(chapterName, @"(?<!\d)(\d+(?:[.,]\d+)?)", RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return false;
            }

            string token = match.Groups[1].Value.Replace(',', '.');
            if (!double.TryParse(token, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number))
            {
                return false;
            }

            isDecimal = Math.Abs(number - Math.Truncate(number)) > 0.0001d;
            return true;
        }

        private static ReaderChapterAnalysis AnalyzeReaderChapterNumbers(IList<ReaderChapterItem> chapters)
        {
            var analysis = new ReaderChapterAnalysis();
            if (chapters == null || chapters.Count == 0)
            {
                return analysis;
            }

            var integerMap = new Dictionary<int, List<ReaderChapterItem>>();
            var integerNumbers = new List<int>();

            foreach (ReaderChapterItem chapter in chapters)
            {
                if (chapter == null)
                {
                    continue;
                }

                chapter.ParsedChapterNumber = null;
                chapter.IsDecimalChapter = false;
                chapter.HasMissingIntegerGap = false;
                chapter.DisplayForeground = null;

                if (!TryParseReaderChapterNumber(chapter.Name, out double chapterNumber, out bool isDecimal))
                {
                    analysis.UnknownCount++;
                    continue;
                }

                chapter.ParsedChapterNumber = chapterNumber;
                chapter.IsDecimalChapter = isDecimal;
                if (isDecimal)
                {
                    analysis.DecimalCount++;
                    continue;
                }

                analysis.IntegerCount++;
                int integerNumber = (int)Math.Round(chapterNumber);
                if (!integerMap.TryGetValue(integerNumber, out List<ReaderChapterItem> items))
                {
                    items = new List<ReaderChapterItem>();
                    integerMap[integerNumber] = items;
                    integerNumbers.Add(integerNumber);
                }

                items.Add(chapter);
            }

            integerNumbers.Sort();
            var gapBoundaries = new HashSet<int>();
            for (int i = 1; i < integerNumbers.Count; i++)
            {
                int previous = integerNumbers[i - 1];
                int current = integerNumbers[i];
                int missing = current - previous - 1;
                if (missing <= 0)
                {
                    continue;
                }

                gapBoundaries.Add(previous);
                gapBoundaries.Add(current);

                analysis.MissingRanges.Add(missing == 1
                    ? (previous + 1).ToString(CultureInfo.InvariantCulture)
                    : (previous + 1).ToString(CultureInfo.InvariantCulture) + "-" + (current - 1).ToString(CultureInfo.InvariantCulture));
            }

            foreach (KeyValuePair<int, List<ReaderChapterItem>> pair in integerMap)
            {
                bool isGapBoundary = gapBoundaries.Contains(pair.Key);
                foreach (ReaderChapterItem chapter in pair.Value)
                {
                    chapter.HasMissingIntegerGap = isGapBoundary;
                }
            }

            return analysis;
        }

        private void ApplyReaderChapterPresentation(ReaderMangaItem manga)
        {
            if (_readerChapterList == null)
            {
                return;
            }

            if (manga == null || manga.Chapters == null || manga.Chapters.Count == 0)
            {
                if (_readerChapterStatsText != null)
                {
                    _readerChapterStatsText.Text = string.Empty;
                }
                RefreshReaderChapterIssueGrid(null);

                return;
            }

            ReaderChapterAnalysis analysis = AnalyzeReaderChapterNumbers(manga.Chapters);
            RefreshReaderChapterIssueGrid(manga);
            Brush normalBrush = (Brush)TryFindResource("CyberpunkTextBrush") ?? Brushes.White;

            foreach (ReaderChapterItem chapter in manga.Chapters)
            {
                if (chapter == null)
                {
                    continue;
                }

                if (chapter.IsDecimalChapter)
                {
                    chapter.DisplayForeground = Brushes.Cyan;
                }
                else if (chapter.HasMissingIntegerGap)
                {
                    chapter.DisplayForeground = Brushes.Yellow;
                }
                else
                {
                    chapter.DisplayForeground = normalBrush;
                }
            }

            UpdateReaderChapterStatsText(analysis);
        }

        private void UpdateReaderChapterStatsText(ReaderChapterAnalysis analysis)
        {
            if (_readerChapterStatsText == null) return;

            _readerChapterStatsText.Inlines.Clear();
            _readerChapterStatsText.FontSize = 11.5; // larger text size

            string prefix = _isVietnameseUi
                ? $"Chap nguyên: {analysis.IntegerCount} | chap thập phân: {analysis.DecimalCount} | chap khác: {analysis.UnknownCount} | "
                : $"Integer chapters: {analysis.IntegerCount} | decimal chapters: {analysis.DecimalCount} | other: {analysis.UnknownCount} | ";

            _readerChapterStatsText.Inlines.Add(new System.Windows.Documents.Run(prefix)
            {
                Foreground = (Brush)TryFindResource("CyberpunkMutedTextBrush") ?? System.Windows.Media.Brushes.Gray
            });

            string missingLabel = _isVietnameseUi ? "thiếu: " : "missing: ";
            string missingText = analysis.MissingRanges.Count == 0
                ? (_isVietnameseUi ? "không thiếu chap nguyên" : "no missing integers")
                : string.Join(", ", analysis.MissingRanges);

            var missingRun = new System.Windows.Documents.Run(missingLabel + missingText)
            {
                FontWeight = System.Windows.FontWeights.ExtraBold,
                FontSize = 13.5 // make missing part stand out even more
            };

            if (analysis.MissingRanges.Count > 0)
            {
                missingRun.Foreground = System.Windows.Media.Brushes.Tomato; // bright warning color
            }
            else
            {
                missingRun.Foreground = System.Windows.Media.Brushes.LightGreen; // positive status
            }

            _readerChapterStatsText.Inlines.Add(missingRun);
        }

        private ReaderChapterItem BuildReaderChapterItem(string folderPath, string chapterName, int depth)
        {
            return new ReaderChapterItem
            {
                Name = chapterName,
                FolderPath = folderPath,
                FolderDepth = depth,
                LastModifiedUtc = SafeGetLastWriteTimeUtc(folderPath),
                Pages = new List<ReaderPageItem>()
            };
        }

        private void EnsureChapterPagesLoaded(ReaderChapterItem chapter)
        {
            if (chapter == null || (chapter.Pages != null && chapter.Pages.Count > 0))
            {
                return;
            }

            try
            {
                string[] pageFiles = Directory.GetFiles(chapter.FolderPath)
                    .Where(IsSupportedReaderImageFile)
                    .OrderBy(path => Path.GetFileNameWithoutExtension(path), _readerSortComparer)
                    .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var pages = pageFiles
                    .Select((path, index) => new ReaderPageItem
                    {
                        Index = index,
                        Name = Path.GetFileName(path),
                        FilePath = path,
                        LastModifiedUtc = SafeGetLastWriteTimeUtc(path)
                    })
                    .ToList();

                chapter.Pages = pages;
            }
            catch
            {
                chapter.Pages = new List<ReaderPageItem>();
            }
        }

        private List<ReaderNovelDomainItem> BuildReaderNovelDomainItems(IReadOnlyList<ReaderNovelBookItem> books)
        {
            var domains = new List<ReaderNovelDomainItem>();
            if (books == null || books.Count == 0)
            {
                return domains;
            }

            IEnumerable<IGrouping<string, ReaderNovelBookItem>> groupedBooks = books
                .GroupBy(item => string.IsNullOrWhiteSpace(item.SourceGroup) ? "root" : item.SourceGroup, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, ReaderNovelBookItem> group in groupedBooks)
            {
                List<ReaderNovelBookItem> domainBooks = group.ToList();
                domains.Add(new ReaderNovelDomainItem
                {
                    Name = group.Key,
                    FolderPath = GetReaderCommonParentFolder(domainBooks.Select(item => item.FolderPath)),
                    LastModifiedUtc = domainBooks.Count == 0 ? DateTime.MinValue : domainBooks.Max(item => item.LastModifiedUtc),
                    Books = domainBooks
                });
            }

            return domains;
        }

        private void ToggleReaderSort(ReaderWatchSortState state, ReaderWatchSortField field)
        {
            if (state == null)
            {
                return;
            }

            if (state.Field == field)
            {
                state.Ascending = !state.Ascending;
            }
            else
            {
                state.Field = field;
                state.Ascending = field == ReaderWatchSortField.Name;
            }
        }

        private List<ReaderDomainItem> SortReaderDomains(IEnumerable<ReaderDomainItem> domains)
        {
            return SortReaderDomainItems(domains, _readerMangaDomainSortState);
        }

        private List<ReaderDomainItem> SortReaderDomainItems(IEnumerable<ReaderDomainItem> domains, ReaderWatchSortState state)
        {
            return OrderReaderItems(
                domains,
                state,
                item => item?.LastModifiedUtc ?? DateTime.MinValue,
                item => item?.Name);
        }

        private List<ReaderMangaItem> SortReaderBooks(IEnumerable<ReaderMangaItem> books)
        {
            return OrderReaderItems(
                books,
                _readerMangaBookSortState,
                item => item?.LastModifiedUtc ?? DateTime.MinValue,
                item => item?.Name);
        }

        private List<ReaderNovelDomainItem> SortReaderNovelDomains(IEnumerable<ReaderNovelDomainItem> domains)
        {
            return OrderReaderItems(
                domains,
                _readerNovelDomainSortState,
                item => item?.LastModifiedUtc ?? DateTime.MinValue,
                item => item?.Name);
        }

        private List<ReaderNovelBookItem> SortReaderNovelBooks(IEnumerable<ReaderNovelBookItem> books)
        {
            return OrderReaderItems(
                books,
                _readerNovelBookSortState,
                item => item?.LastModifiedUtc ?? DateTime.MinValue,
                item => item?.Name);
        }

        private List<T> OrderReaderItems<T>(IEnumerable<T> items, ReaderWatchSortState state, Func<T, DateTime> modifiedSelector, Func<T, string> nameSelector)
        {
            IEnumerable<T> source = items ?? Enumerable.Empty<T>();
            IOrderedEnumerable<T> ordered;

            if (state != null && state.Field == ReaderWatchSortField.DateModified)
            {
                ordered = state.Ascending
                    ? source.OrderBy(modifiedSelector).ThenBy(nameSelector, _readerSortComparer)
                    : source.OrderByDescending(modifiedSelector).ThenBy(nameSelector, _readerSortComparer);
            }
            else
            {
                ordered = state != null && state.Ascending
                    ? source.OrderBy(nameSelector, _readerSortComparer).ThenByDescending(modifiedSelector)
                    : source.OrderByDescending(nameSelector, _readerSortComparer).ThenByDescending(modifiedSelector);
            }

            return ordered.ToList();
        }

        private void ApplyReaderMangaWatchSorts(bool keepSelection)
        {
            _readerDomains = SortReaderDomains(_readerDomains);
            foreach (ReaderDomainItem domain in _readerDomains)
            {
                domain.Books = SortReaderBooks(domain.Books);
                domain.LastModifiedUtc = domain.Books.Count == 0 ? DateTime.MinValue : domain.Books.Max(item => item.LastModifiedUtc);
            }

            UpdateReaderDomainListItems(_readerDomains);
            if (_currentReaderDomain != null)
            {
                _currentReaderDomain = _readerDomains.FirstOrDefault(item => string.Equals(item.Name, _currentReaderDomain.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (_currentReaderDomain != null)
            {
                OpenReaderDomain(_currentReaderDomain, keepBookSelection: keepSelection);
            }
            else
            {
                SyncReaderDomainListSelection(null);
                ClearReaderBookList();
            }

            RefreshReaderSortButtonLabel(_readerMangaDomainSortDateButton, _readerMangaDomainSortState, ReaderWatchSortField.DateModified, "DATE");
            RefreshReaderSortButtonLabel(_readerMangaDomainSortNameButton, _readerMangaDomainSortState, ReaderWatchSortField.Name, "NAME");
            RefreshReaderSortButtonLabel(_readerMangaBookSortDateButton, _readerMangaBookSortState, ReaderWatchSortField.DateModified, "DATE");
            RefreshReaderSortButtonLabel(_readerMangaBookSortNameButton, _readerMangaBookSortState, ReaderWatchSortField.Name, "NAME");
        }

        private void ApplyReaderNovelWatchSorts(bool keepSelection)
        {
            _readerNovelDomains = SortReaderNovelDomains(_readerNovelDomains);
            foreach (ReaderNovelDomainItem domain in _readerNovelDomains)
            {
                domain.Books = SortReaderNovelBooks(domain.Books);
                domain.LastModifiedUtc = domain.Books.Count == 0 ? DateTime.MinValue : domain.Books.Max(item => item.LastModifiedUtc);
            }

            UpdateReaderNovelDomainListItems(_readerNovelDomains);
            if (_currentReaderNovelDomain != null)
            {
                _currentReaderNovelDomain = _readerNovelDomains.FirstOrDefault(item => string.Equals(item.Name, _currentReaderNovelDomain.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (_currentReaderNovelDomain != null)
            {
                OpenReaderNovelDomain(_currentReaderNovelDomain, keepBookSelection: keepSelection);
            }
            else
            {
                SyncReaderNovelDomainListSelection(null);
                ClearReaderNovelBookList();
            }

            RefreshReaderSortButtonLabel(_readerNovelDomainSortDateButton, _readerNovelDomainSortState, ReaderWatchSortField.DateModified, "DATE");
            RefreshReaderSortButtonLabel(_readerNovelDomainSortNameButton, _readerNovelDomainSortState, ReaderWatchSortField.Name, "NAME");
            RefreshReaderSortButtonLabel(_readerNovelBookSortDateButton, _readerNovelBookSortState, ReaderWatchSortField.DateModified, "DATE");
            RefreshReaderSortButtonLabel(_readerNovelBookSortNameButton, _readerNovelBookSortState, ReaderWatchSortField.Name, "NAME");
        }

        private void ReaderMangaDomainSortDate_Click(object sender, RoutedEventArgs e)
        {
            ToggleReaderSort(_readerMangaDomainSortState, ReaderWatchSortField.DateModified);
            ApplyReaderMangaWatchSorts(keepSelection: true);
        }

        private void ReaderMangaDomainSortName_Click(object sender, RoutedEventArgs e)
        {
            ToggleReaderSort(_readerMangaDomainSortState, ReaderWatchSortField.Name);
            ApplyReaderMangaWatchSorts(keepSelection: true);
        }

        private void ReaderMangaBookSortDate_Click(object sender, RoutedEventArgs e)
        {
            ToggleReaderSort(_readerMangaBookSortState, ReaderWatchSortField.DateModified);
            ApplyReaderMangaWatchSorts(keepSelection: true);
        }

        private void ReaderMangaBookSortName_Click(object sender, RoutedEventArgs e)
        {
            ToggleReaderSort(_readerMangaBookSortState, ReaderWatchSortField.Name);
            ApplyReaderMangaWatchSorts(keepSelection: true);
        }

        private void ReaderNovelDomainSortDate_Click(object sender, RoutedEventArgs e)
        {
            ToggleReaderSort(_readerNovelDomainSortState, ReaderWatchSortField.DateModified);
            ApplyReaderNovelWatchSorts(keepSelection: true);
        }

        private void ReaderNovelDomainSortName_Click(object sender, RoutedEventArgs e)
        {
            ToggleReaderSort(_readerNovelDomainSortState, ReaderWatchSortField.Name);
            ApplyReaderNovelWatchSorts(keepSelection: true);
        }

        private void ReaderNovelBookSortDate_Click(object sender, RoutedEventArgs e)
        {
            ToggleReaderSort(_readerNovelBookSortState, ReaderWatchSortField.DateModified);
            ApplyReaderNovelWatchSorts(keepSelection: true);
        }

        private void ReaderNovelBookSortName_Click(object sender, RoutedEventArgs e)
        {
            ToggleReaderSort(_readerNovelBookSortState, ReaderWatchSortField.Name);
            ApplyReaderNovelWatchSorts(keepSelection: true);
        }

        private List<ReaderNovelBookItem> ScanReaderNovelLibrary(string root, int totalUnits, Action onUnitProcessed)
        {
            var result = new List<ReaderNovelBookItem>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string domainFolder in SafeGetDirectories(root))
            {
                onUnitProcessed?.Invoke();
                string domainName = Path.GetFileName(domainFolder);
                if (string.IsNullOrWhiteSpace(domainName) || domainName.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                CollectReaderNovelBooksRecursive(domainFolder, domainName, 0, seen, result, onUnitProcessed);
            }

            return result
                .OrderBy(item => item.SourceGroup ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.FolderDepth)
                .ThenBy(item => item.Name, _readerSortComparer)
                .ToList();
        }

        private void CollectReaderNovelBooksRecursive(string folderPath, string sourceGroup, int depth, ISet<string> seen, ICollection<ReaderNovelBookItem> result, Action onUnitProcessed)
        {
            string folderName = Path.GetFileName(folderPath);
            if (string.IsNullOrWhiteSpace(folderPath) || seen.Contains(folderPath) || string.IsNullOrWhiteSpace(folderName) || folderName.StartsWith(".", StringComparison.Ordinal))
            {
                return;
            }

            TryAddReaderNovelBook(folderPath, sourceGroup, depth, seen, result);
            onUnitProcessed?.Invoke();

            foreach (string childFolder in SafeGetDirectories(folderPath))
            {
                CollectReaderNovelBooksRecursive(childFolder, sourceGroup, depth + 1, seen, result, onUnitProcessed);
            }
        }

        private void TryAddReaderNovelBook(string folderPath, string sourceGroup, int depth, ISet<string> seen, ICollection<ReaderNovelBookItem> result)
        {
            if (seen.Contains(folderPath))
            {
                return;
            }

            string folderName = Path.GetFileName(folderPath);
            if (string.IsNullOrWhiteSpace(folderName) || folderName.StartsWith(".", StringComparison.Ordinal))
            {
                return;
            }

            // ponytail: same trick as manga scan. One build pass, no pre-check double hit.
            ReaderNovelBookItem book = BuildReaderNovelBookItem(folderPath, sourceGroup, depth);
            if (book == null || book.Chapters.Count == 0)
            {
                return;
            }

            seen.Add(folderPath);
            result.Add(book);
            Debug.Assert(book.Chapters.Count > 0, "Reader novel scan returned empty chapter list.");
        }

        private ReaderNovelBookItem BuildReaderNovelBookItem(string folderPath, string sourceGroup, int depth)
        {
            var chapters = new List<ReaderNovelChapterItem>();
            string bookName = Path.GetFileName(folderPath);

            CollectReaderNovelChapterItems(folderPath, 0, chapters);

            if (chapters.Count == 0)
            {
                return null;
            }

            return new ReaderNovelBookItem
            {
                Name = bookName,
                SourceGroup = sourceGroup,
                FolderPath = folderPath,
                FolderDepth = depth,
                LastModifiedUtc = chapters.Max(item => item.LastModifiedUtc),
                Chapters = chapters
            };
        }

        private void CollectReaderNovelChapterItems(string folderPath, int depth, ICollection<ReaderNovelChapterItem> chapters)
        {
            string folderName = Path.GetFileName(folderPath);
            if (chapters == null || string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(folderName) || folderName.StartsWith(".", StringComparison.Ordinal))
            {
                return;
            }

            if (DirectoryContainsMarkdown(folderPath))
            {
                ReaderNovelChapterItem chapter = BuildReaderNovelChapterItem(folderPath, Path.GetFileName(folderPath), depth);
                if (chapter != null)
                {
                    chapters.Add(chapter);
                }
            }

            foreach (string childFolder in SafeGetDirectories(folderPath))
            {
                CollectReaderNovelChapterItems(childFolder, depth + 1, chapters);
            }
        }

        private ReaderNovelChapterItem BuildReaderNovelChapterItem(string folderPath, string chapterName, int depth)
        {
            bool hasMarkdown;
            try
            {
                hasMarkdown = Directory.EnumerateFiles(folderPath, "*.md", SearchOption.TopDirectoryOnly).Any();
            }
            catch
            {
                hasMarkdown = false;
            }

            if (!hasMarkdown)
            {
                return null;
            }

            return new ReaderNovelChapterItem
            {
                Name = chapterName,
                FolderPath = folderPath,
                FolderDepth = depth,
                LastModifiedUtc = SafeGetLastWriteTimeUtc(folderPath),
                Files = new List<ReaderMarkdownItem>()
            };
        }

        private void EnsureNovelChapterFilesLoaded(ReaderNovelChapterItem chapter)
        {
            if (chapter == null || (chapter.Files != null && chapter.Files.Count > 0))
            {
                return;
            }

            try
            {
                string[] markdownFiles = Directory.GetFiles(chapter.FolderPath, "*.md", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => Path.GetFileNameWithoutExtension(path), _readerSortComparer)
                    .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var files = markdownFiles
                    .Select((path, index) => new ReaderMarkdownItem
                    {
                        Index = index,
                        Name = Path.GetFileName(path),
                        FilePath = path,
                        LastModifiedUtc = SafeGetLastWriteTimeUtc(path)
                    })
                    .ToList();

                chapter.Files = files;
            }
            catch
            {
                chapter.Files = new List<ReaderMarkdownItem>();
            }
        }

        private static bool DirectoryContainsMarkdown(string folderPath)
        {
            try
            {
                return Directory.EnumerateFiles(folderPath, "*.md", SearchOption.TopDirectoryOnly).Any();
            }
            catch
            {
                return false;
            }
        }

        private static bool DirectoryContainsImages(string folderPath)
        {
            try
            {
                return Directory.EnumerateFiles(folderPath).Any(IsSupportedReaderImageFile);
            }
            catch
            {
                return false;
            }
        }

        private static DateTime SafeGetLastWriteTimeUtc(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.GetLastWriteTimeUtc(path);
                }

                if (Directory.Exists(path))
                {
                    return Directory.GetLastWriteTimeUtc(path);
                }
            }
            catch
            {
            }

            return DateTime.MinValue;
        }

        private static bool IsSupportedReaderImageFile(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            return string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".bmp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SafeGetDirectories(string folderPath)
        {
            try
            {
                return Directory.GetDirectories(folderPath)
                    .Where(path =>
                    {
                        string folderName = Path.GetFileName(path);
                        return !string.IsNullOrWhiteSpace(folderName) &&
                               !folderName.StartsWith(".", StringComparison.Ordinal);
                    })
                    .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private int CountReaderLibraryScanUnits(string root)
        {
            return CountReaderScanUnits(root, isNovel: false);
        }

        private int CountReaderNovelLibraryScanUnits(string root)
        {
            return CountReaderScanUnits(root, isNovel: true);
        }

        private int CountReaderScanUnits(string root, bool isNovel)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return 0;
            }

            int total = 0;
            foreach (string domainFolder in SafeGetDirectories(root))
            {
                total += 1;
                total += SafeGetDirectories(domainFolder).Count();
            }

            // ponytail: progress counts direct domain/book folders only. Exact enough and avoids deep scan lag.
            return total;
        }

        private static IEnumerable<string> SafeGetFiles(string folderPath, string searchPattern, SearchOption searchOption)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                {
                    return Array.Empty<string>();
                }

                return Directory.GetFiles(folderPath, searchPattern, searchOption)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private bool ShouldShowReaderCompletionBadges(string root)
        {
            if (!string.IsNullOrWhiteSpace(_readerLibraryRootOverride))
            {
                return false;
            }

            string normalizedRoot = NormalizeReaderPath(root);
            string normalizedDefaultRoot = NormalizeReaderPath(PortablePaths.DefaultDownloadRoot);
            return !string.IsNullOrWhiteSpace(normalizedRoot) &&
                   !string.IsNullOrWhiteSpace(normalizedDefaultRoot) &&
                   string.Equals(normalizedRoot, normalizedDefaultRoot, StringComparison.OrdinalIgnoreCase);
        }

        private ReaderCompletionState GetReaderCompletionStateForCurrentRoot()
        {
            string root = GetCurrentReaderLibraryRoot();
            return ShouldShowReaderCompletionBadges(root)
                ? LoadReaderCompletionState(root)
                : null;
        }

        private ReaderDownloadWatchState LoadReaderDownloadWatchState(string root)
        {
            var state = new ReaderDownloadWatchState();

            foreach (string processFile in EnumerateReaderProcessMarkdownFiles(root))
            {
                string currentBookName = null;
                var rowStatuses = new List<string>();

                try
                {
                    foreach (string line in File.ReadLines(processFile, Encoding.UTF8))
                    {
                        if (line.StartsWith("Book:", StringComparison.OrdinalIgnoreCase))
                        {
                            currentBookName = line.Substring("Book:".Length).Trim();
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(currentBookName) || !line.StartsWith("|", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (line.StartsWith("| No.", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("| :---", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string[] cells = line.Split('|');
                        if (cells.Length < 4)
                        {
                            continue;
                        }

                        string rowStatus = cells.Length > 2 ? cells[2].Trim() : string.Empty;
                        if (!string.IsNullOrWhiteSpace(rowStatus))
                        {
                            rowStatuses.Add(rowStatus);
                        }
                    }

                    string summary = SummarizeReaderDownloadWatchState(rowStatuses);
                    if (!string.IsNullOrWhiteSpace(currentBookName) && !string.IsNullOrWhiteSpace(summary))
                    {
                        state.Add(currentBookName, summary);
                    }
                }
                catch
                {
                }
            }

            return state;
        }

        private static string SummarizeReaderDownloadWatchState(IReadOnlyList<string> rowStatuses)
        {
            if (rowStatuses == null || rowStatuses.Count == 0)
            {
                return null;
            }

            bool hasPending = false;
            bool hasDone = false;

            foreach (string rowStatus in rowStatuses)
            {
                string normalized = NormalizeReaderDownloadStateText(rowStatus);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                if (string.Equals(normalized, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    hasDone = true;
                    continue;
                }

                if (string.Equals(normalized, "downloading", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, "pending", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, "retrying", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, "error", StringComparison.OrdinalIgnoreCase))
                {
                    hasPending = true;
                }
                else
                {
                    hasPending = true;
                }
            }

            if (hasPending)
            {
                return "downloading";
            }

            return hasDone ? "completed" : null;
        }

        private static string NormalizeReaderDownloadStateText(string value)
        {
            string text = (value ?? string.Empty).Trim().ToLowerInvariant();
            switch (text)
            {
                case "done":
                case "completed":
                case "hoan tat":
                case "hoàn tất":
                    return "completed";
                case "downloading":
                case "pending":
                case "retrying":
                case "error":
                case "cancelled":
                case "paused":
                    return "downloading";
                default:
                    return text;
            }
        }

        private ReaderCompletionState LoadReaderCompletionState(string root)
        {
            var state = new ReaderCompletionState();

            foreach (string processFile in EnumerateReaderProcessMarkdownFiles(root))
            {
                string currentBookName = null;

                try
                {
                    foreach (string line in File.ReadLines(processFile, Encoding.UTF8))
                    {
                        if (line.StartsWith("Book:", StringComparison.OrdinalIgnoreCase))
                        {
                            currentBookName = line.Substring("Book:".Length).Trim();
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(currentBookName) || !line.StartsWith("|", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (line.StartsWith("| No.", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("| :---", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string[] cells = line.Split('|');
                        if (cells.Length < 4)
                        {
                            continue;
                        }

                        string status = cells.Length > 2 ? cells[2].Trim() : string.Empty;
                        if (!string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string chapterName = cells.Length > 3 ? cells[3].Trim() : string.Empty;
                        if (string.IsNullOrWhiteSpace(chapterName))
                        {
                            continue;
                        }

                        state.AddCompletedChapter(currentBookName, chapterName);
                    }
                }
                catch
                {
                }
            }

            return state;
        }

        private IEnumerable<string> EnumerateReaderProcessMarkdownFiles(string root)
        {
            string tempProcessRoot = Path.Combine(PortablePaths.PortableTempRoot, ".process");
            foreach (string path in SafeGetFiles(tempProcessRoot, "*.md", SearchOption.AllDirectories))
            {
                yield return path;
            }

            foreach (string domainFolder in SafeGetDirectories(root))
            {
                string legacyProcessFolder = Path.Combine(domainFolder, ".process");
                foreach (string path in SafeGetFiles(legacyProcessFolder, "*.md", SearchOption.TopDirectoryOnly))
                {
                    yield return path;
                }
            }
        }

        private static string NormalizeReaderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim();
            }
        }

        private static string NormalizeReaderCompletionKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            foreach (char ch in value.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        private sealed class ReaderCompletionState
        {
            private readonly Dictionary<string, HashSet<string>> _completedChaptersByBook = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            public void AddCompletedChapter(string bookName, string chapterName)
            {
                string bookKey = NormalizeReaderCompletionKey(bookName);
                string chapterKey = NormalizeReaderCompletionKey(chapterName);

                if (string.IsNullOrWhiteSpace(bookKey) || string.IsNullOrWhiteSpace(chapterKey))
                {
                    return;
                }

                if (!_completedChaptersByBook.TryGetValue(bookKey, out HashSet<string> completedChapters))
                {
                    completedChapters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _completedChaptersByBook[bookKey] = completedChapters;
                }

                completedChapters.Add(chapterKey);
            }

            public void ApplyCompletionState(string bookName, IList<ReaderChapterItem> chapters)
            {
                if (chapters == null || chapters.Count == 0)
                {
                    return;
                }

                string bookKey = NormalizeReaderCompletionKey(bookName);
                if (string.IsNullOrWhiteSpace(bookKey) || !_completedChaptersByBook.TryGetValue(bookKey, out HashSet<string> completedChapters))
                {
                    return;
                }

                foreach (ReaderChapterItem chapter in chapters)
                {
                    if (chapter == null)
                    {
                        continue;
                    }

                    chapter.IsCompleted = completedChapters.Contains(NormalizeReaderCompletionKey(chapter.Name));
                }

                bool allCompleted = chapters.All(chapter => chapter != null && chapter.IsCompleted);
                if (allCompleted)
                {
                    foreach (ReaderChapterItem chapter in chapters)
                    {
                        if (chapter != null)
                        {
                            chapter.IsCompleted = true;
                        }
                    }
                }
            }
        }

        private void ReaderDomainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_readerSelectionGuard || !(_readerDomainList?.SelectedItem is ReaderDomainItem domain))
            {
                return;
            }

            OpenReaderDomain(domain, keepBookSelection: false);
        }

        private void ReaderMangaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_readerSelectionGuard || !(_readerMangaList.SelectedItem is ReaderMangaItem manga))
            {
                return;
            }

            OpenReaderManga(manga, keepChapterSelection: false);
        }

        private void ReaderChapterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_readerSelectionGuard || !(_readerChapterList.SelectedItem is ReaderChapterItem chapter))
            {
                return;
            }

            SelectReaderChapter(chapter);
        }

        private void ReaderFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_readerSelectionGuard || !(_readerFileList?.SelectedItem is ReaderPageItem page))
            {
                return;
            }

            OpenReaderPage(page, launchFastStone: false);
        }

        private void ReaderNovelDomainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_readerSelectionGuard || !(_readerNovelDomainList?.SelectedItem is ReaderNovelDomainItem domain))
            {
                return;
            }

            OpenReaderNovelDomain(domain, keepBookSelection: false);
        }

        private void ReaderNovelBookList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_readerSelectionGuard || !(_readerNovelBookList?.SelectedItem is ReaderNovelBookItem book))
            {
                return;
            }

            OpenReaderNovelBook(book, keepChapterSelection: false);
        }

        private void ReaderNovelChapterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_readerSelectionGuard || !(_readerNovelChapterList?.SelectedItem is ReaderNovelChapterItem chapter))
            {
                return;
            }

            SelectReaderNovelChapter(chapter);
        }

        private void ReaderNovelFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_readerSelectionGuard || !(_readerNovelFileList?.SelectedItem is ReaderMarkdownItem file))
            {
                return;
            }

            OpenReaderNovelFile(file, openInBrowser: false);
        }

        private void ReaderFileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(_readerFileList?.SelectedItem is ReaderPageItem page) || !IsSupportedReaderImageFile(page.FilePath))
            {
                return;
            }

            OpenReaderPage(page, launchFastStone: true);
        }

        private void ReaderNovelFileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(_readerNovelFileList?.SelectedItem is ReaderMarkdownItem file) || !IsMarkdownFile(file.FilePath))
            {
                return;
            }

            OpenReaderNovelFile(file, openInBrowser: true);
        }

        private async void OpenRootReaderFolder_Click(object sender, RoutedEventArgs e)
        {
            _readerLibraryRootOverride = null;
            await RefreshReaderLibraryAsync(forceRefresh: true);
        }

        private async void OpenRootReaderNovelFolder_Click(object sender, RoutedEventArgs e)
        {
            _readerNovelLibraryRootOverride = null;
            await RefreshReaderNovelLibraryAsync(forceRefresh: true);
        }

        private async void OpenOtherReaderFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowser
            {
                SelectedPath = GetCurrentReaderLibraryRoot(),
                Title = _isVietnameseUi ? "Chọn thư mục khác cho Watch" : "Choose another Watch folder"
            };

            if (!dialog.ShowDialog(new WindowInteropHelper(this).Handle))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(dialog.SelectedPath) || !Directory.Exists(dialog.SelectedPath))
            {
                UpdateReaderStatus(_isVietnameseUi ? "Thư mục đã chọn không hợp lệ." : "Selected folder is invalid.");
                return;
            }

            _readerLibraryRootOverride = dialog.SelectedPath;
            await RefreshReaderLibraryWhenIdleAsync(forceRefresh: true);
        }

        private async void OpenOtherReaderNovelFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowser
            {
                SelectedPath = GetCurrentReaderNovelLibraryRoot(),
                Title = _isVietnameseUi ? "Chọn thư mục khác cho Watch novel" : "Choose another Watch novel folder"
            };

            if (!dialog.ShowDialog(new WindowInteropHelper(this).Handle))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(dialog.SelectedPath) || !Directory.Exists(dialog.SelectedPath))
            {
                UpdateReaderNovelStatus(_isVietnameseUi ? "Thư mục đã chọn không hợp lệ." : "Selected folder is invalid.");
                return;
            }

            _readerNovelLibraryRootOverride = dialog.SelectedPath;
            await RefreshReaderNovelLibraryWhenIdleAsync(forceRefresh: true);
        }

        private void InstallMdReader_Click(object sender, RoutedEventArgs e)
        {
            const string mdReaderUrl = "https://chromewebstore.google.com/detail/markdown-reader/medapdbncneneejhbgcjceippjlfkmkg";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = mdReaderUrl,
                    UseShellExecute = true
                });
                UpdateReaderNovelStatus(_isVietnameseUi ? "Đã mở link cài MD Reader." : "Opened MD Reader install link.");
            }
            catch (Exception ex)
            {
                UpdateReaderNovelStatus((_isVietnameseUi ? "Không thể mở link MD Reader: " : "Failed to open MD Reader link: ") + ex.Message);
            }
        }

        private void OpenReaderManga(ReaderMangaItem manga, bool keepChapterSelection)
        {
            if (manga == null)
            {
                return;
            }

            if (manga.IsNavigationItem && !string.IsNullOrWhiteSpace(manga.NavigationTargetFolderPath))
            {
                _readerLibraryRootOverride = manga.NavigationTargetFolderPath;
                RefreshReaderLibraryIfNeeded(forceRefresh: true);
                return;
            }

            if (manga.Chapters != null)
            {
                foreach (var chapter in manga.Chapters)
                {
                    EnsureChapterPagesLoaded(chapter);
                }
            }

            _currentReaderManga = manga;
            _currentReaderDomain = _readerDomains.FirstOrDefault(item =>
                item.Books.Any(book => string.Equals(book.FolderPath, manga.FolderPath, StringComparison.OrdinalIgnoreCase)));
            if (_currentReaderDomain != null)
            {
                SyncReaderDomainListSelection(_currentReaderDomain);
                UpdateReaderBookListItems(_currentReaderDomain.Books);
            }
            _readerSelectionGuard = true;
            _readerMangaList.SelectedItem = manga;
            ApplyReaderChapterPresentation(manga);
            _readerChapterList.ItemsSource = manga.Chapters;

            ReaderChapterItem nextChapter = keepChapterSelection && _currentReaderChapter != null
                ? manga.Chapters.FirstOrDefault(item => string.Equals(item.FolderPath, _currentReaderChapter.FolderPath, StringComparison.OrdinalIgnoreCase))
                : manga.Chapters.FirstOrDefault();

            _readerChapterList.SelectedItem = nextChapter;
            if (nextChapter != null)
            {
                UpdateReaderFileListItems(nextChapter.Pages);
                ScrollReaderChapterIntoView(nextChapter);
            }
            else
            {
                ClearReaderFileList();
            }
            _readerSelectionGuard = false;
            _currentReaderChapter = nextChapter;
            _currentReaderPage = null;
            SetReaderCurrentTitle();
            RenderReaderPlaceholder();
            UpdateReaderStatus(_isVietnameseUi
                ? $"Chọn chapter hoặc ảnh trong panel bên trái để mở {GetReaderWatchCurrentAppDisplayName()}."
                : $"Pick a chapter or an image in the left panels to open {GetReaderWatchCurrentAppDisplayName()}.");
            UpdateReaderNavigationState();
        }

        private void OpenReaderDomain(ReaderDomainItem domain, bool keepBookSelection)
        {
            if (domain == null)
            {
                return;
            }

            _currentReaderDomain = domain;
            _readerSelectionGuard = true;
            SyncReaderDomainListSelection(domain);
            UpdateReaderBookListItems(domain.Books);

            ReaderMangaItem nextBook = keepBookSelection && _currentReaderManga != null
                ? domain.Books.FirstOrDefault(item => string.Equals(item.FolderPath, _currentReaderManga.FolderPath, StringComparison.OrdinalIgnoreCase))
                : domain.Books.FirstOrDefault();

            _readerSelectionGuard = false;

            if (nextBook == null)
            {
                ClearReaderBookList();
                ClearReaderChapterList();
                ClearReaderFileList();
                _currentReaderManga = null;
                _currentReaderChapter = null;
                _currentReaderPage = null;
                RenderReaderPlaceholder();
                UpdateReaderNavigationState();
                return;
            }

            OpenReaderManga(nextBook, keepChapterSelection: keepBookSelection);
        }

        private void OpenReaderNovelDomain(ReaderNovelDomainItem domain, bool keepBookSelection)
        {
            if (domain == null)
            {
                return;
            }

            _currentReaderNovelDomain = domain;
            _readerSelectionGuard = true;
            SyncReaderNovelDomainListSelection(domain);
            UpdateReaderNovelBookListItems(domain.Books);

            ReaderNovelBookItem nextBook = keepBookSelection && _currentReaderNovelBook != null
                ? domain.Books.FirstOrDefault(item => string.Equals(item.FolderPath, _currentReaderNovelBook.FolderPath, StringComparison.OrdinalIgnoreCase))
                : domain.Books.FirstOrDefault();

            _readerSelectionGuard = false;

            if (nextBook == null)
            {
                ClearReaderNovelBookList();
                ClearReaderNovelChapterList();
                ClearReaderNovelFileList();
                _currentReaderNovelBook = null;
                _currentReaderNovelChapter = null;
                _currentReaderNovelFile = null;
                return;
            }

            OpenReaderNovelBook(nextBook, keepChapterSelection: keepBookSelection);
        }

        private void OpenReaderNovelBook(ReaderNovelBookItem book, bool keepChapterSelection)
        {
            if (book == null)
            {
                return;
            }

            if (book.IsNavigationItem && !string.IsNullOrWhiteSpace(book.NavigationTargetFolderPath))
            {
                _readerNovelLibraryRootOverride = book.NavigationTargetFolderPath;
                RefreshReaderNovelLibraryIfNeeded(forceRefresh: true);
                return;
            }

            if (book.Chapters != null)
            {
                foreach (var chapter in book.Chapters)
                {
                    EnsureNovelChapterFilesLoaded(chapter);
                }
            }

            _currentReaderNovelBook = book;
            _currentReaderNovelDomain = _readerNovelDomains.FirstOrDefault(item =>
                item.Books.Any(candidate => string.Equals(candidate.FolderPath, book.FolderPath, StringComparison.OrdinalIgnoreCase)));
            if (_currentReaderNovelDomain != null)
            {
                SyncReaderNovelDomainListSelection(_currentReaderNovelDomain);
                UpdateReaderNovelBookListItems(_currentReaderNovelDomain.Books);
            }

            _readerSelectionGuard = true;
            _readerNovelBookList.SelectedItem = book;
            _readerNovelChapterList.ItemsSource = book.Chapters;

            ReaderNovelChapterItem nextChapter = keepChapterSelection && _currentReaderNovelChapter != null
                ? book.Chapters.FirstOrDefault(item => string.Equals(item.FolderPath, _currentReaderNovelChapter.FolderPath, StringComparison.OrdinalIgnoreCase))
                : book.Chapters.FirstOrDefault();

            _readerNovelChapterList.SelectedItem = nextChapter;
            if (nextChapter != null)
            {
                UpdateReaderNovelFileListItems(nextChapter.Files);
                ScrollReaderNovelChapterIntoView(nextChapter);
            }
            else
            {
                ClearReaderNovelFileList();
            }
            _readerSelectionGuard = false;
            _currentReaderNovelChapter = null;
            _currentReaderNovelFile = null;
            SetReaderNovelCurrentTitle();
            _readerNovelPreviewTextBox.Text = string.Empty;
            if (nextChapter != null)
            {
                SelectReaderNovelChapter(nextChapter);
            }
            else
            {
                _currentReaderNovelChapter = null;
                _currentReaderNovelFile = null;
                UpdateReaderNovelStatus(_isVietnameseUi
                    ? "Chọn chapter hoặc file .md để mở."
                    : "Choose a chapter or .md file to open.");
            }
        }

        private void SelectReaderNovelChapter(ReaderNovelChapterItem chapter)
        {
            if (chapter == null)
            {
                return;
            }

            _currentReaderNovelChapter = chapter;
            _currentReaderNovelFile = null;
            _readerSelectionGuard = true;
            _readerNovelChapterList.SelectedItem = chapter;
            UpdateReaderNovelFileListItems(chapter.Files);
            _readerNovelFileList.SelectedItem = null;
            _readerSelectionGuard = false;
            ScrollReaderNovelChapterIntoView(chapter);
            SetReaderNovelCurrentTitle();
            _readerNovelPreviewTextBox.Text = string.Empty;
            UpdateReaderNovelStatus(chapter.Files.Count > 0
                ? (_isVietnameseUi ? "Đã chọn chapter. Click file .md để xem preview, double-click để mở trình duyệt." : "Chapter selected. Click .md for preview, double-click to open in browser.")
                : (_isVietnameseUi ? "Chapter này chưa có file .md." : "This chapter has no .md file."));
        }

        private void OpenReaderNovelChapter(ReaderNovelChapterItem chapter)
        {
            if (chapter == null)
            {
                return;
            }

            _currentReaderNovelChapter = chapter;
            _readerSelectionGuard = true;
            _readerNovelChapterList.SelectedItem = chapter;
            UpdateReaderNovelFileListItems(chapter.Files);
            ReaderMarkdownItem firstFile = chapter.Files.FirstOrDefault();
            _readerNovelFileList.SelectedItem = firstFile;
            _readerSelectionGuard = false;
            ScrollReaderNovelChapterIntoView(chapter);
            SetReaderNovelCurrentTitle();
            if (firstFile != null)
            {
                OpenReaderNovelFile(firstFile, openInBrowser: false);
            }
            else
            {
                _currentReaderNovelFile = null;
                _readerNovelPreviewTextBox.Text = string.Empty;
                UpdateReaderNovelStatus(_isVietnameseUi
                    ? "Chapter này chưa có file .md."
                    : "This chapter has no .md file.");
            }
        }

        private void OpenReaderNovelFile(ReaderMarkdownItem file, bool openInBrowser)
        {
            if (file == null)
            {
                return;
            }

            _currentReaderNovelFile = file;
            _readerSelectionGuard = true;
            SyncReaderNovelFileListSelection(file);
            _readerSelectionGuard = false;
            SetReaderNovelCurrentTitle();
            if (string.IsNullOrWhiteSpace(file.FilePath) || !File.Exists(file.FilePath))
            {
                _readerNovelPreviewTextBox.Text = string.Empty;
                UpdateReaderNovelStatus(_isVietnameseUi ? "File .md không còn tồn tại." : ".md file no longer exists.");
                return;
            }

            try
            {
                _readerNovelPreviewTextBox.Text = File.ReadAllText(file.FilePath, Encoding.UTF8);
                _readerNovelPreviewTextBox.ScrollToHome();
                if (!openInBrowser)
                {
                    UpdateReaderNovelStatus((_isVietnameseUi ? "Đã chọn file .md. Double-click để mở trình duyệt: " : "Selected .md file. Double-click to open in browser: ") + file.Name);
                    return;
                }
                TryOpenReaderNovelFileInBrowser(file.FilePath, out string browserError);
                UpdateReaderNovelStatus(string.IsNullOrWhiteSpace(browserError)
                    ? ((_isVietnameseUi ? "Đã mở .md bằng trình duyệt: " : "Opened .md in browser: ") + file.Name)
                    : ((_isVietnameseUi ? "Đã nạp preview nhưng mở trình duyệt lỗi: " : "Preview loaded but browser open failed: ") + browserError));
            }
            catch (Exception ex)
            {
                _readerNovelPreviewTextBox.Text = string.Empty;
                UpdateReaderNovelStatus((_isVietnameseUi ? "Không thể đọc file .md: " : "Failed to read .md file: ") + ex.Message);
            }
        }

        private bool TryOpenReaderNovelFileInBrowser(string filePath, out string errorMessage)
        {
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                errorMessage = _isVietnameseUi ? "File .md không còn tồn tại." : ".md file no longer exists.";
                return false;
            }

            try
            {
                var fileUri = new Uri(Path.GetFullPath(filePath)).AbsoluteUri;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileUri,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private void OpenReaderMangaChapterPage(ReaderMangaItem manga, ReaderChapterItem chapter, int pageIndex)
        {
            if (manga == null || chapter == null || chapter.Pages == null || chapter.Pages.Count == 0)
            {
                return;
            }

            int safePageIndex = Math.Max(0, Math.Min(chapter.Pages.Count - 1, pageIndex));

            _currentReaderManga = manga;
            _currentReaderDomain = _readerDomains.FirstOrDefault(item =>
                item.Books.Any(book => string.Equals(book.FolderPath, manga.FolderPath, StringComparison.OrdinalIgnoreCase)));
            if (_currentReaderDomain != null)
            {
                SyncReaderDomainListSelection(_currentReaderDomain);
                UpdateReaderBookListItems(_currentReaderDomain.Books);
            }
            _currentReaderChapter = chapter;
            _currentReaderPage = chapter.Pages[safePageIndex];

            _readerSelectionGuard = true;
            _readerMangaList.SelectedItem = manga;
            ApplyReaderChapterPresentation(manga);
            _readerChapterList.ItemsSource = manga.Chapters;
            _readerChapterList.SelectedItem = chapter;
            UpdateReaderFileListItems(chapter.Pages);
            SyncReaderFileListSelection(_currentReaderPage);
            _readerSelectionGuard = false;

            SetReaderCurrentTitle();
            _forceReaderRenderOnNextPageOpen = true;
            if (_readerUsesFastStone)
            {
                RenderReaderPlaceholder();
                UpdateReaderStatus(_isVietnameseUi
                    ? $"Đã chọn chapter. Chỉ click page mới mở {GetReaderWatchCurrentAppDisplayName()}."
                    : $"Chapter selected. Only page click opens {GetReaderWatchCurrentAppDisplayName()}.");
            }
            else
            {
                RenderReaderPage();
                UpdateReaderStatus(BuildReaderStatusText());
            }
            UpdateReaderNavigationState();
        }

        private void OpenReaderChapter(ReaderChapterItem chapter, int pageIndex)
        {
            if (chapter == null)
            {
                return;
            }

            _currentReaderChapter = chapter;
            _readerSelectionGuard = true;
            _readerChapterList.SelectedItem = chapter;
            UpdateReaderFileListItems(chapter.Pages);
            _readerSelectionGuard = false;
            ScrollReaderChapterIntoView(chapter);

            int safePageIndex = Math.Max(0, Math.Min(chapter.Pages.Count - 1, pageIndex));
            _forceReaderRenderOnNextPageOpen = true;
            OpenReaderPage(chapter.Pages[safePageIndex], launchFastStone: false);
        }

        private void SelectReaderChapter(ReaderChapterItem chapter)
        {
            if (chapter == null)
            {
                return;
            }

            _currentReaderChapter = chapter;
            _currentReaderPage = null;
            _readerSelectionGuard = true;
            _readerChapterList.SelectedItem = chapter;
            UpdateReaderFileListItems(chapter.Pages);
            _readerFileList.SelectedItem = null;
            _readerSelectionGuard = false;
            ScrollReaderChapterIntoView(chapter);
            SetReaderCurrentTitle();
            RenderReaderPlaceholder();
            UpdateReaderStatus(chapter.Pages.Count > 0
                ? (_isVietnameseUi ? $"Đã chọn chapter. Double-click ảnh để mở bằng {GetReaderWatchCurrentAppDisplayName()}." : $"Chapter selected. Double-click image to open in {GetReaderWatchCurrentAppDisplayName()}.")
                : (_isVietnameseUi ? "Chapter này chưa có ảnh." : "This chapter has no images."));
            UpdateReaderNavigationState();
        }

        private void OpenReaderPage(ReaderPageItem page, bool launchFastStone = true)
        {
            if (page == null)
            {
                return;
            }

            _currentReaderPage = page;
            _readerSelectionGuard = true;
            SyncReaderFileListSelection(page);
            _readerSelectionGuard = false;

            SetReaderCurrentTitle();
            if (_readerUsesFastStone)
            {
                if (launchFastStone && _readerHasUserClickedInWatch && !_readerSuppressAutoLaunch)
                {
                    RenderReaderPlaceholder();
                    LaunchCurrentReaderTargetInFastStone(preferChapterFolder: false);
                }
                else
                {
                    RenderReaderPlaceholder();
                    UpdateReaderStatus(_isVietnameseUi
                        ? $"Chọn page để mở {GetReaderWatchCurrentAppDisplayName()}."
                        : $"Select a page to open {GetReaderWatchCurrentAppDisplayName()}.");
                }
            }
            else
            {
                RenderReaderPage();
                UpdateReaderStatus(BuildReaderStatusText());
            }
            UpdateReaderNavigationState();
        }

        private static bool IsMarkdownFile(string filePath)
        {
            return !string.IsNullOrWhiteSpace(filePath) &&
                   string.Equals(Path.GetExtension(filePath), ".md", StringComparison.OrdinalIgnoreCase);
        }

        private void SyncCurrentReaderPageFromIndex(int pageIndex)
        {
            if (_currentReaderChapter == null || _currentReaderChapter.Pages == null || _currentReaderChapter.Pages.Count == 0)
            {
                return;
            }

            int safePageIndex = Math.Max(0, Math.Min(_currentReaderChapter.Pages.Count - 1, pageIndex));
            ReaderPageItem page = _currentReaderChapter.Pages[safePageIndex];
            if (page == null)
            {
                return;
            }

            _currentReaderPage = page;
            _readerSelectionGuard = true;
            _readerSelectionGuard = false;
            SyncReaderFileListSelection(page);

            SetReaderCurrentTitle();
            UpdateReaderNavigationState();
            UpdateReaderStatus(BuildReaderStatusText());
        }

        internal void OpenReaderPageByIndex(int pageIndex)
        {
            if (_currentReaderChapter == null || _currentReaderChapter.Pages == null || _currentReaderChapter.Pages.Count == 0)
            {
                return;
            }

            int safePageIndex = Math.Max(0, Math.Min(_currentReaderChapter.Pages.Count - 1, pageIndex));
            ReaderPageItem page = _currentReaderChapter.Pages[safePageIndex];
            if (page == null)
            {
                return;
            }

            OpenReaderPage(page);
        }

        internal void SyncReaderPageSelectionByIndex(int pageIndex)
        {
            SyncCurrentReaderPageFromIndex(pageIndex);
        }

        private string BuildReaderStatusText()
        {
            if (_currentReaderManga == null || _currentReaderChapter == null || _currentReaderPage == null)
            {
                return _isVietnameseUi ? "Chưa mở trang nào." : "No page is currently open.";
            }

            int chapterIndex = _currentReaderManga.Chapters.FindIndex(item => ReferenceEquals(item, _currentReaderChapter)) + 1;
            return _isVietnameseUi
                ? $"{_currentReaderManga.Name} - chap {chapterIndex}/{_currentReaderManga.Chapters.Count} - trang {_currentReaderPage.Index + 1}/{_currentReaderChapter.Pages.Count}"
                : $"{_currentReaderManga.Name} - chapter {chapterIndex}/{_currentReaderManga.Chapters.Count} - page {_currentReaderPage.Index + 1}/{_currentReaderChapter.Pages.Count}";
        }

        private void UpdateReaderStatus(string text)
        {
            if (_readerStatusText != null)
            {
                _readerStatusText.Text = text;
            }
        }

        private void UpdateReaderWatchLoadProgress(ProgressBar progressBar, TextBlock progressText, int current, int total)
        {
            if (progressBar == null)
            {
                return;
            }

            int safeTotal = Math.Max(1, total);
            int safeCurrent = Math.Max(0, Math.Min(current, safeTotal));
            double percent = (safeCurrent * 100d) / safeTotal;

            progressBar.Visibility = Visibility.Visible;
            progressBar.Minimum = 0;
            progressBar.Maximum = 100;
            progressBar.Value = percent;

            if (progressText != null)
            {
                progressText.Text = $"{percent:0}%";
                progressText.Visibility = Visibility.Visible;
            }
        }

        private void HideReaderWatchLoadProgress(ProgressBar progressBar, TextBlock progressText)
        {
            if (progressBar == null)
            {
                return;
            }

            progressBar.Value = 0;
            progressBar.Visibility = Visibility.Collapsed;
            if (progressText != null)
            {
                progressText.Text = "0%";
                progressText.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateReaderNovelStatus(string text)
        {
            if (_readerNovelStatusText != null)
            {
                _readerNovelStatusText.Text = text;
            }
        }

        private void RenderReaderPage()
        {
            if (_currentReaderPage == null)
            {
                RenderReaderPlaceholder();
                return;
            }

            bool isScrollMode = IsReaderScrollMode();
            bool canScrollSyncExistingChapter = isScrollMode &&
                                               !_forceReaderRenderOnNextPageOpen &&
                                               _currentReaderChapter != null &&
                                               string.Equals(_lastRenderedReaderChapterPath, _currentReaderChapter.FolderPath, StringComparison.OrdinalIgnoreCase) &&
                                               _lastRenderedReaderFitMode == _readerFitMode;

            if (canScrollSyncExistingChapter)
            {
                ScrollReaderViewportToPage(_currentReaderPage.Index);
            }
            else
            {
                RenderReaderNativeContent();
            }

            if (_fullscreenWindow != null)
            {
                _fullscreenWindow.RenderPage();
            }
        }

        private void RenderReaderPlaceholder()
        {
            if (_readerStagePanel == null)
            {
                return;
            }

            _readerPageElements.Clear();
            _readerStagePanel.Children.Clear();
            _readerStagePanel.Orientation = Orientation.Vertical;
            _readerStagePanel.FlowDirection = FlowDirection.LeftToRight;

            string message = _isVietnameseUi
                ? $"Chọn domain, book, chapter, hoặc ảnh ở cột trái.\nClick chapter hoặc ảnh để mở {GetReaderWatchCurrentAppDisplayName()}."
                : $"Pick a domain, book, chapter, or image from the left.\nClick a chapter or image to open {GetReaderWatchCurrentAppDisplayName()}.";
            if (_readerUsesFastStone)
            {
                message = _isVietnameseUi
                    ? $"Watch dùng {GetReaderWatchCurrentAppDisplayName()}.\nChỉ click chapter hoặc ảnh để mở."
                    : $"Watch uses {GetReaderWatchCurrentAppDisplayName()}.\nOnly click a chapter or image to open it.";
            }
            _readerStagePanel.Children.Add(new Border
            {
                Padding = new Thickness(24),
                Child = new TextBlock
                {
                    Text = message,
                    Foreground = (Brush)TryFindResource("CyberpunkTextBrush"),
                    TextAlignment = System.Windows.TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460
                }
            });
            _readerZoom = 1d;
            ApplyReaderZoom();
            UpdateReaderViewportAlignment();
        }

        private void RenderReaderNativeContent()
        {
            if (_readerStagePanel == null || _currentReaderPage == null)
            {
                return;
            }

            _readerPageElements.Clear();
            _readerStagePanel.Children.Clear();

            bool isScrollMode = IsReaderScrollMode();
            bool isSinglePageMode = !isScrollMode || _currentReaderChapter == null;
            _readerStagePanel.Orientation = isSinglePageMode || _readerFitMode == ReaderFitMode.VerticalScroll
                ? Orientation.Vertical
                : Orientation.Horizontal;
            _readerStagePanel.FlowDirection = _readerFitMode == ReaderFitMode.HorizontalScrollRTL
                ? FlowDirection.LeftToRight
                : FlowDirection.LeftToRight;

            IEnumerable<ReaderPageItem> pagesToRender;
            if (isSinglePageMode)
            {
                pagesToRender = new[] { _currentReaderPage };
            }
            else if (_readerFitMode == ReaderFitMode.HorizontalScrollRTL)
            {
                pagesToRender = _currentReaderChapter.Pages.OrderByDescending(item => item.Index);
            }
            else
            {
                pagesToRender = _currentReaderChapter.Pages;
            }

            foreach (ReaderPageItem page in pagesToRender)
            {
                FrameworkElement pageElement = CreateReaderPageElement(page, isSinglePageMode);
                _readerPageElements.Add(pageElement);
                _readerStagePanel.Children.Add(pageElement);
            }

            _forceReaderRenderOnNextPageOpen = false;
            _lastRenderedReaderChapterPath = _currentReaderChapter != null ? _currentReaderChapter.FolderPath : null;
            _lastRenderedReaderFitMode = _readerFitMode;
            ApplyReaderZoom();
            UpdateReaderViewportAlignment();
            _readerScrollViewer?.UpdateLayout();
            ScrollReaderViewportToPage(_currentReaderPage.Index);
            _readerScrollViewer?.Focus();
        }

        private FrameworkElement CreateReaderPageElement(ReaderPageItem page, bool isSinglePageMode)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(page.FilePath);
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                Tag = page.Index
            };

            if (_readerFitMode == ReaderFitMode.FitHeight)
            {
                image.Height = Math.Max(120, (_readerScrollViewer?.ViewportHeight ?? 0) - 36);
            }
            else if (_readerFitMode == ReaderFitMode.ActualSize)
            {
                image.Stretch = Stretch.None;
            }
            else if (_readerFitMode == ReaderFitMode.FitWidth || _readerFitMode == ReaderFitMode.VerticalScroll)
            {
                double width = Math.Max(160, (_readerScrollViewer?.ViewportWidth ?? 0) - 40);
                image.Width = width;
            }
            else
            {
                double viewportWidth = _readerScrollViewer?.ViewportWidth ?? 0;
                image.Width = Math.Max(160, (viewportWidth - 72) / 2d);
            }

            var border = new Border
            {
                Margin = _readerFitMode == ReaderFitMode.VerticalScroll
                    ? new Thickness(0, 0, 0, 14)
                    : new Thickness(0, 0, 14, 0),
                Child = image,
                Tag = page.Index
            };

            if (isSinglePageMode)
            {
                border.Margin = new Thickness(0);
            }

            return border;
        }

        private void ApplyReaderZoom()
        {
            _readerScaleTransform.ScaleX = _readerZoom;
            _readerScaleTransform.ScaleY = _readerZoom;
            UpdateReaderViewportAlignment();
        }

        private void SetReaderZoom(double zoom, bool keepCurrentPageVisible)
        {
            double previousZoom = _readerZoom;
            _readerZoom = Math.Max(0.35d, Math.Min(6d, zoom));
            if (Math.Abs(_readerZoom - 1d) < 0.02d)
            {
                _readerZoom = 1d;
            }

            ApplyReaderZoom();
            _readerScrollViewer?.UpdateLayout();
            if (keepCurrentPageVisible && _currentReaderPage != null)
            {
                if (IsReaderScrollMode())
                {
                    PreserveReaderViewportCenterAfterZoom(previousZoom, _readerZoom);
                    UpdateReaderPageFromViewport();
                }
                else
                {
                    ScrollReaderViewportToPage(_currentReaderPage.Index);
                }
            }

            _readerScrollViewer?.Focus();
        }

        private void UpdateReaderViewportAlignment()
        {
            if (_readerStagePanel == null || _readerViewportHost == null || _readerScrollViewer == null)
            {
                return;
            }

            _readerStagePanel.HorizontalAlignment = _readerStagePanel.ActualWidth <= _readerScrollViewer.ViewportWidth
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            _readerStagePanel.VerticalAlignment = _readerStagePanel.ActualHeight <= _readerScrollViewer.ViewportHeight
                ? VerticalAlignment.Center
                : VerticalAlignment.Top;
        }

        private static string BuildReaderPlaceholderHtml(string message)
        {
            return "<html><body style=\"margin:0;background:#0d121f;color:#d7e2ec;font-family:Segoe UI;display:flex;align-items:center;justify-content:center;height:100vh;\">" +
                   "<div style=\"max-width:460px;text-align:center;line-height:1.6;padding:24px;\">" +
                   EscapeHtml(message).Replace("\n", "<br/>") +
                   "</div></body></html>";
        }

        private static string BuildReaderHtml(string pageUri, ReaderFitMode fitMode)
        {
            string containerClass;
            string imageStyle;
            switch (fitMode)
            {
                case ReaderFitMode.FitHeight:
                    containerClass = "fit-height";
                    imageStyle = "height:calc(100vh - 24px); width:auto; max-width:none;";
                    break;
                case ReaderFitMode.ActualSize:
                    containerClass = "actual-size";
                    imageStyle = "width:auto; height:auto; max-width:none;";
                    break;
                default:
                    containerClass = "fit-width";
                    imageStyle = "width:min(100%, 100vw - 24px); height:auto; max-width:none;";
                    break;
            }

            return BuildReaderDocument(
                fitMode,
                $"<div id=\"readerStage\" class=\"reader-stage single-mode {containerClass}\"><img class=\"reader-page\" data-page-index=\"0\" src=\"{EscapeHtml(pageUri)}\" style=\"display:block;{imageStyle}padding:12px 0;\"/></div>",
                0);
        }

        private static string EscapeHtml(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private void UpdateReaderNavigationState()
        {
            bool hasCurrentChapter = _currentReaderChapter != null;
            bool hasCurrentPage = _currentReaderPage != null;

            if (_readerPrevChapterButton != null)
            {
                _readerPrevChapterButton.IsEnabled = hasCurrentChapter && GetAdjacentChapter(-1) != null;
            }

            if (_readerNextChapterButton != null)
            {
                _readerNextChapterButton.IsEnabled = hasCurrentChapter && GetAdjacentChapter(1) != null;
            }

            if (_readerPrevPageButton != null)
            {
                _readerPrevPageButton.IsEnabled = hasCurrentPage && (GetAdjacentPage(-1) != null || GetAdjacentChapter(-1) != null);
            }

            if (_readerNextPageButton != null)
            {
                _readerNextPageButton.IsEnabled = hasCurrentPage && (GetAdjacentPage(1) != null || GetAdjacentChapter(1) != null);
            }

            if (_readerFullscreenButton != null)
            {
                _readerFullscreenButton.IsEnabled = !_readerUsesFastStone && (hasCurrentPage || hasCurrentChapter);
            }

            UpdateReaderFitButtons();
        }

        private void UpdateReaderFitButtons()
        {
            if (_readerFitCombo == null)
            {
                return;
            }
            if (_readerUsesFastStone)
            {
                if (_readerFitCombo.SelectedIndex != 0)
                {
                    _readerFitCombo.SelectedIndex = 0;
                }
                return;
            }
            int index = 0;
            switch (_readerFitMode)
            {
                case ReaderFitMode.FitWidth:
                    index = 0;
                    break;
                case ReaderFitMode.FitHeight:
                    index = 1;
                    break;
                case ReaderFitMode.ActualSize:
                    index = 2;
                    break;
                case ReaderFitMode.VerticalScroll:
                    index = 3;
                    break;
                case ReaderFitMode.HorizontalScrollLTR:
                    index = 4;
                    break;
                case ReaderFitMode.HorizontalScrollRTL:
                    index = 5;
                    break;
            }
            if (_readerFitCombo.SelectedIndex != index)
            {
                _readerFitCombo.SelectedIndex = index;
            }
        }

        private ReaderPageItem GetAdjacentPage(int direction)
        {
            if (_currentReaderChapter == null || _currentReaderPage == null)
            {
                return null;
            }

            int nextIndex = _currentReaderPage.Index + direction;
            if (nextIndex < 0 || nextIndex >= _currentReaderChapter.Pages.Count)
            {
                return null;
            }

            return _currentReaderChapter.Pages[nextIndex];
        }

        private ReaderChapterItem GetAdjacentChapter(int direction)
        {
            if (_currentReaderManga == null || _currentReaderChapter == null)
            {
                return null;
            }

            int chapterIndex = _currentReaderManga.Chapters.FindIndex(item => ReferenceEquals(item, _currentReaderChapter));
            if (chapterIndex < 0)
            {
                return null;
            }

            int nextIndex = chapterIndex + direction;
            if (nextIndex < 0 || nextIndex >= _currentReaderManga.Chapters.Count)
            {
                return null;
            }

            return _currentReaderManga.Chapters[nextIndex];
        }

        private void ReaderPrevChapter_Click(object sender, RoutedEventArgs e)
        {
            ReaderChapterItem chapter = GetAdjacentChapter(-1);
            if (chapter != null)
            {
                OpenReaderChapter(chapter, 0);
            }
        }

        private void ReaderNextChapter_Click(object sender, RoutedEventArgs e)
        {
            ReaderChapterItem chapter = GetAdjacentChapter(1);
            if (chapter != null)
            {
                OpenReaderChapter(chapter, 0);
            }
        }

        private void ReaderPrevPage_Click(object sender, RoutedEventArgs e)
        {
            MoveReaderPage(-1);
        }

        private void ReaderNextPage_Click(object sender, RoutedEventArgs e)
        {
            MoveReaderPage(1);
        }

        private void MoveReaderPage(int direction)
        {
            ReaderPageItem page = GetAdjacentPage(direction);
            if (page != null)
            {
                OpenReaderPage(page);
                return;
            }

            ReaderChapterItem chapter = GetAdjacentChapter(direction);
            if (chapter == null)
            {
                return;
            }

            int pageIndex = direction < 0 ? Math.Max(0, chapter.Pages.Count - 1) : 0;
            OpenReaderChapter(chapter, pageIndex);
        }

        private void MoveReaderChapter(int direction)
        {
            ReaderChapterItem chapter = GetAdjacentChapter(direction);
            if (chapter == null)
            {
                return;
            }

            OpenReaderChapter(chapter, 0);
        }

        private bool IsReaderHorizontalMode()
        {
            return _readerFitMode == ReaderFitMode.HorizontalScrollLTR ||
                   _readerFitMode == ReaderFitMode.HorizontalScrollRTL;
        }

        private bool IsReaderScrollMode()
        {
            return _readerFitMode == ReaderFitMode.VerticalScroll ||
                   _readerFitMode == ReaderFitMode.HorizontalScrollLTR ||
                   _readerFitMode == ReaderFitMode.HorizontalScrollRTL;
        }

        private void ExecuteReaderViewportScript(string script)
        {
        }

        private void ScrollReaderViewportToBoundary(bool goToStart)
        {
            if (_readerScrollViewer == null)
            {
                return;
            }

            _readerSyncingFromViewport = true;
            try
            {
                if (IsReaderHorizontalMode())
                {
                    double offset = _readerFitMode == ReaderFitMode.HorizontalScrollRTL
                        ? (goToStart ? _readerScrollViewer.ScrollableWidth : 0)
                        : (goToStart ? 0 : _readerScrollViewer.ScrollableWidth);
                    _readerScrollViewer.ScrollToHorizontalOffset(offset);
                }
                else
                {
                    double offset = goToStart ? 0 : _readerScrollViewer.ScrollableHeight;
                    _readerScrollViewer.ScrollToVerticalOffset(offset);
                }
            }
            finally
            {
                _readerSyncingFromViewport = false;
            }

            _fullscreenWindow?.ScrollBoundary(goToStart);
        }

        private void ScrollReaderViewportToPage(int pageIndex)
        {
            if (pageIndex < 0 || _readerScrollViewer == null)
            {
                return;
            }

            FrameworkElement target = _readerPageElements.FirstOrDefault(element => Equals(element.Tag, pageIndex));
            if (target == null)
            {
                _fullscreenWindow?.ScrollToPage(pageIndex);
                return;
            }

            _readerScrollViewer.UpdateLayout();
            Point origin = target.TranslatePoint(new Point(0, 0), _readerStagePanel);

            _readerSyncingFromViewport = true;
            try
            {
                if (_readerFitMode == ReaderFitMode.VerticalScroll)
                {
                    double verticalOffset = GetCenteredScrollOffset(origin.Y, target.ActualHeight, _readerScrollViewer.ViewportHeight, _readerScrollViewer.ScrollableHeight);
                    double horizontalOffset = GetCenteredScrollOffset(origin.X, target.ActualWidth, _readerScrollViewer.ViewportWidth, _readerScrollViewer.ScrollableWidth);
                    _readerScrollViewer.ScrollToVerticalOffset(verticalOffset);
                    _readerScrollViewer.ScrollToHorizontalOffset(horizontalOffset);
                }
                else if (_readerFitMode == ReaderFitMode.HorizontalScrollLTR || _readerFitMode == ReaderFitMode.HorizontalScrollRTL)
                {
                    double horizontalOffset = GetCenteredScrollOffset(origin.X, target.ActualWidth, _readerScrollViewer.ViewportWidth, _readerScrollViewer.ScrollableWidth);
                    double verticalOffset = GetCenteredScrollOffset(origin.Y, target.ActualHeight, _readerScrollViewer.ViewportHeight, _readerScrollViewer.ScrollableHeight);
                    _readerScrollViewer.ScrollToHorizontalOffset(horizontalOffset);
                    _readerScrollViewer.ScrollToVerticalOffset(verticalOffset);
                }
                else
                {
                    target.BringIntoView();
                }
            }
            finally
            {
                _readerSyncingFromViewport = false;
            }

            _readerScrollViewer.Focus();
            _fullscreenWindow?.ScrollToPage(pageIndex);
        }

        private void OpenReaderPageBoundary(bool openLastPage)
        {
            if (_currentReaderChapter == null || _currentReaderChapter.Pages == null || _currentReaderChapter.Pages.Count == 0)
            {
                return;
            }

            int index = openLastPage ? _currentReaderChapter.Pages.Count - 1 : 0;
            OpenReaderPage(_currentReaderChapter.Pages[index]);
        }

        private void AddCurrentReaderPageBookmark()
        {
            if (_currentReaderManga == null || _currentReaderChapter == null || _currentReaderPage == null)
            {
                return;
            }

            _bookmarkManager.AddReaderPageBookmark(new ReaderPageBookmarkEntry
            {
                BookmarkId = Guid.NewGuid().ToString("N"),
                MangaName = _currentReaderManga.Name,
                ChapterName = _currentReaderChapter.Name,
                PageName = _currentReaderPage.Name,
                SourceDomain = _currentReaderManga.SourceGroup ?? "watch",
                BookmarkedAt = DateTime.Now,
                LibraryRoot = GetCurrentReaderLibraryRoot(),
                SeriesFolderPath = ResolveCurrentReaderSeriesFolderPath(),
                MangaFolderPath = _currentReaderManga.FolderPath,
                ChapterFolderPath = _currentReaderChapter.FolderPath,
                PageFilePath = _currentReaderPage.FilePath,
                PageIndex = _currentReaderPage.Index + 1,
                FitModeKey = GetReaderFitModeKey(_readerFitMode),
                ReaderZoom = _readerZoom,
                ViewportPageIndex = GetCurrentViewportPageIndex(),
                ViewportPageXRatio = GetCurrentViewportPageAnchor().X,
                ViewportPageYRatio = GetCurrentViewportPageAnchor().Y
            });

            _bookmarkHistoryWindowInstance?.RefreshReaderPageBookmarks();
            ShowReaderSoftNotification(_isVietnameseUi
                ? $"Đã bookmark trang {_currentReaderPage.Index + 1} của {_currentReaderManga.Name}."
                : $"Bookmarked page {_currentReaderPage.Index + 1} of {_currentReaderManga.Name}.");
        }

        private void ShowReaderSoftNotification(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            UpdateReaderStatus(message);
            _fullscreenWindow?.ShowToast(message);
        }

        private static double GetCenteredScrollOffset(double origin, double elementSize, double viewportSize, double scrollableSize)
        {
            if (viewportSize <= 0)
            {
                return Math.Max(0, origin);
            }

            double offset = origin - Math.Max(0, (viewportSize - elementSize) / 2d);
            if (double.IsNaN(offset) || double.IsInfinity(offset))
            {
                offset = origin;
            }

            return Math.Max(0, Math.Min(scrollableSize, offset));
        }

        private void PreserveReaderViewportCenterAfterZoom(double oldZoom, double newZoom)
        {
            if (_readerScrollViewer == null || oldZoom <= 0 || newZoom <= 0)
            {
                return;
            }

            double centerX = (_readerScrollViewer.HorizontalOffset + (_readerScrollViewer.ViewportWidth / 2d)) / oldZoom;
            double centerY = (_readerScrollViewer.VerticalOffset + (_readerScrollViewer.ViewportHeight / 2d)) / oldZoom;

            _readerSyncingFromViewport = true;
            try
            {
                double nextHorizontalOffset = (centerX * newZoom) - (_readerScrollViewer.ViewportWidth / 2d);
                double nextVerticalOffset = (centerY * newZoom) - (_readerScrollViewer.ViewportHeight / 2d);
                _readerScrollViewer.ScrollToHorizontalOffset(ClampOffset(nextHorizontalOffset, _readerScrollViewer.ScrollableWidth));
                _readerScrollViewer.ScrollToVerticalOffset(ClampOffset(nextVerticalOffset, _readerScrollViewer.ScrollableHeight));
            }
            finally
            {
                _readerSyncingFromViewport = false;
            }
        }

        private void UpdateReaderPageFromViewport()
        {
            int bestIndex = GetPageIndexFromViewportCenter(_readerPageElements, _readerScrollViewer);
            if (bestIndex >= 0)
            {
                SyncCurrentReaderPageFromIndex(bestIndex);
            }
        }

        private void EndReaderMousePan()
        {
            if (!_readerIsMousePanning)
            {
                return;
            }

            _readerIsMousePanning = false;
            if (_readerScrollViewer != null && _readerScrollViewer.IsMouseCaptured)
            {
                _readerScrollViewer.ReleaseMouseCapture();
            }
            Cursor = _readerMousePanPreviousCursor ?? Cursors.Arrow;
            UpdateReaderPageFromViewport();
        }

        internal static int GetPageIndexFromViewportCenter(IReadOnlyList<FrameworkElement> pageElements, ScrollViewer scrollViewer)
        {
            if (pageElements == null || scrollViewer == null || pageElements.Count == 0)
            {
                return -1;
            }

            Point viewportCenter = new Point(scrollViewer.ViewportWidth / 2d, scrollViewer.ViewportHeight / 2d);
            int nearestIndex = -1;
            double nearestDistance = double.MaxValue;

            foreach (FrameworkElement element in pageElements)
            {
                if (element == null || !element.IsVisible)
                {
                    continue;
                }

                Point origin = element.TranslatePoint(new Point(0, 0), scrollViewer);
                Rect elementRect = new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
                if (elementRect.Contains(viewportCenter))
                {
                    return element.Tag is int taggedIndex ? taggedIndex : -1;
                }

                double nearestX = Math.Max(elementRect.Left, Math.Min(viewportCenter.X, elementRect.Right));
                double nearestY = Math.Max(elementRect.Top, Math.Min(viewportCenter.Y, elementRect.Bottom));
                double distance = Math.Pow(nearestX - viewportCenter.X, 2) + Math.Pow(nearestY - viewportCenter.Y, 2);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = element.Tag is int taggedIndex ? taggedIndex : nearestIndex;
                }
            }

            return nearestIndex;
        }

        internal static double ClampOffset(double value, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            return Math.Max(0, Math.Min(max, value));
        }

        private int GetCurrentViewportPageIndex()
        {
            if (!IsReaderScrollMode() || _readerScrollViewer == null || _readerPageElements.Count == 0)
            {
                return _currentReaderPage != null ? _currentReaderPage.Index : 0;
            }

            int pageIndex = GetPageIndexFromViewportCenter(_readerPageElements, _readerScrollViewer);
            return pageIndex >= 0 ? pageIndex : (_currentReaderPage != null ? _currentReaderPage.Index : 0);
        }

        private Point GetCurrentViewportPageAnchor()
        {
            if (!IsReaderScrollMode() || _readerScrollViewer == null || _readerPageElements.Count == 0)
            {
                return new Point(0.5d, 0.5d);
            }

            int pageIndex = GetCurrentViewportPageIndex();
            FrameworkElement target = _readerPageElements.FirstOrDefault(element => Equals(element.Tag, pageIndex));
            if (target == null)
            {
                return new Point(0.5d, 0.5d);
            }

            Point origin = target.TranslatePoint(new Point(0, 0), _readerScrollViewer);
            double centerX = _readerScrollViewer.ViewportWidth / 2d;
            double centerY = _readerScrollViewer.ViewportHeight / 2d;
            double ratioX = target.ActualWidth > 0 ? (centerX - origin.X) / target.ActualWidth : 0.5d;
            double ratioY = target.ActualHeight > 0 ? (centerY - origin.Y) / target.ActualHeight : 0.5d;
            return new Point(
                Math.Max(0d, Math.Min(1d, ratioX)),
                Math.Max(0d, Math.Min(1d, ratioY)));
        }

        private void ApplyReaderBookmarkViewport(ReaderPageBookmarkEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.ReaderZoom > 0)
            {
                _readerZoom = Math.Max(0.35d, Math.Min(6d, entry.ReaderZoom));
                ApplyReaderZoom();
                _readerScrollViewer?.UpdateLayout();
            }

            if (!IsReaderScrollMode())
            {
                ScrollReaderViewportToPage(Math.Max(0, entry.PageIndex - 1));
                return;
            }

            int targetPageIndex = entry.ViewportPageIndex >= 0 ? entry.ViewportPageIndex : Math.Max(0, entry.PageIndex - 1);
            ScrollReaderViewportToAnchor(targetPageIndex, entry.ViewportPageXRatio, entry.ViewportPageYRatio);
            _fullscreenWindow?.ApplyBookmarkViewport(entry);
        }

        private void ScrollReaderViewportToAnchor(int pageIndex, double pageXRatio, double pageYRatio)
        {
            if (_readerScrollViewer == null || pageIndex < 0)
            {
                return;
            }

            FrameworkElement target = _readerPageElements.FirstOrDefault(element => Equals(element.Tag, pageIndex));
            if (target == null)
            {
                return;
            }

            _readerScrollViewer.UpdateLayout();
            Point origin = target.TranslatePoint(new Point(0, 0), _readerStagePanel);
            double targetX = origin.X + (target.ActualWidth * Math.Max(0d, Math.Min(1d, pageXRatio)));
            double targetY = origin.Y + (target.ActualHeight * Math.Max(0d, Math.Min(1d, pageYRatio)));

            _readerSyncingFromViewport = true;
            try
            {
                _readerScrollViewer.ScrollToHorizontalOffset(ClampOffset(targetX - (_readerScrollViewer.ViewportWidth / 2d), _readerScrollViewer.ScrollableWidth));
                _readerScrollViewer.ScrollToVerticalOffset(ClampOffset(targetY - (_readerScrollViewer.ViewportHeight / 2d), _readerScrollViewer.ScrollableHeight));
            }
            finally
            {
                _readerSyncingFromViewport = false;
            }

            _readerScrollViewer.Focus();
        }

        private static string GetReaderFitModeKey(ReaderFitMode fitMode)
        {
            switch (fitMode)
            {
                case ReaderFitMode.FitHeight:
                    return "fit-height";
                case ReaderFitMode.ActualSize:
                    return "actual-size";
                case ReaderFitMode.VerticalScroll:
                    return "vertical-scroll";
                case ReaderFitMode.HorizontalScrollLTR:
                    return "horizontal-ltr";
                case ReaderFitMode.HorizontalScrollRTL:
                    return "horizontal-rtl";
                default:
                    return "fit-width";
            }
        }

        private static bool TryParseReaderFitModeKey(string fitModeKey, out ReaderFitMode fitMode)
        {
            switch ((fitModeKey ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "fit-height":
                    fitMode = ReaderFitMode.FitHeight;
                    return true;
                case "actual-size":
                    fitMode = ReaderFitMode.ActualSize;
                    return true;
                case "vertical-scroll":
                    fitMode = ReaderFitMode.VerticalScroll;
                    return true;
                case "horizontal-ltr":
                    fitMode = ReaderFitMode.HorizontalScrollLTR;
                    return true;
                case "horizontal-rtl":
                    fitMode = ReaderFitMode.HorizontalScrollRTL;
                    return true;
                case "fit-width":
                    fitMode = ReaderFitMode.FitWidth;
                    return true;
                default:
                    fitMode = ReaderFitMode.FitWidth;
                    return false;
            }
        }

        private static string ToJavaScriptString(string value)
        {
            if (value == null)
            {
                return "''";
            }

            return "'" + value
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n") + "'";
        }

        internal async void OpenReaderPageBookmark(ReaderPageBookmarkEntry entry)
        {
            if (entry == null || !ReaderBookmarkHasAnyExistingPath(entry))
            {
                UpdateReaderStatus(_isVietnameseUi ? "Bookmark trang không còn hợp lệ." : "Page bookmark is no longer valid.");
                return;
            }

            SelectAppSection(AppSection.Watch);

            if (!string.IsNullOrWhiteSpace(entry.LibraryRoot) && Directory.Exists(entry.LibraryRoot))
            {
                _readerLibraryRootOverride = entry.LibraryRoot;
                await RefreshReaderLibraryAsync(forceRefresh: true);
            }

            ReaderMangaItem manga = ResolveReaderBookmarkManga(entry, out bool usedChapterOnlyFallback);

            if (manga == null)
            {
                UpdateReaderStatus(_isVietnameseUi ? "Không thể dựng lại manga từ bookmark trang." : "Failed to rebuild manga from page bookmark.");
                return;
            }

            EnsureReaderLibraryContains(manga);

            ReaderChapterItem chapter = manga.Chapters.FirstOrDefault(item =>
                string.Equals(item.FolderPath, entry.ChapterFolderPath, StringComparison.OrdinalIgnoreCase));
            if (chapter == null)
            {
                UpdateReaderStatus(_isVietnameseUi ? "Chapter của bookmark trang không còn tồn tại." : "Page bookmark chapter no longer exists.");
                return;
            }

            int pageIndex = chapter.Pages.FindIndex(item =>
                string.Equals(item.FilePath, entry.PageFilePath, StringComparison.OrdinalIgnoreCase));
            if (pageIndex < 0)
            {
                pageIndex = Math.Max(0, Math.Min(chapter.Pages.Count - 1, entry.PageIndex - 1));
            }

            if (TryParseReaderFitModeKey(entry.FitModeKey, out ReaderFitMode savedFitMode))
            {
                _readerFitMode = savedFitMode;
                UpdateReaderFitButtons();
            }

            OpenReaderMangaChapterPage(manga, chapter, pageIndex);
            ApplyReaderBookmarkViewport(entry);
            if (usedChapterOnlyFallback)
            {
                UpdateReaderStatus(_isVietnameseUi
                    ? $"{BuildReaderStatusText()} - đang fallback về chap đơn."
                    : $"{BuildReaderStatusText()} - using chapter-only fallback.");
            }
        }

        private string ResolveCurrentReaderSeriesFolderPath()
        {
            if (_currentReaderManga == null)
            {
                return null;
            }

            if (_currentReaderChapter == null ||
                !string.Equals(_currentReaderManga.FolderPath, _currentReaderChapter.FolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return _currentReaderManga.FolderPath;
            }

            string parentFolder = GetReaderParentSeriesFolderPath(_currentReaderChapter.FolderPath, _currentReaderManga.SourceGroup);
            return string.IsNullOrWhiteSpace(parentFolder) ? _currentReaderManga.FolderPath : parentFolder;
        }

        private bool ReaderBookmarkHasAnyExistingPath(ReaderPageBookmarkEntry entry)
        {
            return ReaderBookmarkPathExists(entry?.SeriesFolderPath) ||
                   ReaderBookmarkPathExists(entry?.MangaFolderPath) ||
                   ReaderBookmarkPathExists(entry?.ChapterFolderPath) ||
                   ReaderBookmarkPathExists(entry?.PageFilePath);
        }

        private static bool ReaderBookmarkPathExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return Directory.Exists(path) || File.Exists(path);
        }

        private string GetReaderParentSeriesFolderPath(string chapterFolderPath, string sourceGroup)
        {
            if (string.IsNullOrWhiteSpace(chapterFolderPath) || !Directory.Exists(chapterFolderPath))
            {
                return null;
            }

            DirectoryInfo parent = Directory.GetParent(chapterFolderPath);
            if (parent == null || !parent.Exists)
            {
                return null;
            }

            ReaderMangaItem parentManga = BuildReaderMangaItem(parent.FullName, sourceGroup, 0, GetReaderCompletionStateForCurrentRoot(), LoadReaderDownloadWatchState(GetCurrentReaderLibraryRoot()));
            if (parentManga == null)
            {
                return null;
            }

            return parentManga.Chapters.Any(item => string.Equals(item.FolderPath, chapterFolderPath, StringComparison.OrdinalIgnoreCase))
                ? parent.FullName
                : null;
        }

        private ReaderMangaItem ResolveReaderBookmarkManga(ReaderPageBookmarkEntry entry, out bool usedChapterOnlyFallback)
        {
            usedChapterOnlyFallback = false;

            ReaderMangaItem manga = FindReaderBookmarkMangaInLibrary(entry);
            if (manga != null)
            {
                return manga;
            }

            string inferredSeriesFolderPath = GetReaderParentSeriesFolderPath(entry.ChapterFolderPath, entry.SourceDomain);
            foreach (string candidatePath in new[]
            {
                entry.SeriesFolderPath,
                inferredSeriesFolderPath,
                entry.MangaFolderPath,
                entry.ChapterFolderPath
            })
            {
                ReaderMangaItem built = BuildReaderBookmarkMangaCandidate(candidatePath, entry);
                if (built == null)
                {
                    continue;
                }

                if (ReaderMangaContainsChapterFolder(built, entry.ChapterFolderPath))
                {
                    usedChapterOnlyFallback = string.Equals(candidatePath, entry.ChapterFolderPath, StringComparison.OrdinalIgnoreCase);
                    return built;
                }
            }

            return null;
        }

        private ReaderMangaItem FindReaderBookmarkMangaInLibrary(ReaderPageBookmarkEntry entry)
        {
            if (_readerLibrary == null || _readerLibrary.Count == 0)
            {
                return null;
            }

            foreach (Func<ReaderMangaItem, bool> matcher in new Func<ReaderMangaItem, bool>[]
            {
                item => !string.IsNullOrWhiteSpace(entry.SeriesFolderPath) &&
                    string.Equals(item.FolderPath, entry.SeriesFolderPath, StringComparison.OrdinalIgnoreCase),
                item => ReaderMangaContainsChapterFolder(item, entry.ChapterFolderPath),
                item => !string.IsNullOrWhiteSpace(entry.MangaFolderPath) &&
                    string.Equals(item.FolderPath, entry.MangaFolderPath, StringComparison.OrdinalIgnoreCase),
                item => {
                    string parentFolder = GetReaderParentSeriesFolderPath(entry.ChapterFolderPath, entry.SourceDomain);
                    return !string.IsNullOrWhiteSpace(parentFolder) &&
                        string.Equals(item.FolderPath, parentFolder, StringComparison.OrdinalIgnoreCase);
                }
            })
            {
                ReaderMangaItem match = _readerLibrary.FirstOrDefault(matcher);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private ReaderMangaItem BuildReaderBookmarkMangaCandidate(string folderPath, ReaderPageBookmarkEntry entry)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return null;
            }

            return BuildReaderMangaItem(folderPath, entry.SourceDomain, 0, GetReaderCompletionStateForCurrentRoot(), LoadReaderDownloadWatchState(GetCurrentReaderLibraryRoot()));
        }

        private static bool ReaderMangaContainsChapterFolder(ReaderMangaItem manga, string chapterFolderPath)
        {
            return manga != null &&
                   !string.IsNullOrWhiteSpace(chapterFolderPath) &&
                   manga.Chapters != null &&
                   manga.Chapters.Any(item => string.Equals(item.FolderPath, chapterFolderPath, StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureReaderLibraryContains(ReaderMangaItem manga)
        {
            if (manga == null)
            {
                return;
            }

            if (_readerLibrary == null)
            {
                _readerLibrary = new List<ReaderMangaItem>();
            }

            if (_readerLibrary.Any(item => string.Equals(item.FolderPath, manga.FolderPath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _readerLibrary.Add(manga);
            _readerLibrary = _readerLibrary
                .OrderBy(item => item.SourceGroup ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, _readerSortComparer)
                .ToList();
            _readerDomains = BuildReaderDomainItems(_readerLibrary);
            ApplyReaderMangaWatchSorts(keepSelection: true);
        }

        private void ReaderFitWidth_Click(object sender, RoutedEventArgs e)
        {
            _readerFitMode = ReaderFitMode.FitWidth;
            UpdateReaderFitButtons();
            RenderReaderPage();
        }

        private void ReaderFitHeight_Click(object sender, RoutedEventArgs e)
        {
            _readerFitMode = ReaderFitMode.FitHeight;
            UpdateReaderFitButtons();
            RenderReaderPage();
        }

        private void ReaderActualSize_Click(object sender, RoutedEventArgs e)
        {
            _readerFitMode = ReaderFitMode.ActualSize;
            UpdateReaderFitButtons();
            RenderReaderPage();
        }

        private void ReaderFitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_readerUsesFastStone)
            {
                return;
            }
            if (_readerFitCombo == null) return;
            switch (_readerFitCombo.SelectedIndex)
            {
                case 0:
                    _readerFitMode = ReaderFitMode.FitWidth;
                    break;
                case 1:
                    _readerFitMode = ReaderFitMode.FitHeight;
                    break;
                case 2:
                    _readerFitMode = ReaderFitMode.ActualSize;
                    break;
                case 3:
                    _readerFitMode = ReaderFitMode.VerticalScroll;
                    break;
                case 4:
                    _readerFitMode = ReaderFitMode.HorizontalScrollLTR;
                    break;
                case 5:
                    _readerFitMode = ReaderFitMode.HorizontalScrollRTL;
                    break;
            }
            RenderReaderPage();
        }

        private void ReaderFullscreen_Click(object sender, RoutedEventArgs e)
        {
            string appName = GetReaderWatchCurrentAppDisplayName();
            UpdateReaderStatus(_isVietnameseUi
                ? $"{appName} chỉ mở khi click chapter hoặc ảnh."
                : $"{appName} opens only when you click a chapter or an image.");
        }

        private void ReaderWatchWith_Click(object sender, RoutedEventArgs e)
        {
            if (_readerWatchWithButton == null)
            {
                return;
            }

            var menu = new ContextMenu();
            menu.Items.Add(CreateReaderWatchWithMenuItem(ReaderWatchExternalApp.FastStone));
            menu.Items.Add(CreateReaderWatchWithMenuItem(ReaderWatchExternalApp.Bandiview));
            _readerWatchWithButton.ContextMenu = menu;
            menu.PlacementTarget = _readerWatchWithButton;
            menu.IsOpen = true;
        }

        private MenuItem CreateReaderWatchWithMenuItem(ReaderWatchExternalApp app)
        {
            var item = new MenuItem
            {
                Header = GetReaderWatchAppDisplayName(app),
                IsCheckable = true,
                IsChecked = _readerWatchExternalApp == app
            };
            item.Click += async (sender, args) => await SelectReaderWatchAppAsync(app);
            return item;
        }

        private async Task SelectReaderWatchAppAsync(ReaderWatchExternalApp app)
        {
            _readerWatchExternalApp = app;
            UpdateReaderWatchWithButtonLabel();
            RenderReaderPlaceholder();

            try
            {
                await EnsureReaderWatchAppReadyAsync(app);
            }
            catch (Exception ex)
            {
                UpdateReaderStatus((_isVietnameseUi ? "Không thể tải " : "Failed to download ") + GetReaderWatchCurrentAppDisplayName() + ": " + ex.Message);
                return;
            }

            _readerWatchAppSelected = true;

            UpdateReaderStatus((_isVietnameseUi ? "Đã chọn app watch: " : "Selected watch app: ") + GetReaderWatchCurrentAppDisplayName());
        }

        private void PromptReaderWatchAppSelectionIfNeeded()
        {
            if (_readerWatchAppSelected || _readerWatchWithButton == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() => ReaderWatchWith_Click(_readerWatchWithButton, new RoutedEventArgs())), DispatcherPriority.Loaded);
        }

        private void XnConvert_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button))
            {
                return;
            }

            var menu = new ContextMenu();
            menu.Items.Add(CreateXnConvertInstallMenuItem("tự cài đặt vào ổ C", silentInstall: true));
            menu.Items.Add(CreateXnConvertInstallMenuItem("cài vào ổ khác", silentInstall: false));
            button.ContextMenu = menu;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }

        private MenuItem CreateXnConvertInstallMenuItem(string label, bool silentInstall)
        {
            var item = new MenuItem
            {
                Header = label
            };
            item.Click += async (sender, args) => await InstallXnConvertAsync(silentInstall);
            return item;
        }

        private async Task InstallXnConvertAsync(bool silentInstall)
        {
            try
            {
                UpdateReaderStatus(silentInstall ? "Đang tải XnConvert để cài vào ổ C..." : "Đang tải XnConvert...");
                await EnsureXnConvertInstallerReadyAsync();
                StartXnConvertInstaller(silentInstall);
                UpdateReaderStatus(silentInstall
                    ? "Đã chạy XnConvert silent install vào ổ C."
                    : "Đã chạy XnConvert installer.");
            }
            catch (Exception ex)
            {
                UpdateReaderStatus("XnConvert lỗi: " + ex.Message);
            }
        }

        private async Task EnsureXnConvertInstallerReadyAsync()
        {
            if (File.Exists(PortablePaths.XnConvertInstallerPath))
            {
                return;
            }

            Directory.CreateDirectory(PortablePaths.PortableDataRoot);
            using (var response = await _httpClient.GetAsync("https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/accessories/XnConvert.Portable.exe", HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = File.Open(PortablePaths.XnConvertInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output);
                }
            }
        }

        private void StartXnConvertInstaller(bool silentInstall)
        {
            var psi = new ProcessStartInfo
            {
                FileName = PortablePaths.XnConvertInstallerPath,
                Arguments = silentInstall ? "/s" : string.Empty,
                WorkingDirectory = PortablePaths.PortableDataRoot,
                UseShellExecute = true
            };

            Process.Start(psi);
        }

        private void KnightComic_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button))
            {
                return;
            }

            var menu = new ContextMenu();
            menu.Items.Add(CreateKnightComicInstallMenuItem("tự cài đặt vào ổ C", silentInstall: true));
            menu.Items.Add(CreateKnightComicInstallMenuItem("cài vào ổ khác", silentInstall: false));
            button.ContextMenu = menu;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }

        private MenuItem CreateKnightComicInstallMenuItem(string label, bool silentInstall)
        {
            var item = new MenuItem
            {
                Header = label
            };
            item.Click += async (sender, args) => await InstallKnightComicAsync(silentInstall);
            return item;
        }

        private async Task InstallKnightComicAsync(bool silentInstall)
        {
            try
            {
                UpdateReaderStatus(silentInstall ? "Đang tải KnightComic để cài vào ổ C..." : "Đang tải KnightComic...");
                await EnsureKnightComicInstallerReadyAsync();
                StartKnightComicInstaller(silentInstall);
                UpdateReaderStatus(silentInstall
                    ? "Đã chạy KnightComic silent install vào ổ C."
                    : "Đã chạy KnightComic installer.");
            }
            catch (Exception ex)
            {
                UpdateReaderStatus("KnightComic lỗi: " + ex.Message);
            }
        }

        private async Task EnsureKnightComicInstallerReadyAsync()
        {
            if (File.Exists(PortablePaths.KnightComicInstallerPath))
            {
                return;
            }

            Directory.CreateDirectory(PortablePaths.PortableDataRoot);
            using (var response = await _httpClient.GetAsync("https://github.com/ghostminhtoan/comic.downloader.gmtpc/releases/download/accessories/KnightComic.exe", HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = File.Open(PortablePaths.KnightComicInstallerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output);
                }
            }
        }

        private void StartKnightComicInstaller(bool silentInstall)
        {
            var psi = new ProcessStartInfo
            {
                FileName = PortablePaths.KnightComicInstallerPath,
                Arguments = silentInstall ? "/s" : string.Empty,
                WorkingDirectory = PortablePaths.PortableDataRoot,
                UseShellExecute = true
            };

            Process.Start(psi);
        }

        public void ToggleReaderFullscreen()
        {
            if (_readerUsesFastStone)
            {
                return;
            }
            _isReaderFullscreen = !_isReaderFullscreen;

            if (_isReaderFullscreen)
            {
                if (_readerFullscreenButton != null)
                {
                    _readerFullscreenButton.Content = _isVietnameseUi ? "Thoát Full" : "Exit Full";
                }

                _fullscreenWindow = new ReaderFullscreenWindow(this);
                _fullscreenWindow.Show();
            }
            else
            {
                if (_fullscreenWindow != null)
                {
                    _fullscreenWindow.Close();
                }
                if (_readerFullscreenButton != null)
                {
                    _readerFullscreenButton.Content = "Fullscreen";
                }
            }
        }

        internal void OnFullscreenClosed()
        {
            _fullscreenWindow = null;
            _isReaderFullscreen = false;
            if (_readerFullscreenButton != null)
            {
                _readerFullscreenButton.Content = "Fullscreen";
            }
        }

        internal void HandleFullscreenHotkey(KeyEventArgs e)
        {
            HandleReaderHotkeys(e);
        }

        private static string BuildReaderHtmlForChapter(List<string> pageUris, ReaderFitMode fitMode, int activePageIndex)
        {
            var sb = new StringBuilder();
            if (fitMode == ReaderFitMode.HorizontalScrollLTR)
            {
                sb.Append("<div id=\"readerStage\" class=\"reader-stage chapter-mode horizontal-ltr\">");
                for (int i = 0; i < pageUris.Count; i++)
                {
                    sb.Append($"<img class=\"reader-page\" data-page-index=\"{i}\" src=\"{EscapeHtml(pageUris[i])}\" style=\"display:block;height:calc(100vh - 24px);width:auto;margin:0;padding:0;\"/>");
                }
                sb.Append("</div>");
            }
            else if (fitMode == ReaderFitMode.HorizontalScrollRTL)
            {
                sb.Append("<div id=\"readerStage\" class=\"reader-stage chapter-mode horizontal-rtl\">");
                for (int i = 0; i < pageUris.Count; i++)
                {
                    sb.Append($"<img class=\"reader-page\" data-page-index=\"{i}\" src=\"{EscapeHtml(pageUris[i])}\" style=\"display:block;height:calc(100vh - 24px);width:auto;margin:0;padding:0;\"/>");
                }
                sb.Append("</div>");
            }
            else
            {
                sb.Append("<div id=\"readerStage\" class=\"reader-stage chapter-mode vertical-scroll\">");
                for (int i = 0; i < pageUris.Count; i++)
                {
                    sb.Append($"<img class=\"reader-page\" data-page-index=\"{i}\" src=\"{EscapeHtml(pageUris[i])}\" style=\"display:block;width:min(100%, 100vw - 24px);height:auto;margin:0 auto;padding:0;\"/>");
                }
                sb.Append("</div>");
            }

            return BuildReaderDocument(fitMode, sb.ToString(), activePageIndex);
        }
private bool HandleReaderHotkeys(KeyEventArgs e)
        {
            if (_currentSection != AppSection.Watch)
            {
                return false;
            }

            ModifierKeys modifiers = Keyboard.Modifiers;
            bool ctrl = (modifiers & ModifierKeys.Control) != 0;
            bool shift = (modifiers & ModifierKeys.Shift) != 0;

            if (ctrl)
            {
                switch (e.Key)
                {
                    case Key.PageDown:
                        MoveReaderChapter(1);
                        e.Handled = true;
                        return true;

                    case Key.PageUp:
                        MoveReaderChapter(-1);
                        e.Handled = true;
                        return true;

                    case Key.B:
                        if (shift)
                        {
                            OpenBookmarkHistoryWindow(2);
                        }
                        else
                        {
                            AddCurrentReaderPageBookmark();
                        }
                        e.Handled = true;
                        return true;

                    case Key.H:
                        OpenBookmarkHistoryWindow(0);
                        e.Handled = true;
                        return true;
                }
            }

            if (modifiers != ModifierKeys.None)
            {
                return false;
            }

            switch (e.Key)
            {
                case Key.Left:
                case Key.Up:
                case Key.PageUp:
                case Key.Back:
                    MoveReaderPage(-1);
                    e.Handled = true;
                    return true;

                case Key.Right:
                case Key.Down:
                case Key.PageDown:
                case Key.Space:
                    MoveReaderPage(1);
                    e.Handled = true;
                    return true;

                case Key.Home:
                    if (IsReaderHorizontalMode())
                    {
                        ScrollReaderViewportToBoundary(goToStart: true);
                    }
                    else
                    {
                        OpenReaderPageBoundary(openLastPage: false);
                    }
                    e.Handled = true;
                    return true;

                case Key.End:
                    if (IsReaderHorizontalMode())
                    {
                        ScrollReaderViewportToBoundary(goToStart: false);
                    }
                    else
                    {
                        OpenReaderPageBoundary(openLastPage: true);
                    }
                    e.Handled = true;
                    return true;

                case Key.F:
                case Key.F11:
                    ToggleReaderFullscreen();
                    e.Handled = true;
                    return true;

                case Key.Add:
                case Key.OemPlus:
                    SetReaderZoom(_readerZoom + 0.12d, keepCurrentPageVisible: true);
                    e.Handled = true;
                    return true;

                case Key.Subtract:
                case Key.OemMinus:
                    SetReaderZoom(_readerZoom - 0.12d, keepCurrentPageVisible: true);
                    e.Handled = true;
                    return true;

                case Key.D0:
                case Key.NumPad0:
                    SetReaderZoom(1d, keepCurrentPageVisible: true);
                    e.Handled = true;
                    return true;

                case Key.D1:
                    ReaderFitWidth_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return true;

                case Key.D2:
                    ReaderFitHeight_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return true;

                case Key.D3:
                    ReaderActualSize_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return true;
            }

            return false;
        }

        internal void HandleReaderWebMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (message.StartsWith("reader:setPage:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(message.Substring("reader:setPage:".Length), out int pageIndex))
                {
                    SyncCurrentReaderPageFromIndex(pageIndex);
                }
                return;
            }

            switch (message)
            {
                case "reader:prevPage":
                    MoveReaderPage(-1);
                    break;
                case "reader:nextPage":
                    MoveReaderPage(1);
                    break;
                case "reader:firstPage":
                    OpenReaderPageBoundary(openLastPage: false);
                    break;
                case "reader:lastPage":
                    OpenReaderPageBoundary(openLastPage: true);
                    break;
                case "reader:nextChapter":
                    MoveReaderChapter(1);
                    break;
                case "reader:prevChapter":
                    MoveReaderChapter(-1);
                    break;
                case "reader:addPageBookmark":
                    AddCurrentReaderPageBookmark();
                    break;
                case "reader:showPageBookmarks":
                    OpenBookmarkHistoryWindow(2);
                    break;
                case "reader:showHistory":
                    OpenBookmarkHistoryWindow(0);
                    break;
            }
        }

        private static string BuildReaderDocument(ReaderFitMode fitMode, string contentHtml, int activePageIndex)
        {
            string fitModeName = fitMode == ReaderFitMode.HorizontalScrollLTR
                ? "horizontal-ltr"
                : fitMode == ReaderFitMode.HorizontalScrollRTL
                    ? "horizontal-rtl"
                    : fitMode == ReaderFitMode.VerticalScroll
                        ? "vertical-scroll"
                        : fitMode == ReaderFitMode.FitHeight
                            ? "fit-height"
                            : fitMode == ReaderFitMode.ActualSize
                                ? "actual-size"
                                : "fit-width";

            string script = @"
(() => {
  const viewport = document.getElementById('readerViewport');
  const stage = document.getElementById('readerStage');
  const mode = document.body.dataset.fitMode || 'fit-width';
  const activePageIndex = parseInt(document.body.dataset.activePageIndex || '0', 10) || 0;
  if (!viewport || !stage) return;

  let scale = 1;
  const minScale = 0.5;
  const maxScale = 6;
  let drag = null;
  let lastReportedPageIndex = -1;
  let traceScheduled = false;
  let initialRestoreDone = false;

  const isHorizontalRtl = mode === 'horizontal-rtl';

  function clamp(value, min, max) {
    return Math.max(min, Math.min(max, value));
  }

  function applyScale() {
    stage.style.transform = 'scale(' + scale + ')';
    updateViewportAlignment();
  }

  function showToast(message) {
    if (!message) return;

    let toast = document.getElementById('readerToast');
    if (!toast) {
      toast = document.createElement('div');
      toast.id = 'readerToast';
      toast.style.position = 'fixed';
      toast.style.top = '14px';
      toast.style.right = '14px';
      toast.style.zIndex = '9999';
      toast.style.maxWidth = '420px';
      toast.style.padding = '10px 14px';
      toast.style.borderRadius = '10px';
      toast.style.background = 'rgba(10, 16, 30, 0.92)';
      toast.style.border = '1px solid rgba(0, 240, 255, 0.65)';
      toast.style.boxShadow = '0 0 18px rgba(0, 240, 255, 0.25)';
      toast.style.color = '#d7e2ec';
      toast.style.font = '600 12px/1.45 Segoe UI, sans-serif';
      toast.style.pointerEvents = 'none';
      toast.style.opacity = '0';
      toast.style.transform = 'translateY(-6px)';
      toast.style.transition = 'opacity 160ms ease, transform 160ms ease';
      toast.style.backdropFilter = 'blur(4px)';
      document.body.appendChild(toast);
    }

    toast.textContent = message;
    toast.style.opacity = '1';
    toast.style.transform = 'translateY(0)';

    clearTimeout(window.__readerToastTimer);
    window.__readerToastTimer = setTimeout(() => {
      toast.style.opacity = '0';
      toast.style.transform = 'translateY(-6px)';
    }, 1800);
  }

  function post(message) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(message);
    }
  }

  function scrollBoundary(direction) {
    const goStart = direction === 'start';
    if (mode === 'horizontal-rtl') {
      viewport.scrollLeft = goStart ? viewport.scrollWidth : 0;
      return;
    }

    if (mode === 'horizontal-ltr') {
      viewport.scrollLeft = goStart ? 0 : viewport.scrollWidth;
      return;
    }

    viewport.scrollTop = goStart ? 0 : viewport.scrollHeight;
  }

  function scrollToPage(index) {
    const target = stage.querySelector('[data-page-index=' + index + ']');
    if (!target) return;
    const options = mode === 'vertical-scroll'
      ? { block: 'start', inline: 'nearest' }
      : mode === 'horizontal-rtl'
        ? { block: 'nearest', inline: 'end' }
        : { block: 'nearest', inline: 'start' };
    target.scrollIntoView(options);
    reportCurrentPage(index);
    scheduleTraceVisiblePage();
  }

  function centerVerticalViewport() {
    if (mode !== 'vertical-scroll') return;
    viewport.scrollLeft = Math.max(0, (viewport.scrollWidth - viewport.clientWidth) / 2);
  }

  function updateViewportAlignment() {
    const stageRect = stage.getBoundingClientRect();
    const canCenterHorizontally = stageRect.width < viewport.clientWidth;
    const canCenterVertically = stageRect.height < viewport.clientHeight;

    viewport.style.display = 'flex';
    viewport.style.justifyContent = canCenterHorizontally ? 'center' : 'flex-start';
    viewport.style.alignItems = canCenterVertically ? 'center' : 'flex-start';
  }

  function reportCurrentPage(index) {
    if (index === lastReportedPageIndex || index < 0) return;
    lastReportedPageIndex = index;
    post('reader:setPage:' + index);
  }

  function traceVisiblePage() {
    traceScheduled = false;
    if (!initialRestoreDone) return;
    const pages = stage.querySelectorAll('[data-page-index]');
    if (!pages || pages.length === 0) return;

    const viewportRect = viewport.getBoundingClientRect();
    let bestIndex = -1;
    let bestVisibleArea = -1;

    pages.forEach((page) => {
      const rect = page.getBoundingClientRect();
      const visibleWidth = Math.max(0, Math.min(rect.right, viewportRect.right) - Math.max(rect.left, viewportRect.left));
      const visibleHeight = Math.max(0, Math.min(rect.bottom, viewportRect.bottom) - Math.max(rect.top, viewportRect.top));
      const visibleArea = visibleWidth * visibleHeight;
      if (visibleArea > bestVisibleArea) {
        bestVisibleArea = visibleArea;
        bestIndex = parseInt(page.dataset.pageIndex || '-1', 10);
      }
    });

    if (bestIndex >= 0) {
      reportCurrentPage(bestIndex);
    }
  }

  function scheduleTraceVisiblePage() {
    if (traceScheduled) return;
    traceScheduled = true;
    window.requestAnimationFrame(traceVisiblePage);
  }

  function zoomAt(clientX, clientY, deltaY) {
    const rect = viewport.getBoundingClientRect();
    const beforeX = (viewport.scrollLeft + (clientX - rect.left)) / scale;
    const beforeY = (viewport.scrollTop + (clientY - rect.top)) / scale;
    const step = deltaY < 0 ? 0.12 : -0.12;
    scale = clamp(scale + step, minScale, maxScale);
    if (Math.abs(scale - 1) < 0.02) scale = 1;
    applyScale();
    if (mode === 'vertical-scroll') {
      centerVerticalViewport();
    } else {
      viewport.scrollLeft = beforeX * scale - (clientX - rect.left);
    }
    viewport.scrollTop = beforeY * scale - (clientY - rect.top);
    updateViewportAlignment();
    scheduleTraceVisiblePage();
  }

  viewport.addEventListener('wheel', (event) => {
    if (!event.ctrlKey) return;
    event.preventDefault();

    zoomAt(event.clientX, event.clientY, event.deltaY);
  }, { passive: false });

  viewport.addEventListener('mousedown', (event) => {
    if (event.button !== 0) return;
    drag = {
      x: event.clientX,
      y: event.clientY,
      left: viewport.scrollLeft,
      top: viewport.scrollTop
    };
    viewport.classList.add('dragging');
    event.preventDefault();
  });

  window.addEventListener('mousemove', (event) => {
    if (!drag) return;
    viewport.scrollLeft = drag.left - (event.clientX - drag.x);
    viewport.scrollTop = drag.top - (event.clientY - drag.y);
    scheduleTraceVisiblePage();
  });

  function stopDrag() {
    drag = null;
    viewport.classList.remove('dragging');
  }

  window.addEventListener('mouseup', stopDrag);
  window.addEventListener('mouseleave', stopDrag);
  viewport.addEventListener('scroll', scheduleTraceVisiblePage, { passive: true });
  window.addEventListener('resize', () => {
    updateViewportAlignment();
    scheduleTraceVisiblePage();
  });

  window.readerApi = {
    scrollBoundary,
    scrollToPage,
    showToast
  };

  window.addEventListener('keydown', (event) => {
    if (event.ctrlKey && event.key === 'PageDown') {
      event.preventDefault();
      post('reader:nextChapter');
      return;
    }

    if (event.ctrlKey && event.key === 'PageUp') {
      event.preventDefault();
      post('reader:prevChapter');
      return;
    }

    if (event.ctrlKey && !event.shiftKey && (event.key === 'b' || event.key === 'B')) {
      event.preventDefault();
      post('reader:addPageBookmark');
      return;
    }

    if (event.ctrlKey && event.shiftKey && (event.key === 'b' || event.key === 'B')) {
      event.preventDefault();
      post('reader:showPageBookmarks');
      return;
    }

    if (event.ctrlKey && (event.key === 'h' || event.key === 'H')) {
      event.preventDefault();
      post('reader:showHistory');
      return;
    }

    switch (event.key) {
      case 'Home':
        event.preventDefault();
        if (mode === 'horizontal-ltr' || mode === 'horizontal-rtl') {
          scrollBoundary('start');
        } else {
          post('reader:firstPage');
        }
        break;
      case 'End':
        event.preventDefault();
        if (mode === 'horizontal-ltr' || mode === 'horizontal-rtl') {
          scrollBoundary('end');
        } else {
          post('reader:lastPage');
        }
        break;
    }
  });

  function restoreInitialHorizontalRtlPosition() {
    if (!isHorizontalRtl) return;

    requestAnimationFrame(() => {
      viewport.scrollLeft = viewport.scrollWidth;

      requestAnimationFrame(() => {
        if (activePageIndex <= 0) {
          viewport.scrollLeft = viewport.scrollWidth;
        } else {
          scrollToPage(activePageIndex);
        }

        requestAnimationFrame(() => {
          initialRestoreDone = true;
          scheduleTraceVisiblePage();
        });
      });
    });
  }

  function restoreInitialNonHorizontalPosition() {
    requestAnimationFrame(() => {
      centerVerticalViewport();
      scrollToPage(activePageIndex);

      requestAnimationFrame(() => {
        initialRestoreDone = true;
        scheduleTraceVisiblePage();
      });
    });
  }

  window.addEventListener('load', () => {
    applyScale();
    document.body.focus();
    if (isHorizontalRtl) {
      restoreInitialHorizontalRtlPosition();
      return;
    }

    restoreInitialNonHorizontalPosition();
  });
})();
";

            return "<html><head><meta charset=\"utf-8\"/><style>" +
                   "html,body{margin:0;padding:0;background:#06090f;overflow:hidden;height:100%;}" +
                   "body{color:#d7e2ec;font-family:'Segoe UI',sans-serif;}" +
                   "#readerViewport{width:100vw;height:100vh;overflow:auto;cursor:grab;overscroll-behavior:none;}" +
                   "#readerViewport.dragging{cursor:grabbing;}" +
                   ".reader-stage{transform-origin:0 0;will-change:transform;box-sizing:border-box;padding:12px;}" +
                   ".reader-stage img{-webkit-user-drag:none;user-select:none;pointer-events:none;}" +
                   "#readerToast{backdrop-filter:blur(4px);}" +
                   ".single-mode{min-width:100%;min-height:100%;display:flex;justify-content:center;align-items:flex-start;}" +
                   ".chapter-mode.vertical-scroll{width:100%;display:flex;flex-direction:column;align-items:center;transform-origin:top center;}" +
                   ".chapter-mode.horizontal-ltr,.chapter-mode.horizontal-rtl{width:max-content;min-height:100%;display:flex;align-items:flex-start;}" +
                   ".chapter-mode.horizontal-ltr{flex-direction:row;}" +
                   ".chapter-mode.horizontal-rtl{flex-direction:row-reverse;}" +
                   "</style></head>" +
                   $"<body tabindex=\"0\" data-fit-mode=\"{fitModeName}\" data-active-page-index=\"{activePageIndex}\">" +
                   "<div id=\"readerViewport\">" +
                   contentHtml +
                   "</div><script>" +
                   script +
                   "</script></body></html>";
        }

        private void UpdateReaderLanguage()
        {
            if (_readerOtherFolderButton != null)
            {
                _readerOtherFolderButton.Content = _isVietnameseUi ? "Mở thư mục khác" : "Load other folder";
            }

            if (_readerRootFolderButton != null)
            {
                _readerRootFolderButton.Content = _isVietnameseUi ? "Mở thư mục gốc" : "Load root folder";
            }

            if (_readerFullscreenButton != null)
            {
                _readerFullscreenButton.Content = _isReaderFullscreen 
                    ? (_isVietnameseUi ? "Thoát Full" : "Exit Full")
                    : "Fullscreen";
            }

            if (_readerChapterMissingButton != null) _readerChapterMissingButton.Content = _isVietnameseUi ? "Chương thiếu" : "Missing chapters";
            if (_readerCopyMissingButton != null) _readerCopyMissingButton.Content = _isVietnameseUi ? "Sao chép chap thiếu" : "Copy missing chapter";
            if (_readerCopyAllMissingButton != null) _readerCopyAllMissingButton.Content = _isVietnameseUi ? "Sao chép chap thiếu của mọi truyện" : "Copy all book's missing chapter";
            if (_readerGoogleAllMissingButton != null) _readerGoogleAllMissingButton.Content = _isVietnameseUi ? "Google chap thiếu của mọi truyện" : "Google all book's missing chapter";

            UpdateReaderFitButtons();
            UpdateReaderNovelLanguageState();
            if (_currentReaderPage == null)
            {
                UpdateReaderStatus(_isVietnameseUi
                    ? "Bấm Refresh library để quét thư mục tải và bắt đầu đọc."
                    : "Use Refresh library to scan the download root and start reading.");
            }
            else
            {
                UpdateReaderStatus(BuildReaderStatusText());
            }
        }

        private void UpdateReaderNovelLanguageState()
        {
            if (_readerNovelOtherFolderButton != null)
            {
                _readerNovelOtherFolderButton.Content = _isVietnameseUi ? "Mở thư mục khác" : "Load other folder";
            }

            if (_readerNovelRootFolderButton != null)
            {
                _readerNovelRootFolderButton.Content = _isVietnameseUi ? "Mở thư mục gốc" : "Load root folder";
            }

            if (_currentReaderNovelFile == null)
            {
                UpdateReaderNovelStatus(_isVietnameseUi
                    ? "Bấm Refresh library để quét thư mục tải novel."
                    : "Use Refresh library to scan the novel download root.");
            }
            else
            {
                UpdateReaderNovelStatus((_isVietnameseUi ? "Đã chọn file .md: " : "Selected .md file: ") + _currentReaderNovelFile.Name);
            }
        }

        internal ReaderFitMode GetReaderFitMode()
        {
            return _readerFitMode;
        }

        internal int GetCurrentReaderPageIndex()
        {
            return _currentReaderPage != null ? _currentReaderPage.Index : 0;
        }

        internal double GetCurrentReaderZoom()
        {
            return _readerZoom;
        }

        internal string GetCurrentReaderTitle()
        {
            return _readerCurrentTitleText != null ? _readerCurrentTitleText.Text : string.Empty;
        }

        internal List<ReaderPageItem> GetCurrentReaderPagesSnapshot()
        {
            if (_currentReaderChapter == null || _currentReaderChapter.Pages == null)
            {
                return new List<ReaderPageItem>();
            }

            return _currentReaderChapter.Pages
                .Select(page => new ReaderPageItem
                {
                    FilePath = page.FilePath,
                    Index = page.Index,
                    Name = page.Name
                })
                .ToList();
        }

        private TextBox _xnConvertPathTextBox;
        private string XnConvertPathFile => Path.Combine(PortablePaths.PortableDataRoot, "xnconvert_path.txt");

        private string GetXnConvertPath()
        {
            try
            {
                if (File.Exists(XnConvertPathFile))
                {
                    return File.ReadAllText(XnConvertPathFile, System.Text.Encoding.UTF8).Trim();
                }
            }
            catch {}
            string defaultPath = @"C:\XnConvert Portable\XnConvertPortable.exe";
            if (File.Exists(defaultPath))
            {
                return defaultPath;
            }
            return string.Empty;
        }

        private void SaveXnConvertPath(string path)
        {
            try
            {
                Directory.CreateDirectory(PortablePaths.PortableDataRoot);
                File.WriteAllText(XnConvertPathFile, path, System.Text.Encoding.UTF8);
            }
            catch {}
        }

        private FrameworkElement CreateWatchConvertTabContent()
        {
            var mainCard = CreateWatchMainCard();
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var titleText = new TextBlock
            {
                Text = _isVietnameseUi ? "Công cụ Convert" : "Convert Tools",
                Foreground = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(titleText, 0);
            mainGrid.Children.Add(titleText);

            var panelGrid = new Grid();
            panelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(panelGrid, 1);
            mainGrid.Children.Add(panelGrid);

            // XnConvert Panel
            var xnContent = new StackPanel { Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Top };

            var lblInstall = new TextBlock
            {
                Text = _isVietnameseUi ? "Cài đặt ứng dụng:" : "Install App:",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            xnContent.Children.Add(lblInstall);

            var btnInstallXn = CreateReaderMiniButton("INSTALL XNCONVERT", BtnInstallXnConvert_Click, 180);
            btnInstallXn.Style = TryFindResource("CompactCyanButton") as Style;
            btnInstallXn.HorizontalAlignment = HorizontalAlignment.Left;
            btnInstallXn.Margin = new Thickness(0, 0, 0, 15);
            xnContent.Children.Add(btnInstallXn);

            var lblPath = new TextBlock
            {
                Text = _isVietnameseUi ? "Đường dẫn XnConvertPortable.exe:" : "XnConvertPortable.exe path:",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            xnContent.Children.Add(lblPath);

            _xnConvertPathTextBox = new TextBox
            {
                Text = GetXnConvertPath(),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x38)),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 0, 15)
            };
            _xnConvertPathTextBox.TextChanged += (s, e) => SaveXnConvertPath(_xnConvertPathTextBox.Text);
            xnContent.Children.Add(_xnConvertPathTextBox);

            var xnPanel = CreateReaderWatchPanel("XnConvert Tool", xnContent);
            xnPanel.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(xnPanel, 0);
            panelGrid.Children.Add(xnPanel);

            // KnightComic Panel
            var kcContent = new StackPanel { Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Top };

            var lblInstallKc = new TextBlock
            {
                Text = _isVietnameseUi ? "Cài đặt ứng dụng:" : "Install App:",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            kcContent.Children.Add(lblInstallKc);

            var btnInstallKc = CreateReaderMiniButton("INSTALL KNIGHTCOMIC", BtnInstallKnightComic_Click, 180);
            btnInstallKc.Style = TryFindResource("CompactOrangeButton") as Style ?? TryFindResource("CompactPinkButton") as Style;
            btnInstallKc.HorizontalAlignment = HorizontalAlignment.Left;
            btnInstallKc.Margin = new Thickness(0, 0, 0, 15);
            kcContent.Children.Add(btnInstallKc);

            var kcPanel = CreateReaderWatchPanel("Knight Comic", kcContent);
            Grid.SetColumn(kcPanel, 1);
            panelGrid.Children.Add(kcPanel);

            mainCard.Child = mainGrid;
            return mainCard;
        }

        private async void BtnInstallXnConvert_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                _isVietnameseUi 
                    ? "Bạn có muốn cài đặt mặc định XnConvert vào ổ C không?\nChọn YES để tự động cài đặt vào ổ C (chế độ silent).\nChọn NO để cài đặt vào ổ/thư mục khác.\nChọn CANCEL để hủy bỏ."
                    : "Do you want to install XnConvert to C drive by default?\nSelect YES to install silently to C drive.\nSelect NO to choose a custom directory.\nSelect CANCEL to cancel.",
                "Install XnConvert",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    UpdateReaderStatus(_isVietnameseUi ? "Đang tải XnConvert để cài vào ổ C..." : "Downloading XnConvert to install to C...");
                    await EnsureXnConvertInstallerReadyAsync();
                    StartXnConvertInstaller(silentInstall: true);
                    
                    string defaultPath = @"C:\XnConvert Portable\XnConvertPortable.exe";
                    if (_xnConvertPathTextBox != null)
                    {
                        _xnConvertPathTextBox.Text = defaultPath;
                    }
                    SaveXnConvertPath(defaultPath);
                    
                    UpdateReaderStatus(_isVietnameseUi ? "Đã chạy XnConvert silent install vào ổ C." : "Launched XnConvert silent installer on C.");
                }
                catch (Exception ex)
                {
                    UpdateReaderStatus("XnConvert lỗi: " + ex.Message);
                }
            }
            else if (result == MessageBoxResult.No)
            {
                var prompt = new PromptDialog(
                    _isVietnameseUi ? "Nơi cài đặt XnConvert" : "XnConvert Installation Location",
                    _isVietnameseUi ? "Nhập đường dẫn thư mục cài đặt (ví dụ: D:\\):" : "Enter installation folder path (e.g. D:\\):",
                    "D:\\");
                prompt.Owner = Window.GetWindow(this);
                if (prompt.ShowDialog() == true)
                {
                    string targetDir = prompt.ResponseText.Trim();
                    if (!string.IsNullOrWhiteSpace(targetDir))
                    {
                        try
                        {
                            UpdateReaderStatus(_isVietnameseUi ? "Đang tải XnConvert..." : "Downloading XnConvert...");
                            await EnsureXnConvertInstallerReadyAsync();
                            
                            string normalizedDir = targetDir;
                            if (normalizedDir.Length == 1 && char.IsLetter(normalizedDir[0]))
                            {
                                normalizedDir += ":\\";
                            }
                            else if (normalizedDir.Length == 2 && char.IsLetter(normalizedDir[0]) && normalizedDir[1] == ':')
                            {
                                normalizedDir += "\\";
                            }
                            else if (!normalizedDir.EndsWith("\\") && !normalizedDir.EndsWith("/"))
                            {
                                normalizedDir += "\\";
                            }

                            var psi = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = PortablePaths.XnConvertInstallerPath,
                                Arguments = $"-d\"{normalizedDir}\" /s",
                                WorkingDirectory = PortablePaths.PortableDataRoot,
                                UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);

                            string finalPath = Path.Combine(normalizedDir, "XnConvert Portable", "XnConvertPortable.exe");
                            if (_xnConvertPathTextBox != null)
                            {
                                _xnConvertPathTextBox.Text = finalPath;
                            }
                            SaveXnConvertPath(finalPath);

                            UpdateReaderStatus(_isVietnameseUi ? "Đã chạy XnConvert installer." : "Launched XnConvert installer.");
                        }
                        catch (Exception ex)
                        {
                            UpdateReaderStatus("XnConvert lỗi: " + ex.Message);
                        }
                    }
                }
            }
        }

        private void BtnInstallKnightComic_Click(object sender, RoutedEventArgs e)
        {
            KnightComic_Click(sender, e);
        }

        private void BtnConvertDomainXnConvert_Click(object sender, RoutedEventArgs e)
        {
            var folderPaths = new List<string>();
            if (_readerDomainList?.ItemsSource is IEnumerable<ReaderDomainItem> domains)
            {
                foreach (var domain in domains)
                {
                    if (domain != null && domain.IsChecked && domain.Books != null)
                    {
                        foreach (var book in domain.Books)
                        {
                            if (book != null && !string.IsNullOrWhiteSpace(book.FolderPath))
                            {
                                folderPaths.Add(book.FolderPath);
                            }
                        }
                    }
                }
            }
            ExecuteXnConvertOnPaths(folderPaths);
        }

        private void BtnConvertBookXnConvert_Click(object sender, RoutedEventArgs e)
        {
            var folderPaths = new List<string>();
            if (_readerMangaList?.ItemsSource is IEnumerable<ReaderMangaItem> books)
            {
                foreach (var book in books)
                {
                    if (book != null && book.IsChecked)
                    {
                        if (!string.IsNullOrWhiteSpace(book.FolderPath))
                        {
                            folderPaths.Add(book.FolderPath);
                        }
                    }
                }
            }
            ExecuteXnConvertOnPaths(folderPaths);
        }

        private void ExecuteXnConvertOnPaths(List<string> folderPaths)
        {
            if (folderPaths.Count == 0)
            {
                MessageBox.Show(
                    _isVietnameseUi 
                        ? "Vui lòng tick chọn ít nhất một thư mục để convert."
                        : "Please check at least one folder to convert.",
                    "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string xnPath = GetXnConvertPath();
            if (!File.Exists(xnPath))
            {
                MessageBox.Show(
                    _isVietnameseUi 
                        ? $"Không tìm thấy XnConvert tại đường dẫn: {xnPath}\nVui lòng cài đặt hoặc chỉnh sửa đường dẫn trong tab Convert."
                        : $"XnConvert executable not found at: {xnPath}\nPlease install or configure the path in the Convert tab.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                var normalizedPaths = folderPaths.Select(path => path.Replace('/', '\\')).ToList();
                var args = string.Join(" ", normalizedPaths.Select(p => $"\"{p}\""));
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = xnPath.Replace('/', '\\'),
                    Arguments = args,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error running XnConvert: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private class PromptDialog : Window
        {
            private TextBox txtInput;
            public string ResponseText { get; private set; }

            public PromptDialog(string title, string promptText, string defaultResponse = "")
            {
                Title = title;
                Width = 400;
                Height = 150;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ResizeMode = ResizeMode.NoResize;
                Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F));
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF));
                BorderThickness = new Thickness(1);
                
                var grid = new Grid { Margin = new Thickness(10) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var lblPrompt = new TextBlock
                {
                    Text = promptText,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                Grid.SetRow(lblPrompt, 0);
                grid.Children.Add(lblPrompt);

                txtInput = new TextBox
                {
                    Text = defaultResponse,
                    Margin = new Thickness(0, 0, 0, 10),
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x38)),
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White
                };
                Grid.SetRow(txtInput, 1);
                grid.Children.Add(txtInput);

                var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var btnOk = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
                btnOk.Click += (s, e) => { ResponseText = txtInput.Text; DialogResult = true; };
                var btnCancel = new Button { Content = "Cancel", Width = 75, IsCancel = true };
                btnCancel.Click += (s, e) => { DialogResult = false; };
                
                buttonsPanel.Children.Add(btnOk);
                buttonsPanel.Children.Add(btnCancel);
                Grid.SetRow(buttonsPanel, 2);
                grid.Children.Add(buttonsPanel);

                Content = grid;
            }
        }
    }

    #if false
    internal class ReaderFullscreenWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly WebView2 _webView;
        private bool _webViewReady;

        public ReaderFullscreenWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            Title = "Manga Reader - Fullscreen";
            Background = new SolidColorBrush(Color.FromRgb(0x06, 0x09, 0x0F));
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            Topmost = true;

            _webView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var grid = new Grid();
            grid.Children.Add(_webView);
            Content = grid;

            Loaded += ReaderFullscreenWindow_Loaded;
            Closed += ReaderFullscreenWindow_Closed;
            KeyDown += ReaderFullscreenWindow_KeyDown;
        }

        private async void ReaderFullscreenWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(PortablePaths.WebView2UserDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(
                    null,
                    PortablePaths.WebView2UserDataFolder,
                    new CoreWebView2EnvironmentOptions());
                await _webView.EnsureCoreWebView2Async(env);
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    _webView.CoreWebView2.WebMessageReceived += (webViewSender, args) =>
                    {
                        try
                        {
                            _mainWindow.HandleReaderWebMessage(args.TryGetWebMessageAsString());
                        }
                        catch
                        {
                        }
                    };
                }

                _webView.PreviewKeyDown += (s, args) =>
                {
                    if (args.Key == Key.Escape)
                    {
                        Close();
                        args.Handled = true;
                    }
                    else
                    {
                        ReaderFullscreenWindow_KeyDown(this, args);
                    }
                };

                _webViewReady = true;
                RenderPage();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to initialize WebView2 in Fullscreen: " + ex.Message);
            }
        }

        public void RenderPage()
        {
            if (!_webViewReady) return;

            string tempDir = PortablePaths.PortableTempRoot;
            string tempFile = Path.Combine(tempDir, "reader.html");
            if (File.Exists(tempFile))
            {
                _webView.CoreWebView2.Navigate(new Uri(tempFile).AbsoluteUri);
            }
        }

        public void ExecuteScript(string script)
        {
            if (!_webViewReady || _webView?.CoreWebView2 == null || string.IsNullOrWhiteSpace(script))
            {
                return;
            }

            try
            {
                _webView.ExecuteScriptAsync(script);
            }
            catch
            {
            }
        }

        private void ReaderFullscreenWindow_Closed(object sender, EventArgs e)
        {
            _mainWindow.OnFullscreenClosed();
        }

        private void ReaderFullscreenWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            _mainWindow.HandleFullscreenHotkey(e);
        }
    }
    #endif

    internal class ReaderFullscreenWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private readonly ScrollViewer _scrollViewer;
        private readonly Border _viewportHost;
        private readonly StackPanel _stagePanel;
        private readonly ScaleTransform _scaleTransform = new ScaleTransform(1d, 1d);
        private readonly TextBlock _titleText;
        private readonly List<FrameworkElement> _pageElements = new List<FrameworkElement>();
        private double _zoom = 1d;
        private bool _syncingFromViewport;
        private bool _isMousePanning;
        private Point _mousePanStartPoint;
        private double _mousePanStartHorizontalOffset;
        private double _mousePanStartVerticalOffset;
        private Cursor _mousePanPreviousCursor;

        public ReaderFullscreenWindow(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            Title = "Manga Reader - Fullscreen";
            Background = new SolidColorBrush(Color.FromRgb(0x06, 0x09, 0x0F));
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            Topmost = true;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _titleText = new TextBlock
            {
                Margin = new Thickness(16, 12, 16, 10),
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            _stagePanel = new StackPanel
            {
                Margin = new Thickness(12),
                LayoutTransform = _scaleTransform,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            _viewportHost = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x06, 0x09, 0x0F)),
                Child = _stagePanel
            };
            _viewportHost.SizeChanged += (sender, args) => UpdateViewportAlignment();

            _scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                CanContentScroll = false,
                PanningMode = PanningMode.Both,
                Focusable = true,
                Content = _viewportHost
            };
            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            _scrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
            _scrollViewer.PreviewMouseLeftButtonDown += ScrollViewer_PreviewMouseLeftButtonDown;
            _scrollViewer.PreviewMouseLeftButtonUp += ScrollViewer_PreviewMouseLeftButtonUp;
            _scrollViewer.PreviewMouseMove += ScrollViewer_PreviewMouseMove;
            _scrollViewer.LostMouseCapture += ScrollViewer_LostMouseCapture;

            Grid.SetRow(_titleText, 0);
            Grid.SetRow(_scrollViewer, 1);
            root.Children.Add(_titleText);
            root.Children.Add(_scrollViewer);
            Content = root;

            Loaded += (sender, args) => RenderPage();
            Closed += ReaderFullscreenWindow_Closed;
            KeyDown += ReaderFullscreenWindow_KeyDown;
        }

        public void RenderPage()
        {
            List<ReaderPageItem> pages = _mainWindow.GetCurrentReaderPagesSnapshot();
            if (pages.Count == 0)
            {
                return;
            }

            MainWindow.ReaderFitMode fitMode = _mainWindow.GetReaderFitMode();
            int activePageIndex = _mainWindow.GetCurrentReaderPageIndex();
            _titleText.Text = _mainWindow.GetCurrentReaderTitle();
            _zoom = _mainWindow.GetCurrentReaderZoom();

            _pageElements.Clear();
            _stagePanel.Children.Clear();
            _stagePanel.Orientation = fitMode == MainWindow.ReaderFitMode.VerticalScroll ||
                                      fitMode == MainWindow.ReaderFitMode.FitWidth ||
                                      fitMode == MainWindow.ReaderFitMode.FitHeight ||
                                      fitMode == MainWindow.ReaderFitMode.ActualSize
                ? Orientation.Vertical
                : Orientation.Horizontal;

            IEnumerable<ReaderPageItem> pagesToRender;
            if (fitMode == MainWindow.ReaderFitMode.VerticalScroll)
            {
                pagesToRender = pages;
            }
            else if (fitMode == MainWindow.ReaderFitMode.HorizontalScrollRTL)
            {
                pagesToRender = pages.OrderByDescending(item => item.Index);
            }
            else if (fitMode == MainWindow.ReaderFitMode.HorizontalScrollLTR)
            {
                pagesToRender = pages;
            }
            else
            {
                pagesToRender = pages.Where(item => item.Index == activePageIndex);
            }

            foreach (ReaderPageItem page in pagesToRender)
            {
                FrameworkElement element = CreatePageElement(page, fitMode);
                _pageElements.Add(element);
                _stagePanel.Children.Add(element);
            }

            ApplyZoom();
            UpdateViewportAlignment();
            _scrollViewer.UpdateLayout();
            ScrollToPage(activePageIndex);
            _scrollViewer.Focus();
        }

        public void ExecuteScript(string script)
        {
        }

        public void ScrollBoundary(bool goToStart)
        {
            _syncingFromViewport = true;
            try
            {
                MainWindow.ReaderFitMode fitMode = _mainWindow.GetReaderFitMode();
                if (fitMode == MainWindow.ReaderFitMode.HorizontalScrollLTR || fitMode == MainWindow.ReaderFitMode.HorizontalScrollRTL)
                {
                    double offset = fitMode == MainWindow.ReaderFitMode.HorizontalScrollRTL
                        ? (goToStart ? _scrollViewer.ScrollableWidth : 0)
                        : (goToStart ? 0 : _scrollViewer.ScrollableWidth);
                    _scrollViewer.ScrollToHorizontalOffset(offset);
                }
                else
                {
                    _scrollViewer.ScrollToVerticalOffset(goToStart ? 0 : _scrollViewer.ScrollableHeight);
                }
            }
            finally
            {
                _syncingFromViewport = false;
            }
        }

        public void ShowToast(string message)
        {
        }

        public void ApplyBookmarkViewport(ReaderPageBookmarkEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.ReaderZoom > 0)
            {
                _zoom = Math.Max(0.35d, Math.Min(6d, entry.ReaderZoom));
                ApplyZoom();
                _scrollViewer.UpdateLayout();
            }

            int pageIndex = entry.ViewportPageIndex >= 0 ? entry.ViewportPageIndex : Math.Max(0, entry.PageIndex - 1);
            ScrollToAnchor(pageIndex, entry.ViewportPageXRatio, entry.ViewportPageYRatio);
        }

        private FrameworkElement CreatePageElement(ReaderPageItem page, MainWindow.ReaderFitMode fitMode)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(page.FilePath);
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                Tag = page.Index
            };

            if (fitMode == MainWindow.ReaderFitMode.FitHeight)
            {
                image.Height = Math.Max(120, _scrollViewer.ViewportHeight - 36);
            }
            else if (fitMode == MainWindow.ReaderFitMode.ActualSize)
            {
                image.Stretch = Stretch.None;
            }
            else if (fitMode == MainWindow.ReaderFitMode.FitWidth || fitMode == MainWindow.ReaderFitMode.VerticalScroll)
            {
                image.Width = Math.Max(160, _scrollViewer.ViewportWidth - 40);
            }
            else
            {
                image.Width = Math.Max(160, (_scrollViewer.ViewportWidth - 72) / 2d);
            }

            return new Border
            {
                Margin = fitMode == MainWindow.ReaderFitMode.VerticalScroll
                    ? new Thickness(0, 0, 0, 14)
                    : new Thickness(0, 0, 14, 0),
                Child = image,
                Tag = page.Index
            };
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                return;
            }

            e.Handled = true;
            double previousZoom = _zoom;
            _zoom = Math.Max(0.35d, Math.Min(6d, _zoom + (e.Delta > 0 ? 0.12d : -0.12d)));
            if (Math.Abs(_zoom - 1d) < 0.02d)
            {
                _zoom = 1d;
            }

            ApplyZoom();
            _scrollViewer.UpdateLayout();
            if (_mainWindow.GetReaderFitMode() == MainWindow.ReaderFitMode.VerticalScroll ||
                _mainWindow.GetReaderFitMode() == MainWindow.ReaderFitMode.HorizontalScrollLTR ||
                _mainWindow.GetReaderFitMode() == MainWindow.ReaderFitMode.HorizontalScrollRTL)
            {
                PreserveViewportCenterAfterZoom(previousZoom, _zoom);
                UpdatePageFromViewport();
            }
            else
            {
                ScrollToPage(_mainWindow.GetCurrentReaderPageIndex());
            }
        }

        private void ScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            MainWindow.ReaderFitMode fitMode = _mainWindow.GetReaderFitMode();
            if (fitMode != MainWindow.ReaderFitMode.VerticalScroll &&
                fitMode != MainWindow.ReaderFitMode.HorizontalScrollLTR &&
                fitMode != MainWindow.ReaderFitMode.HorizontalScrollRTL)
            {
                return;
            }

            if (_scrollViewer.ScrollableWidth <= 0 && _scrollViewer.ScrollableHeight <= 0)
            {
                return;
            }

            _isMousePanning = true;
            _mousePanStartPoint = e.GetPosition(_scrollViewer);
            _mousePanStartHorizontalOffset = _scrollViewer.HorizontalOffset;
            _mousePanStartVerticalOffset = _scrollViewer.VerticalOffset;
            _mousePanPreviousCursor = Cursor;
            Cursor = Cursors.SizeAll;
            _scrollViewer.CaptureMouse();
            _scrollViewer.Focus();
            e.Handled = true;
        }

        private void ScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isMousePanning)
            {
                return;
            }

            Point currentPoint = e.GetPosition(_scrollViewer);
            Vector delta = currentPoint - _mousePanStartPoint;
            _syncingFromViewport = true;
            try
            {
                _scrollViewer.ScrollToHorizontalOffset(MainWindow.ClampOffset(_mousePanStartHorizontalOffset - delta.X, _scrollViewer.ScrollableWidth));
                _scrollViewer.ScrollToVerticalOffset(MainWindow.ClampOffset(_mousePanStartVerticalOffset - delta.Y, _scrollViewer.ScrollableHeight));
            }
            finally
            {
                _syncingFromViewport = false;
            }

            UpdatePageFromViewport();
            e.Handled = true;
        }

        private void ScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isMousePanning)
            {
                return;
            }

            EndMousePan();
            e.Handled = true;
        }

        private void ScrollViewer_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndMousePan();
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_syncingFromViewport || _pageElements.Count == 0)
            {
                return;
            }

            UpdatePageFromViewport();
        }

        public void ScrollToPage(int pageIndex)
        {
            FrameworkElement target = _pageElements.FirstOrDefault(element => Equals(element.Tag, pageIndex));
            if (target == null)
            {
                return;
            }

            MainWindow.ReaderFitMode fitMode = _mainWindow.GetReaderFitMode();
            _syncingFromViewport = true;
            try
            {
                Point origin = target.TranslatePoint(new Point(0, 0), _stagePanel);
                if (fitMode == MainWindow.ReaderFitMode.VerticalScroll)
                {
                    double verticalOffset = GetCenteredScrollOffset(origin.Y, target.ActualHeight, _scrollViewer.ViewportHeight, _scrollViewer.ScrollableHeight);
                    double horizontalOffset = GetCenteredScrollOffset(origin.X, target.ActualWidth, _scrollViewer.ViewportWidth, _scrollViewer.ScrollableWidth);
                    _scrollViewer.ScrollToVerticalOffset(verticalOffset);
                    _scrollViewer.ScrollToHorizontalOffset(horizontalOffset);
                }
                else if (fitMode == MainWindow.ReaderFitMode.HorizontalScrollLTR || fitMode == MainWindow.ReaderFitMode.HorizontalScrollRTL)
                {
                    double horizontalOffset = GetCenteredScrollOffset(origin.X, target.ActualWidth, _scrollViewer.ViewportWidth, _scrollViewer.ScrollableWidth);
                    double verticalOffset = GetCenteredScrollOffset(origin.Y, target.ActualHeight, _scrollViewer.ViewportHeight, _scrollViewer.ScrollableHeight);
                    _scrollViewer.ScrollToHorizontalOffset(horizontalOffset);
                    _scrollViewer.ScrollToVerticalOffset(verticalOffset);
                }
                else
                {
                    target.BringIntoView();
                }
            }
            finally
            {
                _syncingFromViewport = false;
            }
        }

        private void ScrollToAnchor(int pageIndex, double pageXRatio, double pageYRatio)
        {
            FrameworkElement target = _pageElements.FirstOrDefault(element => Equals(element.Tag, pageIndex));
            if (target == null)
            {
                return;
            }

            _syncingFromViewport = true;
            try
            {
                Point origin = target.TranslatePoint(new Point(0, 0), _stagePanel);
                double targetX = origin.X + (target.ActualWidth * Math.Max(0d, Math.Min(1d, pageXRatio)));
                double targetY = origin.Y + (target.ActualHeight * Math.Max(0d, Math.Min(1d, pageYRatio)));
                _scrollViewer.ScrollToHorizontalOffset(MainWindow.ClampOffset(targetX - (_scrollViewer.ViewportWidth / 2d), _scrollViewer.ScrollableWidth));
                _scrollViewer.ScrollToVerticalOffset(MainWindow.ClampOffset(targetY - (_scrollViewer.ViewportHeight / 2d), _scrollViewer.ScrollableHeight));
            }
            finally
            {
                _syncingFromViewport = false;
            }
        }

        private void ApplyZoom()
        {
            _scaleTransform.ScaleX = _zoom;
            _scaleTransform.ScaleY = _zoom;
            UpdateViewportAlignment();
        }

        private void UpdateViewportAlignment()
        {
            _stagePanel.HorizontalAlignment = _stagePanel.ActualWidth <= _scrollViewer.ViewportWidth
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Left;
            _stagePanel.VerticalAlignment = _stagePanel.ActualHeight <= _scrollViewer.ViewportHeight
                ? VerticalAlignment.Center
                : VerticalAlignment.Top;
        }

        private static double GetCenteredScrollOffset(double origin, double elementSize, double viewportSize, double scrollableSize)
        {
            if (viewportSize <= 0)
            {
                return Math.Max(0, origin);
            }

            double offset = origin - Math.Max(0, (viewportSize - elementSize) / 2d);
            if (double.IsNaN(offset) || double.IsInfinity(offset))
            {
                offset = origin;
            }

            return Math.Max(0, Math.Min(scrollableSize, offset));
        }

        private void PreserveViewportCenterAfterZoom(double oldZoom, double newZoom)
        {
            if (oldZoom <= 0 || newZoom <= 0)
            {
                return;
            }

            double centerX = (_scrollViewer.HorizontalOffset + (_scrollViewer.ViewportWidth / 2d)) / oldZoom;
            double centerY = (_scrollViewer.VerticalOffset + (_scrollViewer.ViewportHeight / 2d)) / oldZoom;

            _syncingFromViewport = true;
            try
            {
                double nextHorizontalOffset = (centerX * newZoom) - (_scrollViewer.ViewportWidth / 2d);
                double nextVerticalOffset = (centerY * newZoom) - (_scrollViewer.ViewportHeight / 2d);
                _scrollViewer.ScrollToHorizontalOffset(MainWindow.ClampOffset(nextHorizontalOffset, _scrollViewer.ScrollableWidth));
                _scrollViewer.ScrollToVerticalOffset(MainWindow.ClampOffset(nextVerticalOffset, _scrollViewer.ScrollableHeight));
            }
            finally
            {
                _syncingFromViewport = false;
            }
        }

        private void UpdatePageFromViewport()
        {
            int bestIndex = MainWindow.GetPageIndexFromViewportCenter(_pageElements, _scrollViewer);
            if (bestIndex >= 0)
            {
                _mainWindow.SyncReaderPageSelectionByIndex(bestIndex);
            }
        }

        private void EndMousePan()
        {
            if (!_isMousePanning)
            {
                return;
            }

            _isMousePanning = false;
            if (_scrollViewer.IsMouseCaptured)
            {
                _scrollViewer.ReleaseMouseCapture();
            }
            Cursor = _mousePanPreviousCursor ?? Cursors.Arrow;
            UpdatePageFromViewport();
        }

        private void ReaderFullscreenWindow_Closed(object sender, EventArgs e)
        {
            _mainWindow.OnFullscreenClosed();
        }

        private void ReaderFullscreenWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.F11 || e.Key == Key.F)
            {
                Close();
                e.Handled = true;
                return;
            }

            _mainWindow.HandleFullscreenHotkey(e);
        }
    }
}

