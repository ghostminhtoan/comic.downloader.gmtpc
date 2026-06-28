using System;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace get_link_manga
{
    [DataContract]
    internal sealed class HakoChapterCaptureResult
    {
        [DataMember(Name = "title")]
        public string ChapterTitle { get; set; }

        [DataMember(Name = "contentHtml")]
        public string ContentHtml { get; set; }

        [DataMember(Name = "bookTitle")]
        public string BookTitle { get; set; }

        [DataMember(Name = "isChallenge")]
        public bool IsChallenge { get; set; }

        [DataMember(Name = "isRateLimited")]
        public bool IsRateLimited { get; set; }
    }

    internal sealed class HakoChapterCaptureWindow : Window
    {
        private sealed class OwnerPlacementSnapshot
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool IsVisible { get; set; }
        }

        private readonly string _targetUrl;
        private readonly bool _isVietnamese;
        private readonly bool _autoFocus;
        private readonly TextBlock _statusText;
        private readonly WebView2 _webView;
        private readonly Action _stopRequested;
        private bool _captureFinished;
        private TaskCompletionSource<HakoChapterCaptureResult> _completionSource;
        private double _savedOpacity = 1d;
        private bool _savedShowInTaskbar;

        internal HakoChapterCaptureResult CaptureResult { get; private set; }

        private HakoChapterCaptureWindow(string targetUrl, bool isVietnamese, bool autoFocus, Action stopRequested)
        {
            _targetUrl = targetUrl ?? string.Empty;
            _isVietnamese = isVietnamese;
            _autoFocus = autoFocus;
            _stopRequested = stopRequested;

            Title = isVietnamese ? "COPY CHAPTER WEBVIEW2" : "CHAPTER COPY WEBVIEW2";
            Width = 960;
            Height = 760;
            MinWidth = 520;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F));
            ShowActivated = autoFocus;
            ShowInTaskbar = !autoFocus;
            PreviewKeyDown += OnPreviewKeyDown;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var topBar = new DockPanel
            {
                Margin = new Thickness(12, 10, 12, 10),
                LastChildFill = true
            };

            var stopButton = new Button
            {
                Content = isVietnamese ? "STOP ALT F2" : "STOP ALT F2",
                MinWidth = 120,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(12, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            stopButton.Click += (sender, args) => RequestStopAndClose();
            DockPanel.SetDock(stopButton, Dock.Right);
            topBar.Children.Add(stopButton);

            _statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)),
                FontWeight = FontWeights.Bold,
                Text = isVietnamese ? "Đang mở chapter..." : "Opening chapter..."
            };
            topBar.Children.Add(_statusText);

            Grid.SetRow(topBar, 0);
            root.Children.Add(topBar);

            _webView = new WebView2();
            Grid.SetRow(_webView, 1);
            root.Children.Add(_webView);

            Content = root;
            Loaded += OnLoadedAsync;
            Closed += OnClosed;
        }

        internal static async Task<HakoChapterCaptureResult> CaptureAsync(Window owner, string targetUrl, bool isVietnamese, bool autoFocus, CancellationToken token, Action stopRequested)
        {
            OwnerPlacementSnapshot ownerSnapshot = null;
            if (owner != null)
            {
                ownerSnapshot = await owner.Dispatcher.InvokeAsync(() => new OwnerPlacementSnapshot
                {
                    Left = owner.Left,
                    Top = owner.Top,
                    Width = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width,
                    Height = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height,
                    IsVisible = owner.IsVisible
                }).Task;
            }

            var completion = new TaskCompletionSource<HakoChapterCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var threadReady = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            HakoChapterCaptureWindow captureWindow = null;

            var thread = new Thread(() =>
            {
                captureWindow = new HakoChapterCaptureWindow(targetUrl, isVietnamese, autoFocus, stopRequested);
                if (ownerSnapshot != null)
                {
                    captureWindow.PlaceNearOwnerSnapshot(ownerSnapshot);
                }
                else
                {
                    captureWindow.PlaceNearOwnerSnapshot(null);
                }

                captureWindow._completionSource = completion;
                captureWindow.Closed += (sender, args) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                threadReady.TrySetResult(Dispatcher.CurrentDispatcher);
                captureWindow.Show();
                Dispatcher.Run();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            Dispatcher captureDispatcher = await threadReady.Task;
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);
            if (token.CanBeCanceled)
            {
                registration = token.Register(() =>
                {
                    try
                    {
                        captureDispatcher.BeginInvoke(new Action(() =>
                        {
                            if (captureWindow != null && captureWindow.IsVisible)
                            {
                                captureWindow.Close();
                            }
                        }));
                    }
                    catch
                    {
                    }
                });
            }

            try
            {
                return await completion.Task;
            }
            finally
            {
                registration.Dispose();
            }
        }

        private void PlaceNearOwnerSnapshot(OwnerPlacementSnapshot owner)
        {
            Rect workArea = new Rect(
                SystemParameters.WorkArea.Left,
                SystemParameters.WorkArea.Top,
                SystemParameters.WorkArea.Width,
                SystemParameters.WorkArea.Height);

            double targetWidth = Math.Min(Width, Math.Max(MinWidth, workArea.Width * 0.42));
            double targetHeight = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 32));
            Width = Math.Min(targetWidth, workArea.Width - 24);
            Height = Math.Min(targetHeight, workArea.Height - 24);

            double left = workArea.Right - Width - 12;
            double top = workArea.Top + 12;

            if (owner != null && owner.IsVisible)
            {
                Rect ownerBounds = new Rect(owner.Left, owner.Top, owner.Width, owner.Height);
                double gap = 12;
                double rightSpace = workArea.Right - ownerBounds.Right - gap;
                double leftSpace = ownerBounds.Left - workArea.Left - gap;
                double belowSpace = workArea.Bottom - ownerBounds.Bottom - gap;
                double aboveSpace = ownerBounds.Top - workArea.Top - gap;

                if (rightSpace >= MinWidth)
                {
                    Width = Math.Min(Width, rightSpace);
                    left = ownerBounds.Right + gap;
                    top = Clamp(ownerBounds.Top, workArea.Top + 8, workArea.Bottom - Height - 8);
                }
                else if (leftSpace >= MinWidth)
                {
                    Width = Math.Min(Width, leftSpace);
                    left = ownerBounds.Left - Width - gap;
                    top = Clamp(ownerBounds.Top, workArea.Top + 8, workArea.Bottom - Height - 8);
                }
                else if (belowSpace >= MinHeight)
                {
                    Height = Math.Min(Height, belowSpace);
                    top = ownerBounds.Bottom + gap;
                    left = Clamp(ownerBounds.Left, workArea.Left + 8, workArea.Right - Width - 8);
                }
                else if (aboveSpace >= MinHeight)
                {
                    Height = Math.Min(Height, aboveSpace);
                    top = ownerBounds.Top - Height - gap;
                    left = Clamp(ownerBounds.Left, workArea.Left + 8, workArea.Right - Width - 8);
                }
            }

            Left = Clamp(left, workArea.Left + 8, workArea.Right - Width - 8);
            Top = Clamp(top, workArea.Top + 8, workArea.Bottom - Height - 8);
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
            {
                return min;
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2 && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                e.Handled = true;
                RequestStopAndClose();
            }
        }

        private void RequestStopAndClose()
        {
            try
            {
                _stopRequested?.Invoke();
            }
            catch
            {
            }

            if (IsVisible)
            {
                Close();
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _completionSource?.TrySetResult(CaptureResult);
        }

        private async void OnLoadedAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_autoFocus)
                {
                    ApplyStealthWindowMode(true);
                }

                var env = await CoreWebView2Environment.CreateAsync(
                    null,
                    Path.Combine(PortablePaths.WebView2UserDataFolder, "hako-chapter-copy"),
                    new CoreWebView2EnvironmentOptions());
                await _webView.EnsureCoreWebView2Async(env);
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                _webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted;
                _webView.Source = new Uri(_targetUrl);
            }
            catch
            {
                Close();
            }
        }

        private async void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess || _captureFinished || _webView.CoreWebView2 == null)
            {
                return;
            }

            _statusText.Text = _isVietnamese ? "Đang bỏ chặn copy và lấy text..." : "Unlocking copy and extracting text...";

            try
            {
                await _webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
  if (!document.getElementById('gmtpc-copy-unlock')) {
    const style = document.createElement('style');
    style.id = 'gmtpc-copy-unlock';
    style.textContent = '*{user-select:text !important;-webkit-user-select:text !important} #chapter-content,.chapter-content,.long-text{user-select:text !important;-webkit-user-select:text !important;}';
    document.documentElement.appendChild(style);
  }
})()");
            }
            catch
            {
            }

            for (int attempt = 0; attempt < 75; attempt++)
            {
                if (_captureFinished || _webView.CoreWebView2 == null)
                {
                    return;
                }

                try
                {
                    string json = await _webView.CoreWebView2.ExecuteScriptAsync(@"
(() => {
  const titleNode = document.querySelector('.title-top h4') || document.querySelector('.title-top h2') || document.querySelector('.title-top h6');
  const contentNode = document.querySelector('#chapter-content') || document.querySelector('.chapter-content') || document.querySelector('.long-text');
  const bookNode =
    document.querySelector('.rd_sidebar-name h5 a') ||
    document.querySelector('.series-name') ||
    document.querySelector('.series-name-group .series-name') ||
    document.querySelector('a[href^=""/truyen/""][title]') ||
    document.querySelector('a[href^=""/sang-tac/""][title]') ||
    document.querySelector('a[href^=""/truyen/""]') ||
    document.querySelector('a[href^=""/sang-tac/""]');
  const html = document.documentElement ? document.documentElement.outerHTML : '';
  const bodyText = document.body ? document.body.innerText : '';
  const isChallenge = /cf-turnstile|cf-challenge|cloudflare|just a moment|captcha|xác minh/i.test(html) || /just a moment|captcha|xác minh/i.test(bodyText);
  const isRateLimited = /429|too many requests|quá nhiều yêu cầu|qua nhieu yeu cau/i.test(html) || /429|too many requests|quá nhiều yêu cầu|qua nhieu yeu cau/i.test(bodyText);
  return {
    title: titleNode ? (titleNode.innerText || '').trim() : '',
    contentHtml: contentNode ? contentNode.innerHTML : '',
    bookTitle: bookNode ? (((bookNode.getAttribute && bookNode.getAttribute('title')) || bookNode.innerText || '').trim()) : '',
    isChallenge: isChallenge,
    isRateLimited: isRateLimited
  };
})()");

                    HakoChapterCaptureResult result = DeserializeJson<HakoChapterCaptureResult>(json);
                    if (result != null && !string.IsNullOrWhiteSpace(result.ContentHtml))
                    {
                        CaptureResult = result;
                        _captureFinished = true;
                        Close();
                        return;
                    }

                    if (result != null && result.IsRateLimited)
                    {
                        CaptureResult = result;
                        _captureFinished = true;
                        _statusText.Text = _isVietnamese
                            ? "Dính 429. App nghỉ 10 giây rồi tự thử lại."
                            : "429 hit. App waits 10 seconds then retries.";
                        Close();
                        return;
                    }

                    if (result != null && result.IsChallenge)
                    {
                        if (!_autoFocus)
                        {
                            try
                            {
                                Activate();
                            }
                            catch
                            {
                            }
                        }

                        _statusText.Text = _isVietnamese
                            ? "Phát hiện captcha. Hãy vượt captcha, xong app tự đóng chap."
                            : "Captcha detected. Solve it, then app auto closes chapter.";
                    }
                    else if (attempt == 0 || attempt % 4 == 0)
                    {
                        _statusText.Text = _isVietnamese
                            ? $"Đang chờ nội dung chapter... ({attempt + 1}/90)"
                            : $"Waiting chapter content... ({attempt + 1}/90)";
                    }
                }
                catch
                {
                }

                await Task.Delay(800);
            }

            Close();
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as T;
            }
        }

        private void ApplyStealthWindowMode(bool enabled)
        {
            if (enabled)
            {
                _savedOpacity = Opacity;
                _savedShowInTaskbar = ShowInTaskbar;
                ShowInTaskbar = false;
                Opacity = 0d;
                Left = -10000;
                Top = -10000;
                return;
            }

            Opacity = _savedOpacity <= 0d ? 1d : _savedOpacity;
            ShowInTaskbar = _savedShowInTaskbar;
        }
    }
}
