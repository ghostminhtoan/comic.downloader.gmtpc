using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        private const string MangadexAuthLoginUrl = "https://auth.mangadex.org/realms/mangadex/login-actions/authenticate?client_id=mangadex-frontend-stable";
        private static volatile bool _isDamconuongLoginWindowActive;
        private static volatile bool _isMangadexLoginWindowActive;
        private DamconuongLoginWindow _damconuongLoginWindow;
        private DamconuongLoginWindow _mangadexLoginWindow;
        private readonly Dictionary<string, PasswordManagerEntry> _passwordManagerEntries = new Dictionary<string, PasswordManagerEntry>(StringComparer.OrdinalIgnoreCase);
        private bool _suppressPasswordManagerEvents;
        private bool _showDamconuongPasswordManagerPassword;
        private bool _showMangadexPasswordManagerPassword;
        private readonly List<string> _mangadexPreferredTranslatedLanguages = new List<string>();

        private async void BtnDamconuongLogin_Click(object sender, RoutedEventArgs e)
        {
            string preferredUrl = txtDamconuongTagUrl?.Text?.Trim();
            string loginEmail = txtDamconuongLoginEmail?.Text?.Trim() ?? string.Empty;
            string loginPassword = txtDamconuongLoginPassword?.Password ?? string.Empty;
            try
            {
                if (!IsDamconuongUrl(preferredUrl))
                {
                    preferredUrl = DamconuongBaseUrl;
                }

                await OpenDamconuongLoginAsync(preferredUrl, loginEmail, loginPassword);
            }
            catch (Exception ex)
            {
                lblStatus.Text = (_isVietnameseUi ? "Login damconuong lỗi: " : "damconuong login failed: ") + ex.Message;
                DamconuongLog("Lỗi login: " + ex.Message);
            }
        }

        private async void BtnMangadexLogin_Click(object sender, RoutedEventArgs e)
        {
            string preferredUrl = txtMangadexTagUrl?.Text?.Trim();
            string loginEmail = txtMangadexLoginEmail?.Text?.Trim() ?? string.Empty;
            string loginPassword = txtMangadexLoginPassword?.Password ?? string.Empty;
            try
            {
                if (!IsMangadexUrl(preferredUrl))
                {
                    preferredUrl = MangadexBaseUrl;
                }

                await OpenMangadexLoginAsync(preferredUrl, loginEmail, loginPassword);
            }
            catch (Exception ex)
            {
                lblStatus.Text = (_isVietnameseUi ? "Login MangaDex lỗi: " : "MangaDex login failed: ") + ex.Message;
                MangadexLog("Lỗi login: " + ex.Message);
            }
        }

        private async void BtnPasswordManagerApplyDomain_Click(object sender, RoutedEventArgs e)
        {
            string domain = (sender as FrameworkElement)?.Tag?.ToString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain))
            {
                return;
            }

            await ApplyPasswordManagerEntryAsync(domain);
        }

        private async void BtnPasswordManagerApplyAll_Click(object sender, RoutedEventArgs e)
        {
            string[] supportedDomains = { "damconuong.shop", "mangadex.org" };
            int applied = 0;
            int failed = 0;

            foreach (string domain in supportedDomains)
            {
                if (!TryGetPasswordManagerEntryValues(domain, out string username, out string password, false))
                {
                    continue;
                }

                try
                {
                    if (await ApplyPasswordManagerCredentialsAsync(domain, username, password))
                    {
                        applied++;
                    }
                }
                catch
                {
                    failed++;
                }
            }

            if (applied == 0 && failed == 0)
            {
                lblStatus.Text = _isVietnameseUi ? "Chưa có domain nào đủ username/password để apply." : "No domain has enough username/password to apply.";
                SelectPasswordManagerTab();
                return;
            }

            SelectPasswordManagerTab();
            lblStatus.Text = _isVietnameseUi
                ? $"Apply all xong. Thành công: {applied}. Lỗi: {failed}."
                : $"Apply all finished. Success: {applied}. Failed: {failed}.";
        }

        private void InitializePasswordManagerControls()
        {
            LoadPasswordManagerSettings();
            LoadAllPasswordManagerEntriesToUi();
            UpdatePasswordManagerPasswordVisibility("damconuong.shop");
            UpdatePasswordManagerPasswordVisibility("mangadex.org");
            UpdatePasswordManagerLanguage();
        }

        private string GetPasswordManagerSettingsPath()
        {
            return Path.Combine(PortablePaths.PortableDataRoot, "autosave_password.md");
        }

        private string GetLegacyPasswordManagerSettingsPath()
        {
            return Path.Combine(PortablePaths.PortableDataRoot, "password-manager.txt");
        }

        private bool TryGetPasswordManagerEntryValues(string domain, out string username, out string password, bool requireComplete)
        {
            username = string.Empty;
            password = string.Empty;
            if (string.IsNullOrWhiteSpace(domain))
            {
                return false;
            }

            if (TryGetPasswordManagerControls(domain, out TextBox usernameBox, out PasswordBox passwordBox, out TextBox passwordVisibleBox, out Button _))
            {
                username = usernameBox?.Text?.Trim() ?? string.Empty;
                password = GetPasswordManagerPasswordValue(domain);
            }
            else if (_passwordManagerEntries.TryGetValue(domain, out PasswordManagerEntry entry))
            {
                username = entry?.Username?.Trim() ?? string.Empty;
                password = entry?.Password ?? string.Empty;
            }

            return !requireComplete || (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password));
        }

        private async Task ApplyPasswordManagerEntryAsync(string domain)
        {
            try
            {
                if (!TryGetPasswordManagerEntryValues(domain, out string username, out string password, true))
                {
                    lblStatus.Text = _isVietnameseUi
                        ? $"Thiếu username/password cho {domain}."
                        : $"Missing username/password for {domain}.";
                    return;
                }

                await ApplyPasswordManagerCredentialsAsync(domain, username, password);
            }
            catch (Exception ex)
            {
                lblStatus.Text = (_isVietnameseUi ? "Apply password lỗi: " : "Password apply failed: ") + ex.Message;
                DamconuongLog("Lỗi apply password: " + ex.Message);
            }
        }

        private async Task<bool> ApplyPasswordManagerCredentialsAsync(string domain, string username, string password)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                lblStatus.Text = _isVietnameseUi ? "Tab password chưa có domain." : "Password tab has no domain.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                lblStatus.Text = _isVietnameseUi ? "Thiếu username hoặc password ở tab password." : "Missing username or password in password tab.";
                return false;
            }

            if (string.Equals(domain, "damconuong.shop", StringComparison.OrdinalIgnoreCase))
            {
                if (tabLeftPanel != null && tabHentaiSourceRootItem != null)
                {
                    tabLeftPanel.SelectedItem = tabHentaiSourceRootItem;
                }

                if (tabHentai != null && tabDamconuongItem != null)
                {
                    tabHentai.SelectedItem = tabDamconuongItem;
                }

                if (txtDamconuongLoginEmail != null)
                {
                    txtDamconuongLoginEmail.Text = username;
                }

                if (txtDamconuongLoginPassword != null)
                {
                    txtDamconuongLoginPassword.Password = password;
                }

                await OpenDamconuongLoginAsync(txtDamconuongTagUrl?.Text?.Trim(), username, password);
                return true;
            }

            if (string.Equals(domain, "mangadex.org", StringComparison.OrdinalIgnoreCase))
            {
                if (tabLeftPanel != null && tabMangaSourceRootItem != null)
                {
                    tabLeftPanel.SelectedItem = tabMangaSourceRootItem;
                }

                if (tabManga != null && tabMangadexItem != null)
                {
                    tabManga.SelectedItem = tabMangadexItem;
                }

                if (txtMangadexLoginEmail != null)
                {
                    txtMangadexLoginEmail.Text = username;
                }

                if (txtMangadexLoginPassword != null)
                {
                    txtMangadexLoginPassword.Password = password;
                }

                await OpenMangadexLoginAsync(txtMangadexTagUrl?.Text?.Trim(), username, password);
                return true;
            }

            lblStatus.Text = _isVietnameseUi ? $"Chưa hỗ trợ auto login cho {domain}." : $"Auto login not supported for {domain} yet.";
            return false;
        }

        private void LoadPasswordManagerSettings()
        {
            string settingsPath = GetPasswordManagerSettingsPath();
            if (File.Exists(settingsPath))
            {
                LoadPasswordManagerSettingsFromMarkdown(File.ReadAllText(settingsPath, Encoding.UTF8));
                return;
            }

            string legacyPath = GetLegacyPasswordManagerSettingsPath();
            if (!File.Exists(legacyPath))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(legacyPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split('|');
                if (parts.Length < 3)
                {
                    continue;
                }

                string domain = parts[0]?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(domain))
                {
                    continue;
                }

                _passwordManagerEntries[domain] = new PasswordManagerEntry
                {
                    Username = Uri.UnescapeDataString(parts[1] ?? string.Empty),
                    Password = Uri.UnescapeDataString(parts[2] ?? string.Empty)
                };
            }

            SavePasswordManagerSettings();
        }

        private void SavePasswordManagerSettings()
        {
            string settingsPath = GetPasswordManagerSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            File.WriteAllText(settingsPath, BuildPasswordManagerMarkdown(), Encoding.UTF8);
        }

        private string BuildPasswordManagerMarkdown()
        {
            var lines = new List<string>
            {
                "# autosave_password",
                string.Empty
            };

            foreach (var pair in _passwordManagerEntries
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"## {pair.Key}");
                lines.Add($"- Username: {pair.Value?.Username ?? string.Empty}");
                lines.Add($"- Password: {pair.Value?.Password ?? string.Empty}");
                lines.Add(string.Empty);
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void LoadPasswordManagerSettingsFromMarkdown(string content)
        {
            _passwordManagerEntries.Clear();

            string currentDomain = string.Empty;
            foreach (string rawLine in (content ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = rawLine?.Trim() ?? string.Empty;
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    currentDomain = line.Substring(3).Trim();
                    if (!string.IsNullOrWhiteSpace(currentDomain) && !_passwordManagerEntries.ContainsKey(currentDomain))
                    {
                        _passwordManagerEntries[currentDomain] = new PasswordManagerEntry();
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(currentDomain) || !_passwordManagerEntries.ContainsKey(currentDomain))
                {
                    continue;
                }

                if (line.StartsWith("- Username:", StringComparison.OrdinalIgnoreCase))
                {
                    _passwordManagerEntries[currentDomain].Username = line.Substring("- Username:".Length).Trim();
                    continue;
                }

                if (line.StartsWith("- Password:", StringComparison.OrdinalIgnoreCase))
                {
                    _passwordManagerEntries[currentDomain].Password = line.Substring("- Password:".Length).Trim();
                }
            }
        }

        private void PersistPasswordManagerCurrentEntry()
        {
            if (_suppressPasswordManagerEvents)
            {
                return;
            }
        }

        private void LoadPasswordManagerEntryToUi(string domain)
        {
            _suppressPasswordManagerEvents = true;
            try
            {
                if (!_passwordManagerEntries.TryGetValue(domain ?? string.Empty, out PasswordManagerEntry entry))
                {
                    entry = new PasswordManagerEntry();
                }

                if (TryGetPasswordManagerControls(domain, out TextBox usernameBox, out PasswordBox passwordBox, out TextBox passwordVisibleBox, out Button _))
                {
                    usernameBox.Text = entry.Username ?? string.Empty;
                    passwordBox.Password = entry.Password ?? string.Empty;
                    passwordVisibleBox.Text = entry.Password ?? string.Empty;
                }
            }
            finally
            {
                _suppressPasswordManagerEvents = false;
            }
        }

        private void LoadAllPasswordManagerEntriesToUi()
        {
            LoadPasswordManagerEntryToUi("damconuong.shop");
            LoadPasswordManagerEntryToUi("mangadex.org");
        }

        private string GetPasswordManagerPasswordValue(string domain)
        {
            if (!TryGetPasswordManagerControls(domain, out TextBox _, out PasswordBox passwordBox, out TextBox passwordVisibleBox, out Button _))
            {
                return string.Empty;
            }

            return GetPasswordManagerShowPassword(domain)
                ? passwordVisibleBox?.Text ?? string.Empty
                : passwordBox?.Password ?? string.Empty;
        }

        private void UpdatePasswordManagerPasswordVisibility(string domain)
        {
            if (!TryGetPasswordManagerControls(domain, out TextBox _, out PasswordBox passwordBox, out TextBox passwordVisibleBox, out Button toggleButton))
            {
                return;
            }

            string password = GetPasswordManagerPasswordValue(domain);
            bool showPassword = GetPasswordManagerShowPassword(domain);
            _suppressPasswordManagerEvents = true;
            try
            {
                passwordBox.Password = password;
                passwordVisibleBox.Text = password;
                passwordBox.Visibility = showPassword ? Visibility.Collapsed : Visibility.Visible;
                passwordVisibleBox.Visibility = showPassword ? Visibility.Visible : Visibility.Collapsed;
                toggleButton.Content = CreatePasswordVisibilityIcon(showPassword);
                toggleButton.ToolTip = _isVietnameseUi
                    ? (showPassword ? "Ẩn mật khẩu" : "Hiện mật khẩu")
                    : (showPassword ? "Hide password" : "Show password");
            }
            finally
            {
                _suppressPasswordManagerEvents = false;
            }
        }

        private UIElement CreatePasswordVisibilityIcon(bool showPassword)
        {
            Brush strokeBrush = TryFindResource("CyberpunkTextBrush") as Brush ?? Brushes.White;
            var canvas = new Canvas
            {
                Width = 18,
                Height = 18
            };

            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Stroke = strokeBrush,
                StrokeThickness = 1.6,
                Data = Geometry.Parse("M1.5,9 C4.5,3.8 8.2,2.2 11.8,2.2 C15.4,2.2 19.1,3.8 22.1,9 C19.1,14.2 15.4,15.8 11.8,15.8 C8.2,15.8 4.5,14.2 1.5,9 Z"),
                Stretch = Stretch.Fill,
                Width = 18,
                Height = 18
            });

            canvas.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 4.6,
                Height = 4.6,
                Fill = strokeBrush
            });
            Canvas.SetLeft(canvas.Children[1], 6.7);
            Canvas.SetTop(canvas.Children[1], 6.7);

            if (!showPassword)
            {
                canvas.Children.Add(new System.Windows.Shapes.Line
                {
                    X1 = 3.1,
                    Y1 = 15.2,
                    X2 = 15.2,
                    Y2 = 3.1,
                    Stroke = strokeBrush,
                    StrokeThickness = 1.8,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }

            return new Viewbox
            {
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                Child = canvas
            };
        }

        private void PersistPasswordManagerEntry(string domain)
        {
            if (_suppressPasswordManagerEvents || string.IsNullOrWhiteSpace(domain))
            {
                return;
            }

            if (!TryGetPasswordManagerControls(domain, out TextBox usernameBox, out PasswordBox _, out TextBox _, out Button _))
            {
                return;
            }

            _passwordManagerEntries[domain] = new PasswordManagerEntry
            {
                Username = usernameBox?.Text?.Trim() ?? string.Empty,
                Password = GetPasswordManagerPasswordValue(domain)
            };

            SavePasswordManagerSettings();
        }

        private bool TryGetPasswordManagerControls(string domain, out TextBox usernameBox, out PasswordBox passwordBox, out TextBox passwordVisibleBox, out Button toggleButton)
        {
            usernameBox = null;
            passwordBox = null;
            passwordVisibleBox = null;
            toggleButton = null;

            if (string.Equals(domain, "damconuong.shop", StringComparison.OrdinalIgnoreCase))
            {
                usernameBox = txtPasswordManagerDamconuongUsername;
                passwordBox = txtPasswordManagerDamconuongPassword;
                passwordVisibleBox = txtPasswordManagerDamconuongPasswordVisible;
                toggleButton = btnPasswordManagerDamconuongToggleVisibility;
                return usernameBox != null && passwordBox != null && passwordVisibleBox != null && toggleButton != null;
            }

            if (string.Equals(domain, "mangadex.org", StringComparison.OrdinalIgnoreCase))
            {
                usernameBox = txtPasswordManagerMangadexUsername;
                passwordBox = txtPasswordManagerMangadexPassword;
                passwordVisibleBox = txtPasswordManagerMangadexPasswordVisible;
                toggleButton = btnPasswordManagerMangadexToggleVisibility;
                return usernameBox != null && passwordBox != null && passwordVisibleBox != null && toggleButton != null;
            }

            return false;
        }

        private bool GetPasswordManagerShowPassword(string domain)
        {
            if (string.Equals(domain, "mangadex.org", StringComparison.OrdinalIgnoreCase))
            {
                return _showMangadexPasswordManagerPassword;
            }

            return _showDamconuongPasswordManagerPassword;
        }

        private void SetPasswordManagerShowPassword(string domain, bool value)
        {
            if (string.Equals(domain, "mangadex.org", StringComparison.OrdinalIgnoreCase))
            {
                _showMangadexPasswordManagerPassword = value;
                return;
            }

            _showDamconuongPasswordManagerPassword = value;
        }

        private void UpdatePasswordManagerLanguage()
        {
            if (txtPasswordManagerTitle != null)
            {
                txtPasswordManagerTitle.Text = _isVietnameseUi ? "QUẢN LÝ MẬT KHẨU" : "PASSWORD MANAGER";
            }

            if (txtPasswordManagerDomainLabel != null)
            {
                txtPasswordManagerDomainLabel.Text = _isVietnameseUi ? "MIỀN" : "DOMAIN";
            }

            if (txtPasswordManagerUsernameLabel != null)
            {
                txtPasswordManagerUsernameLabel.Text = _isVietnameseUi ? "TÊN ĐĂNG NHẬP" : "USERNAME";
            }

            if (txtPasswordManagerPasswordLabel != null)
            {
                txtPasswordManagerPasswordLabel.Text = _isVietnameseUi ? "MẬT KHẨU" : "PASSWORD";
            }

            if (txtPasswordManagerHelpText != null)
            {
                txtPasswordManagerHelpText.Text = _isVietnameseUi
                    ? "Nút APPLY từng domain sẽ đẩy username/password sang tab source tương ứng, mở login, rồi tab source tự xóa ô login sau khi hoàn tất. Password sẽ autosave vào autosave_password.md."
                    : "Per-domain APPLY buttons send username/password to the matching source tab, open login, then the source tab clears its login boxes after completion. Password autosaves to autosave_password.md.";
            }

            if (btnPasswordManagerImport != null)
            {
                btnPasswordManagerImport.Content = _isVietnameseUi ? "IMPORT" : "IMPORT";
            }

            if (btnPasswordManagerExport != null)
            {
                btnPasswordManagerExport.Content = _isVietnameseUi ? "EXPORT" : "EXPORT";
            }

            if (btnPasswordManagerApplyDamconuong != null)
            {
                btnPasswordManagerApplyDamconuong.Content = _isVietnameseUi ? "ÁP DỤNG" : "APPLY";
            }

            if (btnPasswordManagerApplyMangadex != null)
            {
                btnPasswordManagerApplyMangadex.Content = _isVietnameseUi ? "ÁP DỤNG" : "APPLY";
            }

            if (btnPasswordManagerApplyAll != null)
            {
                btnPasswordManagerApplyAll.Content = _isVietnameseUi ? "ÁP DỤNG TẤT CẢ" : "APPLY ALL";
            }

            if (txtDamconuongLoginEmailLabel != null)
            {
                txtDamconuongLoginEmailLabel.Text = _isVietnameseUi ? "EMAIL ĐĂNG NHẬP" : "LOGIN EMAIL";
            }

            if (txtDamconuongLoginPasswordLabel != null)
            {
                txtDamconuongLoginPasswordLabel.Text = _isVietnameseUi ? "MẬT KHẨU ĐĂNG NHẬP" : "LOGIN PASSWORD";
            }

            if (btnDamconuongLogin != null)
            {
                btnDamconuongLogin.Content = _isVietnameseUi ? "ĐĂNG NHẬP" : "LOGIN";
            }

            if (txtDamconuongRedirectDomain != null)
            {
                txtDamconuongRedirectDomain.Visibility = Visibility.Visible;
            }

            if (txtMangadexLoginEmailLabel != null)
            {
                txtMangadexLoginEmailLabel.Text = _isVietnameseUi ? "EMAIL ĐĂNG NHẬP" : "LOGIN EMAIL";
            }

            if (txtMangadexLoginPasswordLabel != null)
            {
                txtMangadexLoginPasswordLabel.Text = _isVietnameseUi ? "MẬT KHẨU ĐĂNG NHẬP" : "LOGIN PASSWORD";
            }

            if (btnMangadexLogin != null)
            {
                btnMangadexLogin.Content = _isVietnameseUi ? "ĐĂNG NHẬP" : "LOGIN";
            }

            if (txtMangadexDownloadLanguagesLabel != null)
            {
                txtMangadexDownloadLanguagesLabel.Text = _isVietnameseUi ? "NGÔN NGỮ TẢI" : "DOWNLOAD LANGUAGES";
            }

            if (chkMangadexLangVi != null)
            {
                chkMangadexLangVi.Content = _isVietnameseUi ? "Tiếng Việt" : "Vietnamese";
            }

            if (chkMangadexLangEn != null)
            {
                chkMangadexLangEn.Content = _isVietnameseUi ? "Tiếng Anh" : "English";
            }

            if (chkMangadexLangJa != null)
            {
                chkMangadexLangJa.Content = _isVietnameseUi ? "Tiếng Nhật" : "Japanese";
            }

            if (txtMangadexNoPaginationLabel != null)
            {
                txtMangadexNoPaginationLabel.Text = _isVietnameseUi ? "WEB KHÔNG HỖ TRỢ PHÂN TRANG" : "WEBSITE DOES NOT SUPPORT PAGINATION";
            }

            if (txtDamconuongHelpText != null)
            {
                txtDamconuongHelpText.Text = _isVietnameseUi
                    ? "Hỗ trợ category, book, chapter. Category tự đi page 1 -> cuối. Chapter chỉ lấy ảnh trong #chapter-content / reading-detail box_doc."
                    : "Supports category, book, and chapter. Category auto walks page 1 -> last. Chapters only read images from #chapter-content / reading-detail box_doc.";
            }

            if (txtMangadexPageHintText != null)
            {
                txtMangadexPageHintText.Text = _isVietnameseUi
                    ? "Category hiện tổng page. Book/chapter cũng hiện page number."
                    : "Category shows total pages. Book/chapter also show page number.";
            }

            UpdatePasswordManagerPasswordVisibility("damconuong.shop");
            UpdatePasswordManagerPasswordVisibility("mangadex.org");
        }

        private void DamconuongLoginInput_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowInfo(_isVietnameseUi ? "Đăng nhập ở tab password." : "Log in from the password tab.", _isVietnameseUi ? "Thông báo" : "Info");
        }

        private void DamconuongLoginInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            ShowInfo(_isVietnameseUi ? "Đăng nhập ở tab password." : "Log in from the password tab.", _isVietnameseUi ? "Thông báo" : "Info");
        }

        private void MangadexLoginInput_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            ShowInfo(_isVietnameseUi ? "Đăng nhập ở tab password." : "Log in from the password tab.", _isVietnameseUi ? "Thông báo" : "Info");
        }

        private void MangadexLoginInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            ShowInfo(_isVietnameseUi ? "Đăng nhập ở tab password." : "Log in from the password tab.", _isVietnameseUi ? "Thông báo" : "Info");
        }

        private void TxtPasswordManagerDomainUsername_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressPasswordManagerEvents)
            {
                return;
            }

            PersistPasswordManagerEntry((sender as FrameworkElement)?.Tag?.ToString());
        }

        private void TxtPasswordManagerDomainPassword_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressPasswordManagerEvents)
            {
                return;
            }

            string domain = (sender as FrameworkElement)?.Tag?.ToString();
            if (TryGetPasswordManagerControls(domain, out TextBox _, out PasswordBox passwordBox, out TextBox passwordVisibleBox, out Button _))
            {
                _suppressPasswordManagerEvents = true;
                passwordVisibleBox.Text = passwordBox?.Password ?? string.Empty;
                _suppressPasswordManagerEvents = false;
            }

            PersistPasswordManagerEntry(domain);
        }

        private void TxtPasswordManagerDomainPasswordVisible_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressPasswordManagerEvents)
            {
                return;
            }

            string domain = (sender as FrameworkElement)?.Tag?.ToString();
            if (TryGetPasswordManagerControls(domain, out TextBox _, out PasswordBox passwordBox, out TextBox passwordVisibleBox, out Button _))
            {
                _suppressPasswordManagerEvents = true;
                passwordBox.Password = passwordVisibleBox?.Text ?? string.Empty;
                _suppressPasswordManagerEvents = false;
            }

            PersistPasswordManagerEntry(domain);
        }

        private void BtnPasswordManagerDomainToggleVisibility_Click(object sender, RoutedEventArgs e)
        {
            string domain = (sender as FrameworkElement)?.Tag?.ToString();
            SetPasswordManagerShowPassword(domain, !GetPasswordManagerShowPassword(domain));
            UpdatePasswordManagerPasswordVisibility(domain);
        }

        private void BtnPasswordManagerExport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Markdown password (*.md)|*.md",
                FileName = "autosave_password.md"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            SavePasswordManagerSettings();
            File.WriteAllText(dialog.FileName, BuildPasswordManagerMarkdown(), Encoding.UTF8);
            lblStatus.Text = _isVietnameseUi ? "Đã export password." : "Passwords exported.";
        }

        private void BtnPasswordManagerImport_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Markdown password (*.md)|*.md"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            LoadPasswordManagerSettingsFromMarkdown(File.ReadAllText(dialog.FileName, Encoding.UTF8));
            LoadAllPasswordManagerEntriesToUi();
            SavePasswordManagerSettings();
            lblStatus.Text = _isVietnameseUi ? "Đã import password." : "Passwords imported.";
        }

        private async Task OpenDamconuongLoginAsync(string targetUrl, string loginEmail, string loginPassword)
        {
            if (_isDamconuongLoginWindowActive)
            {
                if (_damconuongLoginWindow == null)
                {
                    _isDamconuongLoginWindowActive = false;
                }
                else
                {
                    _damconuongLoginWindow.Activate();
                }
            }

            try
            {
                DamconuongLoginWindow loginWindow = await EnsureDamconuongLoginWindowAsync(targetUrl);
                _isDamconuongLoginWindowActive = true;
                lblStatus.Text = _isVietnameseUi ? "Đang mở login damconuong.shop..." : "Opening damconuong.shop login...";

                if (!string.IsNullOrWhiteSpace(loginEmail) && !string.IsNullOrWhiteSpace(loginPassword))
                {
                    bool applied = await loginWindow.ApplyCredentialsAsync(loginEmail, loginPassword, true, true);
                    bool authenticated = applied && await loginWindow.WaitForAuthenticatedSessionAsync(true, true);
                    if (authenticated)
                    {
                        await loginWindow.CompleteAndCloseAsync();
                        if (loginWindow.WasCompleted)
                        {
                            SyncDamconuongLoginState(loginWindow);
                            int refreshedBooks = await RefreshDamconuongBlockedBookNamesAfterLoginAsync(_downloadCts?.Token ?? CancellationToken.None);
                            ClearDamconuongLoginInputs();
                            SelectPasswordManagerTab();
                            lblStatus.Text = _isVietnameseUi
                                ? $"Đã auto login damconuong.shop. Đã quét lại tên {refreshedBooks} truyện."
                                : $"damconuong.shop auto login completed. Refreshed {refreshedBooks} book names.";
                            DamconuongLog($"Auto login thành công bằng flow HOÀN TẤT. Đã quét lại tên {refreshedBooks} truyện và xóa login email/password ở tab source.");
                            return;
                        }
                    }
                }

                if (await loginWindow.ShowNonBlockingAsync())
                {
                    SyncDamconuongLoginState(loginWindow);
                    int refreshedBooks = await RefreshDamconuongBlockedBookNamesAfterLoginAsync(_downloadCts?.Token ?? CancellationToken.None);
                    ClearDamconuongLoginInputs();
                    SelectPasswordManagerTab();
                    lblStatus.Text = _isVietnameseUi
                        ? $"Đã đồng bộ phiên login damconuong.shop. Đã quét lại tên {refreshedBooks} truyện."
                        : $"damconuong.shop login session synced. Refreshed {refreshedBooks} book names.";
                    DamconuongLog($"Đồng bộ cookie và user-agent từ cửa sổ login thành công. Đã quét lại tên {refreshedBooks} truyện.");
                }
                else
                {
                    lblStatus.Text = _isVietnameseUi ? "Đã hủy login damconuong.shop." : "damconuong.shop login cancelled.";
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = (_isVietnameseUi ? "Login damconuong lỗi: " : "damconuong login failed: ") + ex.Message;
                DamconuongLog("Lỗi login: " + ex.Message);
            }
            finally
            {
                _isDamconuongLoginWindowActive = false;
                if (_damconuongLoginWindow != null && !_damconuongLoginWindow.IsVisible)
                {
                    _damconuongLoginWindow = null;
                }
            }
        }

        private async Task OpenMangadexLoginAsync(string targetUrl, string loginEmail, string loginPassword)
        {
            string preferredTargetUrl = IsMangadexUrl(targetUrl) ? NormalizeMangadexUrl(targetUrl) : MangadexBaseUrl;

            if (_isMangadexLoginWindowActive)
            {
                if (_mangadexLoginWindow == null)
                {
                    _isMangadexLoginWindowActive = false;
                }
                else
                {
                    _mangadexLoginWindow.Activate();
                }
            }

            try
            {
                DamconuongLoginWindow loginWindow = await EnsureMangadexLoginWindowAsync(targetUrl);
                _isMangadexLoginWindowActive = true;
                lblStatus.Text = _isVietnameseUi ? "Đang mở login MangaDex..." : "Opening MangaDex login...";

                if (!string.IsNullOrWhiteSpace(loginEmail) && !string.IsNullOrWhiteSpace(loginPassword))
                {
                    bool applied = await loginWindow.ApplyCredentialsAsync(loginEmail, loginPassword);
                    bool authenticated = applied && await loginWindow.WaitForAuthenticatedSessionAsync();
                    if (authenticated)
                    {
                        await loginWindow.NavigateIfNeededAsync(preferredTargetUrl);
                        await Task.Delay(1200);
                        SyncMangadexLoginState(loginWindow);
                        SetMangadexPreferredTranslatedLanguages(loginWindow.SelectedTranslatedLanguages);
                        ClearMangadexLoginInputs();
                        SelectPasswordManagerTab();
                        lblStatus.Text = _isVietnameseUi
                            ? "Đã đăng nhập MangaDex. Hãy chọn ngôn ngữ rồi bấm HOÀN TẤT."
                            : "MangaDex signed in. Choose language, then click DONE.";
                        return;
                    }
                }

                if (await loginWindow.ShowNonBlockingAsync())
                {
                    SyncMangadexLoginState(loginWindow);
                    SetMangadexPreferredTranslatedLanguages(loginWindow.SelectedTranslatedLanguages);
                    ClearMangadexLoginInputs();
                    SelectPasswordManagerTab();
                    lblStatus.Text = _isVietnameseUi
                        ? "Đã đồng bộ phiên login MangaDex."
                        : "MangaDex login session synced.";
                }
                else
                {
                    lblStatus.Text = _isVietnameseUi ? "Đã hủy login MangaDex." : "MangaDex login cancelled.";
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = (_isVietnameseUi ? "Login MangaDex lỗi: " : "MangaDex login failed: ") + ex.Message;
                MangadexLog("Lỗi login: " + ex.Message);
            }
            finally
            {
                _isMangadexLoginWindowActive = false;
                if (_mangadexLoginWindow != null && !_mangadexLoginWindow.IsVisible)
                {
                    _mangadexLoginWindow = null;
                }
            }
        }

        private void ClearDamconuongLoginInputs()
        {
            txtDamconuongLoginEmail?.Clear();
            txtDamconuongLoginPassword?.Clear();
        }

        private void ClearMangadexLoginInputs()
        {
            txtMangadexLoginEmail?.Clear();
            txtMangadexLoginPassword?.Clear();
        }

        private void SelectPasswordManagerTab()
        {
            if (tabLeftPanel == null || tabPasswordRootItem == null)
            {
                return;
            }

            tabLeftPanel.SelectedItem = tabPasswordRootItem;
        }

        private sealed class PasswordManagerEntry
        {
            internal string Username { get; set; }
            internal string Password { get; set; }
        }

        private async Task<DamconuongLoginWindow> EnsureDamconuongLoginWindowAsync(string targetUrl)
        {
            string loginUrl = IsDamconuongUrl(targetUrl) ? NormalizeDamconuongUrl(targetUrl) : GetDamconuongResolvedBaseUrl();
            if (_damconuongLoginWindow == null || !_damconuongLoginWindow.IsLoaded || !_damconuongLoginWindow.IsVisible)
            {
                _damconuongLoginWindow = new DamconuongLoginWindow(loginUrl, _isVietnameseUi, "damconuong.shop", "damconuong", GetDamconuongResolvedBaseUrl(), GetDamconuongAllowedHosts(), keepOpenAfterAuth: false)
                {
                    Owner = this
                };
                _damconuongLoginWindow.Closed += (_, __) => _damconuongLoginWindow = null;
                _damconuongLoginWindow.Show();
            }
            else
            {
                _damconuongLoginWindow.Activate();
                await _damconuongLoginWindow.NavigateIfNeededAsync(loginUrl);
            }

            await _damconuongLoginWindow.WaitUntilReadyAsync();
            return _damconuongLoginWindow;
        }

        private async Task<DamconuongLoginWindow> EnsureMangadexLoginWindowAsync(string targetUrl)
        {
            string loginUrl = MangadexBaseUrl;
            if (_mangadexLoginWindow == null || !_mangadexLoginWindow.IsLoaded || !_mangadexLoginWindow.IsVisible)
            {
                _mangadexLoginWindow = new DamconuongLoginWindow(loginUrl, _isVietnameseUi, "mangadex.org", "mangadex", MangadexBaseUrl, new[] { "mangadex.org", "www.mangadex.org", "auth.mangadex.org" }, keepOpenAfterAuth: true)
                {
                    Owner = this
                };
                _mangadexLoginWindow.Closed += (_, __) => _mangadexLoginWindow = null;
                _mangadexLoginWindow.Show();
            }
            else
            {
                _mangadexLoginWindow.Activate();
                await _mangadexLoginWindow.NavigateIfNeededAsync(loginUrl);
            }

            await _mangadexLoginWindow.WaitUntilReadyAsync();
            return _mangadexLoginWindow;
        }

        private void SyncDamconuongLoginState(DamconuongLoginWindow loginWindow)
        {
            if (loginWindow == null)
            {
                return;
            }

            Uri baseUri = new Uri(GetDamconuongResolvedBaseUrl());
            Uri resolvedUri = loginWindow.ResolvedUri ?? baseUri;

            foreach (Cookie cookie in loginWindow.ResolvedCookies.GetCookies(resolvedUri).Cast<Cookie>())
            {
                _cookieContainer.Add(resolvedUri, cookie);
                _cookieContainer.Add(baseUri, new Cookie(cookie.Name, cookie.Value, string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path, baseUri.Host)
                {
                    Expires = cookie.Expires,
                    Secure = cookie.Secure,
                    HttpOnly = cookie.HttpOnly
                });
            }

            foreach (Cookie cookie in loginWindow.ResolvedCookies.GetCookies(baseUri).Cast<Cookie>())
            {
                _cookieContainer.Add(baseUri, cookie);
            }

            var scopedCookies = loginWindow.ResolvedCookies.GetCookies(resolvedUri).Cast<Cookie>()
                .Concat(loginWindow.ResolvedCookies.GetCookies(baseUri).Cast<Cookie>())
                .ToList();
            MergeCookiesIntoScopedContainer(resolvedUri.AbsoluteUri, resolvedUri, scopedCookies);
            MergeCookiesIntoScopedContainer(baseUri.AbsoluteUri, baseUri, scopedCookies);

            if (!string.IsNullOrWhiteSpace(loginWindow.UserAgent))
            {
                RememberScopedUserAgent(baseUri.AbsoluteUri, loginWindow.UserAgent);
                RememberScopedUserAgent(resolvedUri.AbsoluteUri, loginWindow.UserAgent);
            }
        }

        private void SyncMangadexLoginState(DamconuongLoginWindow loginWindow)
        {
            if (loginWindow == null)
            {
                return;
            }

            Uri baseUri = new Uri(MangadexBaseUrl);
            Uri resolvedUri = loginWindow.ResolvedUri ?? baseUri;

            foreach (Cookie cookie in loginWindow.ResolvedCookies.GetCookies(resolvedUri).Cast<Cookie>())
            {
                _cookieContainer.Add(resolvedUri, cookie);
                _cookieContainer.Add(baseUri, new Cookie(cookie.Name, cookie.Value, string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path, baseUri.Host)
                {
                    Expires = cookie.Expires,
                    Secure = cookie.Secure,
                    HttpOnly = cookie.HttpOnly
                });
            }

            foreach (Cookie cookie in loginWindow.ResolvedCookies.GetCookies(baseUri).Cast<Cookie>())
            {
                _cookieContainer.Add(baseUri, cookie);
            }

            if (!string.IsNullOrWhiteSpace(loginWindow.UserAgent))
            {
                RememberScopedUserAgent(baseUri.AbsoluteUri, loginWindow.UserAgent);
                RememberScopedUserAgent(resolvedUri.AbsoluteUri, loginWindow.UserAgent);
            }
        }

        private void SetMangadexPreferredTranslatedLanguages(IEnumerable<string> languages)
        {
            _mangadexPreferredTranslatedLanguages.Clear();
            foreach (string language in languages ?? Enumerable.Empty<string>())
            {
                string clean = NormalizeMangadexLanguageCode(language);
                if (!string.IsNullOrWhiteSpace(clean) &&
                    !_mangadexPreferredTranslatedLanguages.Any(value => string.Equals(value, clean, StringComparison.OrdinalIgnoreCase)))
                {
                    _mangadexPreferredTranslatedLanguages.Add(clean);
                }
            }
        }

        private static string NormalizeMangadexLanguageCode(string value)
        {
            string clean = (value ?? string.Empty).Trim().ToLowerInvariant();
            switch (clean)
            {
                case "vietnamese":
                    return "vi";
                case "english":
                    return "en";
                case "japanese":
                    return "ja";
                case "chinese (simplified)":
                    return "zh";
                case "chinese (traditional)":
                    return "zh-hk";
                case "korean":
                    return "ko";
                case "thai":
                    return "th";
                case "indonesian":
                    return "id";
                case "portuguese (brazil)":
                    return "pt-br";
                case "spanish (latam)":
                    return "es-la";
                default:
                    return clean;
            }
        }

    }

    internal sealed class DamconuongLoginWindow : Window
    {
        private readonly WebView2 _webView;
        private readonly TextBlock _statusText;
        private string _targetUrl;
        private readonly bool _isVietnamese;
        private readonly string _siteDisplayName;
        private readonly string _siteBaseUrl;
        private readonly HashSet<string> _allowedHosts;
        private readonly bool _keepOpenAfterAuth;
        private readonly TaskCompletionSource<bool> _readyTcs = new TaskCompletionSource<bool>();
        private bool _wasCompleted;
        private int _transientNavigationRetryCount;
        private bool _loginSubmitStarted;
        private const int MaxTransientNavigationRetries = 4;
        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;
        private const uint KeyeventfKeyup = 0x0002;
        private const uint KeyeventfUnicode = 0x0004;
        private const ushort VkTab = 0x09;
        private const ushort VkSpace = 0x20;
        private const ushort VkReturn = 0x0D;

        internal CookieContainer ResolvedCookies { get; private set; } = new CookieContainer();
        internal Uri ResolvedUri { get; private set; }
        internal string UserAgent { get; private set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        internal List<string> SelectedTranslatedLanguages { get; private set; } = new List<string>();
        internal bool WasCompleted => _wasCompleted;

        internal DamconuongLoginWindow(string targetUrl, bool isVietnamese)
            : this(targetUrl, isVietnamese, "damconuong.shop", "damconuong.shop", "https://damconuong.shop", new[] { "damconuong.shop", "mbpro.vip" }, false)
        {
        }

        internal DamconuongLoginWindow(string targetUrl, bool isVietnamese, string siteDisplayName, string siteKey, string siteBaseUrl, IEnumerable<string> allowedHosts, bool keepOpenAfterAuth)
        {
            _targetUrl = string.IsNullOrWhiteSpace(targetUrl) ? siteBaseUrl : targetUrl;
            _isVietnamese = isVietnamese;
            _siteDisplayName = string.IsNullOrWhiteSpace(siteDisplayName) ? siteKey : siteDisplayName;
            _siteBaseUrl = string.IsNullOrWhiteSpace(siteBaseUrl) ? "https://damconuong.shop" : siteBaseUrl;
            _allowedHosts = new HashSet<string>((allowedHosts ?? Enumerable.Empty<string>()).Where(host => !string.IsNullOrWhiteSpace(host)).Select(host => host.Trim()), StringComparer.OrdinalIgnoreCase);
            _keepOpenAfterAuth = keepOpenAfterAuth;

            Title = isVietnamese ? $"LOGIN {_siteDisplayName.ToUpperInvariant()}" : $"{_siteDisplayName.ToUpperInvariant()} LOGIN";
            Width = 1260;
            Height = 860;
            MinWidth = 980;
            MinHeight = 720;
            Background = new SolidColorBrush(Color.FromRgb(0x09, 0x0D, 0x14));
            Foreground = Brushes.White;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel
            {
                Margin = new Thickness(16, 14, 16, 10)
            };
            header.Children.Add(new TextBlock
            {
                Text = isVietnamese ? $"ĐĂNG NHẬP {_siteDisplayName.ToUpperInvariant()}" : $"{_siteDisplayName.ToUpperInvariant()} LOGIN",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0xE7, 0xFF))
            });
            header.Children.Add(new TextBlock
            {
                Text = isVietnamese
                    ? $"Đăng nhập {_siteDisplayName} trong WebView này. Xong thì bấm HOÀN TẤT để đồng bộ cookie cho downloader."
                    : $"Sign in to {_siteDisplayName} in this WebView. Click DONE to sync cookies back to the downloader.",
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xB3, 0xC7))
            });
            root.Children.Add(header);

            _webView = new WebView2
            {
                Margin = new Thickness(16, 0, 16, 12)
            };
            Grid.SetRow(_webView, 1);
            root.Children.Add(_webView);

            var footer = new Grid
            {
                Margin = new Thickness(16, 0, 16, 16)
            };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _statusText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x6A))
            };
            footer.Children.Add(_statusText);

            var doneButton = new Button
            {
                Content = isVietnamese ? "HOÀN TẤT" : "DONE",
                MinWidth = 120,
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(14, 8, 14, 8)
            };
            doneButton.Click += async (sender, args) => await CompleteAsync();
            Grid.SetColumn(doneButton, 1);
            footer.Children.Add(doneButton);

            var cancelButton = new Button
            {
                Content = isVietnamese ? "ĐÓNG" : "CLOSE",
                MinWidth = 96,
                Margin = new Thickness(10, 0, 0, 0),
                Padding = new Thickness(14, 8, 14, 8)
            };
            cancelButton.Click += (sender, args) => Close();
            Grid.SetColumn(cancelButton, 2);
            footer.Children.Add(cancelButton);

            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Content = root;

            Loaded += DamconuongLoginWindow_Loaded;
        }

        internal Task<bool> ShowNonBlockingAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            Closed += OnClosed;
            return tcs.Task;

            void OnClosed(object sender, EventArgs e)
            {
                Closed -= OnClosed;
                tcs.TrySetResult(_wasCompleted);
            }
        }

        private async void DamconuongLoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                string safeUserDataFolderName = Regex.Replace((_siteDisplayName ?? "source-login").ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
                string userDataFolder = System.IO.Path.Combine(PortablePaths.WebView2UserDataFolder, safeUserDataFolderName + "-login");
                System.IO.Directory.CreateDirectory(userDataFolder);
                string browserArgs = "--disable-extensions --disable-component-extensions-with-background-pages --disable-background-networking --disable-sync --disable-default-apps --no-first-run --disable-features=msSmartScreenProtection,RendererCodeIntegrity --blink-settings=imagesEnabled=false";
                var env = await CoreWebView2Environment.CreateAsync(
                    null,
                    userDataFolder,
                    new CoreWebView2EnvironmentOptions(browserArgs));

                await _webView.EnsureCoreWebView2Async(env);
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    _webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                    _webView.CoreWebView2.Settings.UserAgent = UserAgent;
                    _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                    _webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                    _webView.CoreWebView2.NavigationCompleted += WebView_NavigationCompleted;
                    await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
const textOnlyStyle = document.createElement('style');
textOnlyStyle.textContent = 'img, picture, video, audio, canvas, [style*=""background-image""] { display: none !important; visibility: hidden !important; }';
(document.head || document.documentElement).appendChild(textOnlyStyle);
window.open = () => null;
document.addEventListener('click', function (event) {
  const anchor = event.target && event.target.closest ? event.target.closest('a[target=""_blank""]') : null;
  if (anchor) {
    anchor.removeAttribute('target');
  }
}, true);");
                }

                _readyTcs.TrySetResult(true);
                _statusText.Text = _isVietnamese
                    ? $"Đang mở trang {_siteDisplayName}..."
                    : $"Opening {_siteDisplayName}...";
                _webView.Source = new Uri(_targetUrl);
            }
            catch (Exception ex)
            {
                _readyTcs.TrySetException(ex);
                MessageBox.Show(
                    (_isVietnamese ? "Không thể khởi tạo WebView2: " : "Failed to initialize WebView2: ") + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
            }
        }

        private void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_webView?.CoreWebView2 == null)
            {
                return;
            }

            if (!e.IsSuccess && IsTransientWebViewNavigationError(e.WebErrorStatus) &&
                _siteDisplayName.IndexOf("mangadex", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _statusText.Text = _isVietnamese
                    ? "MangaDex lỗi mạng. Nếu thấy trang can't reach this page, app sẽ tự quay về trang chủ và đăng nhập lại."
                    : "MangaDex network error. If the can't reach this page screen is visible, the app will restart login from home.";
                return;
            }

            _transientNavigationRetryCount = 0;
            _statusText.Text = _isVietnamese
                ? (_keepOpenAfterAuth ? "Đăng nhập xong, chọn ngôn ngữ rồi bấm HOÀN TẤT." : "Đăng nhập xong thì bấm HOÀN TẤT để lưu cookie.")
                : (_keepOpenAfterAuth ? "Sign in, choose language, then click DONE." : "Click DONE after login to save cookies.");
        }

        private bool IsTransientWebViewNavigationError(CoreWebView2WebErrorStatus status)
        {
            string value = status.ToString();
            return value.IndexOf("Connection", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("Reset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("Disconnected", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task<bool> RetryTransientNavigationAsync(string reason)
        {
            if (_siteDisplayName.IndexOf("mangadex", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (_transientNavigationRetryCount >= MaxTransientNavigationRetries)
            {
                _statusText.Text = _isVietnamese
                    ? $"MangaDex bị lỗi mạng ({reason}). Hãy bấm refresh hoặc mở login lại."
                    : $"MangaDex network error ({reason}). Refresh or reopen login.";
                return false;
            }

            _transientNavigationRetryCount++;
            _statusText.Text = _isVietnamese
                ? $"MangaDex bị reset kết nối. Tự refresh lần {_transientNavigationRetryCount}/{MaxTransientNavigationRetries}..."
                : $"MangaDex connection reset. Auto refresh {_transientNavigationRetryCount}/{MaxTransientNavigationRetries}...";
            await Task.Delay(1200 + (_transientNavigationRetryCount * 500));

            _webView.CoreWebView2?.Navigate(_siteBaseUrl);
            return true;
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
        }

        private void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e?.Uri))
            {
                return;
            }

            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri uri))
            {
                string host = uri.Host ?? string.Empty;
                if (_allowedHosts.Any(allowed => host.IndexOf(allowed, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return;
                }

                e.Cancel = true;
            }
        }

        internal Task WaitUntilReadyAsync()
        {
            return _readyTcs.Task;
        }

        internal async Task NavigateIfNeededAsync(string targetUrl)
        {
            await WaitUntilReadyAsync();
            string normalized = string.IsNullOrWhiteSpace(targetUrl) ? _siteBaseUrl : targetUrl;
            _targetUrl = normalized;
            string current = _webView.Source?.ToString() ?? string.Empty;
            if (!string.Equals(current, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _webView.CoreWebView2?.Navigate(normalized);
                await Task.Delay(1200);
            }
        }

        internal async Task<bool> ApplyCredentialsAsync(string email, string password, bool submitAfterFill = true, bool enterAfterRemember = false)
        {
            await WaitUntilReadyAsync();
            string script =
@"
(async () => {
  const emailValue = __EMAIL__;
  const passwordValue = __PASSWORD__;
  const selectorsEmail = [
    'input[type=""email""]',
    'input[type=""text""]',
    'input:not([type])',
    'input[name*=""email"" i]',
    'input[name*=""user"" i]',
    'input[name*=""login"" i]',
    'input[id*=""email"" i]',
    'input[id*=""user"" i]',
    'input[id*=""login"" i]',
    'input[autocomplete=""username""]',
    'input[autocomplete=""email""]',
    'input[inputmode=""email""]'
  ];
  const selectorsPassword = [
    'input[type=""password""]',
    'input[name*=""pass"" i]',
    'input[id*=""pass"" i]',
    'input[autocomplete=""current-password""]'
  ];
  const submitSelectors = [
    'button[type=""submit""]',
    'input[type=""submit""]',
    'button[name*=""login"" i]',
    'button[id*=""login"" i]',
    'button[aria-label*=""login"" i]',
    'button[aria-label*=""sign in"" i]',
    'button[title*=""login"" i]',
    'button[title*=""sign in"" i]',
    '.btn-login',
    '.login-button'
  ];
  const rememberSelectors = [
    'input[type=""checkbox""][name*=""remember"" i]',
    'input[type=""checkbox""][id*=""remember"" i]',
    'input[type=""checkbox""][autocomplete=""remember""]'
  ];
  const find = list => {
    for (const selector of list) {
      const matches = Array.from(document.querySelectorAll(selector));
      const el = matches.find(node => {
        if (!node) return false;
        const style = window.getComputedStyle(node);
        return !node.disabled && style.display !== 'none' && style.visibility !== 'hidden';
      });
      if (el) return el;
    }
    return null;
  };
  const setNativeValue = (el, value) => {
    const prototype = Object.getPrototypeOf(el);
    const descriptor = Object.getOwnPropertyDescriptor(prototype, 'value');
    if (descriptor && descriptor.set) {
      descriptor.set.call(el, value);
    } else {
      el.value = value;
    }
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  };
  const emailInput = find(selectorsEmail);
  const passwordInput = find(selectorsPassword);
  const fallbackEmailInput = Array.from(document.querySelectorAll('input'))
    .find(node => {
      if (!node) return false;
      const type = (node.getAttribute('type') || 'text').toLowerCase();
      const style = window.getComputedStyle(node);
      if (node.disabled || style.display === 'none' || style.visibility === 'hidden') return false;
      return type !== 'password' && type !== 'checkbox' && type !== 'hidden' && type !== 'submit' && type !== 'button';
    });
  const actualEmailInput = emailInput || fallbackEmailInput;
  if (!actualEmailInput || !passwordInput) return 'missing';
  const rememberInput = find(rememberSelectors) || Array.from(document.querySelectorAll('input[type=""checkbox""]'))
    .find(node => {
      const id = node.getAttribute('id');
      const explicitLabel = id ? document.querySelector(`label[for=""${CSS.escape(id)}""]`) : null;
      const label = explicitLabel || node.closest('label') || node.parentElement;
      const text = [
        node.getAttribute('name'),
        node.getAttribute('id'),
        node.getAttribute('value'),
        node.getAttribute('aria-label'),
        label && (label.innerText || label.textContent)
      ].filter(Boolean).join(' ').trim().toLowerCase();
      return text.includes('remember') || text.includes('ghi nhớ') || text.includes('ghi nho');
    }) || (() => {
      const boxes = Array.from(document.querySelectorAll('input[type=""checkbox""]'))
        .filter(node => {
          const style = window.getComputedStyle(node);
          return !node.disabled && style.display !== 'none' && style.visibility !== 'hidden';
        });
      return boxes.length === 1 ? boxes[0] : null;
    })();
  const submitButton = find(submitSelectors);
  const textSubmit = candidates => candidates.find(node => {
    const text = (node.textContent || node.value || '').trim().toLowerCase();
    return text === 'sign in' || text === 'login' || text.includes('sign in') || text.includes('login') || text.includes('đăng nhập') || text.includes('dang nhap');
  });
  const findFallbackSubmit = () => textSubmit(Array.from(document.querySelectorAll('button,[role=""button""],a,input[type=""button""],input[type=""submit""],input:not([type])')));
  const submitForm = () => {
    const button = submitButton || findFallbackSubmit();
    if (button) {
      button.click();
      return true;
    }
    const form = actualEmailInput.form || passwordInput.form;
    if (form) {
      if (typeof form.requestSubmit === 'function') {
        form.requestSubmit();
      } else {
        form.submit();
      }
      return true;
    }
    return false;
  };
  actualEmailInput.focus();
  setNativeValue(actualEmailInput, emailValue);
  passwordInput.focus();
  setNativeValue(passwordInput, passwordValue);
  await new Promise(resolve => setTimeout(resolve, 300));
  if ((actualEmailInput.value || '') !== emailValue || (passwordInput.value || '') !== passwordValue) {
    return 'notset';
  }
  if (!__SUBMIT_AFTER_FILL__) {
    return 'filled';
  }
  if (__ENTER_AFTER_REMEMBER__) {
    if (rememberInput && !rememberInput.checked) {
      rememberInput.click();
      rememberInput.dispatchEvent(new Event('input', { bubbles: true }));
      rememberInput.dispatchEvent(new Event('change', { bubbles: true }));
    }
    if (!rememberInput || !rememberInput.checked) return 'remember-missing';
    await new Promise(resolve => setTimeout(resolve, 300));
    return submitForm() ? 'submitted' : 'submit-missing';
  }
  if (rememberInput && !rememberInput.checked) {
    rememberInput.click();
    rememberInput.dispatchEvent(new Event('change', { bubbles: true }));
  }
  if (submitButton) {
    submitButton.click();
    return 'submitted';
  }
  const fallbackSubmit = findFallbackSubmit();
  if (fallbackSubmit) {
    fallbackSubmit.click();
    return 'submitted';
  }
  passwordInput.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', bubbles: true }));
  passwordInput.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter', code: 'Enter', bubbles: true }));
  const form = actualEmailInput.form || passwordInput.form;
  if (form) {
    if (typeof form.requestSubmit === 'function') {
      form.requestSubmit();
    } else {
      form.submit();
    }
    return 'submitted';
  }
  return 'filled';
})()";

            for (int attempt = 0; attempt < 5; attempt++)
            {
                await NavigateToLoginFormAsync();
                string result = await ExecuteStringScriptAsync(
                    script
                        .Replace("__EMAIL__", ToJavaScriptStringLiteral(email))
                        .Replace("__PASSWORD__", ToJavaScriptStringLiteral(password))
                        .Replace("__SUBMIT_AFTER_FILL__", submitAfterFill ? "true" : "false")
                        .Replace("__ENTER_AFTER_REMEMBER__", enterAfterRemember ? "true" : "false"));
                if (string.IsNullOrWhiteSpace(result) ||
                    string.Equals(result, "missing", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(result, "notset", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(1200);
                    continue;
                }
                if (enterAfterRemember && string.Equals(result, "submitted", StringComparison.OrdinalIgnoreCase))
                {
                    _loginSubmitStarted = true;
                    _statusText.Text = _isVietnamese
                        ? "Đã auto fill form login. Đã tick Remember me và bấm ĐĂNG NHẬP."
                        : "Login form auto-filled. Remember me checked and LOGIN clicked.";
                }
                else if (!enterAfterRemember && string.Equals(result, "submitted", StringComparison.OrdinalIgnoreCase))
                {
                    _loginSubmitStarted = true;
                }
                else if (enterAfterRemember && string.Equals(result, "remember-missing", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(1200);
                    continue;
                }
                else if (enterAfterRemember && string.Equals(result, "submit-missing", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(1200);
                    continue;
                }
                _statusText.Text = _isVietnamese
                    ? (submitAfterFill ? "Đã auto fill form login. Đang chờ xác thực..." : "Đã điền form login. Hãy tự bấm ĐĂNG NHẬP, rồi bấm HOÀN TẤT.")
                    : (submitAfterFill ? "Login form auto-filled. Waiting for authentication..." : "Login form filled. Click LOGIN yourself, then click DONE.");
                return true;
            }

            return false;
        }

        internal async Task<bool> WaitForAuthenticatedSessionAsync(bool navigateToTarget = true, bool ignoreExistingCookieUntilFormGone = false)
        {
            await WaitUntilReadyAsync();

            for (int attempt = 0; attempt < 20; attempt++)
            {
                bool hasAuthCookies = await HasAuthenticationCookiesAsync();
                string currentUrl = (_webView?.Source?.ToString() ?? string.Empty).ToLowerInvariant();
                string state = await ExecuteStringScriptAsync(@"
(() => {
  const text = (document.body?.innerText || '').toLowerCase();
  const hasPassword = !!document.querySelector('input[type=""password""]');
  const loginRequired =
    text.includes('yêu cầu đăng nhập') ||
    text.includes('nội dung này dành cho người dùng đã xác thực') ||
    text.includes('noi dung nay danh cho nguoi dung da xac thuc') ||
    text.includes('tạo tài khoản mới') ||
    text.includes('tao tai khoan moi');

  if (hasPassword) return 'password';
  if (loginRequired) return 'blocked';
  return 'ready';
})()");

                if (ignoreExistingCookieUntilFormGone && string.Equals(state, "password", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(1000);
                    continue;
                }

                if (_siteDisplayName.IndexOf("mangadex", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    currentUrl.Contains("login-actions/authenticate"))
                {
                    await Task.Delay(1000);
                    continue;
                }

                if (hasAuthCookies && !string.Equals(state, "blocked", StringComparison.OrdinalIgnoreCase))
                {
                    if (navigateToTarget)
                    {
                        await NavigateIfNeededAsync(_targetUrl);
                    }
                    await Task.Delay(1000);
                    string targetState = await ExecuteStringScriptAsync(@"
(() => {
  const text = (document.body?.innerText || '').toLowerCase();
  const loginRequired =
    text.includes('yêu cầu đăng nhập') ||
    text.includes('nội dung này dành cho người dùng đã xác thực') ||
    text.includes('noi dung nay danh cho nguoi dung da xac thuc') ||
    text.includes('tạo tài khoản mới') ||
    text.includes('tao tai khoan moi');
  return loginRequired ? 'blocked' : 'ready';
})()");
                    if (string.Equals(targetState, "ready", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                bool isMangadexWaitingAfterSubmit = _siteDisplayName.IndexOf("mangadex", StringComparison.OrdinalIgnoreCase) >= 0 && _loginSubmitStarted;
                if (!isMangadexWaitingAfterSubmit && (attempt == 3 || attempt == 7 || attempt == 11 || attempt == 15))
                {
                    _webView.CoreWebView2?.Navigate(_targetUrl);
                }

                await Task.Delay(1000);
            }

            return false;
        }

        private async Task<bool> HasAuthenticationCookiesAsync()
        {
            if (_webView?.CoreWebView2 == null)
            {
                return false;
            }

            foreach (string url in new[] { _siteBaseUrl, _targetUrl }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync(url);
                if (cookies.Any(cookie =>
                    cookie != null &&
                    !string.IsNullOrWhiteSpace(cookie.Value) &&
                    (cookie.Name.IndexOf("session", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     cookie.Name.IndexOf("remember", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     cookie.Name.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     cookie.Name.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    return true;
                }
            }

            return false;
        }

        internal async Task CaptureSessionAsync()
        {
            await WaitUntilReadyAsync();
            await RefreshResolvedSessionAsync();
        }

        internal async Task CompleteAndCloseAsync()
        {
            await CompleteAsync();
        }

        private async Task NavigateToLoginFormAsync()
        {
            await WaitUntilReadyAsync();
            for (int i = 0; i < 3; i++)
            {
                if (_siteDisplayName.IndexOf("mangadex", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string loadState = await GetMangadexLoadStateAsync();
                    if (string.Equals(loadState, "error", StringComparison.OrdinalIgnoreCase))
                    {
                        if (await RetryMangadexErrorPageIfNeededAsync())
                        {
                            await Task.Delay(1800);
                            continue;
                        }
                    }

                    if (string.Equals(loadState, "loading", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(1500);
                        continue;
                    }

                    if (await RetryMangadexErrorPageIfNeededAsync())
                    {
                        await Task.Delay(1800);
                        continue;
                    }

                    string mdState = await ExecuteStringScriptAsync(@"
(() => {
  const hasPassword = !!document.querySelector('input[type=""password""]');
  if (hasPassword) return 'ready';

  const buttons = Array.from(document.querySelectorAll('button,a'));
  const signInNode = buttons.find(node => {
    const text = (node.textContent || '').trim().toLowerCase();
    const href = (node.getAttribute('href') || '').toLowerCase();
    return text.includes('sign in') || text.includes('login') || text.includes('đăng nhập') || text.includes('dang nhap') || href.includes('/login') || href.includes('/signin');
  });
  if (signInNode) {
    signInNode.click();
    return 'clicked-signin';
  }

  const avatarButton = buttons.find(node => {
    if (!node) return false;
    const rect = node.getBoundingClientRect();
    const style = window.getComputedStyle(node);
    if (style.display === 'none' || style.visibility === 'hidden' || rect.width === 0 || rect.height === 0) return false;
    const text = (node.textContent || '').trim().toLowerCase();
    const aria = (node.getAttribute('aria-label') || '').toLowerCase();
    const title = (node.getAttribute('title') || '').toLowerCase();
    const nearTopRight = rect.top < 120 && rect.right > (window.innerWidth - 120);
    return nearTopRight || text.includes('account') || text.includes('profile') || text.includes('user') || aria.includes('account') || aria.includes('profile') || aria.includes('user') || title.includes('account') || title.includes('profile') || title.includes('user');
  });
  if (avatarButton) {
    avatarButton.click();
    return 'clicked-avatar';
  }

  return 'missing';
})()");

                    if (string.Equals(mdState, "ready", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    if (string.Equals(mdState, "clicked-avatar", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(1500);
                        continue;
                    }

                    if (string.Equals(mdState, "clicked-signin", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(2000);
                        continue;
                    }
                }

                string state = await ExecuteStringScriptAsync(@"
(() => {
  if (document.querySelector('input[type=""password""]')) return 'ready';
  const candidates = Array.from(document.querySelectorAll('a,button'));
  const loginNode = candidates.find(node => {
    const text = (node.textContent || '').trim().toLowerCase();
    const href = (node.getAttribute('href') || '').toLowerCase();
    return text.includes('đăng nhập') || text.includes('dang nhap') || text.includes('login') || text.includes('sign in') || text.includes('signin') || href.includes('dang-nhap') || href.includes('/login') || href.includes('/signin');
  });
  if (!loginNode) return 'missing';
  loginNode.click();
  return 'clicked';
})()");

                if (string.Equals(state, "ready", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (string.Equals(state, "clicked", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(1500);
                    continue;
                }

                _webView.CoreWebView2?.Navigate(_siteBaseUrl);
                await Task.Delay(1500);
            }
        }

        private async Task<string> GetMangadexLoadStateAsync()
        {
            if (_siteDisplayName.IndexOf("mangadex", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return "ready";
            }

            return await ExecuteStringScriptAsync(@"
(() => {
  const text = ((document.body && document.body.innerText) || '').toLowerCase();
  const title = (document.title || '').toLowerCase();
  if (text.includes(""can't reach this page"") ||
      text.includes('can’t reach this page') ||
      text.includes('connection reset') ||
      text.includes('connection is reset') ||
      text.includes('err_connection_reset') ||
      text.includes('err_failed') ||
      title.includes('connection reset')) {
    return 'error';
  }

  if (document.querySelector('input[type=""password""]')) return 'ready';
  if (document.images && Array.from(document.images).some(img => img.complete && img.naturalWidth > 0)) return 'ready';
  if (document.querySelector('button,a,[role=""button""],main,nav')) return 'ready';
  return 'loading';
})()");
        }

        private async Task<bool> RetryMangadexErrorPageIfNeededAsync()
        {
            if (_siteDisplayName.IndexOf("mangadex", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (_loginSubmitStarted)
            {
                return false;
            }

            string state = await ExecuteStringScriptAsync(@"
(() => {
  const text = ((document.body && document.body.innerText) || '').toLowerCase();
  const title = (document.title || '').toLowerCase();
  return text.includes('connection reset') ||
         text.includes('connection is reset') ||
         text.includes('err_connection_reset') ||
         text.includes('err_failed') ||
         text.includes(""can't reach this page"") ||
         text.includes('this site can’t be reached') ||
         text.includes(""this site can't be reached"") ||
         title.includes('connection reset') ||
         title.includes('err_connection_reset') ? 'reset' : 'ok';
})()");
            if (!string.Equals(state, "reset", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return await RetryTransientNavigationAsync("connection reset page");
        }

        private async Task<List<string>> CaptureSelectedLanguagesAsync()
        {
            if (_siteDisplayName.IndexOf("mangadex", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return new List<string>();
            }

            string raw = await ExecuteStringScriptAsync(@"
(() => {
  const values = Array.from(document.querySelectorAll('input[type=""checkbox""]'))
    .filter(input => input && input.checked)
    .map(input => {
      const label = input.closest('label,li,div') || input.parentElement;
      const text = (label && label.innerText ? label.innerText : '').trim();
      return (input.value || text || '').trim();
    })
    .filter(Boolean);
  return values.join('|');
})()");

            return (raw ?? string.Empty)
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeLanguageCode)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeLanguageCode(string value)
        {
            string clean = (value ?? string.Empty).Trim().ToLowerInvariant();
            switch (clean)
            {
                case "vietnamese":
                    return "vi";
                case "english":
                    return "en";
                case "japanese":
                    return "ja";
                case "chinese (simplified)":
                    return "zh";
                case "chinese (traditional)":
                    return "zh-hk";
                case "korean":
                    return "ko";
                case "thai":
                    return "th";
                case "indonesian":
                    return "id";
                case "portuguese (brazil)":
                    return "pt-br";
                case "spanish (latam)":
                    return "es-la";
                default:
                    return clean;
            }
        }

        private async Task RefreshResolvedSessionAsync()
        {
            if (_webView?.CoreWebView2 == null)
            {
                return;
            }

            string currentUrl = _webView.Source?.ToString() ?? _targetUrl;
            ResolvedUri = Uri.TryCreate(currentUrl, UriKind.Absolute, out Uri resolvedUri) ? resolvedUri : new Uri(_targetUrl);

            ResolvedCookies = new CookieContainer();
            foreach (string url in new[] { currentUrl, _siteBaseUrl, _targetUrl }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (CoreWebView2Cookie webCookie in await _webView.CoreWebView2.CookieManager.GetCookiesAsync(url))
                {
                    Uri cookieUri = Uri.TryCreate("https://" + webCookie.Domain.TrimStart('.'), UriKind.Absolute, out Uri parsedCookieUri)
                        ? parsedCookieUri
                        : ResolvedUri;

                    var cookie = new Cookie(webCookie.Name, webCookie.Value, string.IsNullOrWhiteSpace(webCookie.Path) ? "/" : webCookie.Path, webCookie.Domain)
                    {
                        Secure = webCookie.IsSecure,
                        HttpOnly = webCookie.IsHttpOnly
                    };

                    if (webCookie.Expires != DateTime.MinValue)
                    {
                        cookie.Expires = webCookie.Expires;
                    }

                    ResolvedCookies.Add(cookieUri, cookie);
                }
            }

            try
            {
                string userAgent = await ExecuteStringScriptAsync("navigator.userAgent");
                if (!string.IsNullOrWhiteSpace(userAgent))
                {
                    UserAgent = userAgent;
                }
            }
            catch
            {
            }
        }

        private async Task<string> ExecuteStringScriptAsync(string script)
        {
            string raw = await _webView.CoreWebView2.ExecuteScriptAsync(script);
            return DecodeWebViewString(raw);
        }

        private async Task RunNativeLoginSequenceAsync(string email, string password, LoginUiTargets targets)
        {
            Activate();
            _webView.Focus();
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            SetForegroundWindow(hwnd);
            await Task.Delay(180);

            ClickWebViewPoint(targets.EmailX, targets.EmailY);
            await Task.Delay(120);
            SendUnicodeText(email);
            await Task.Delay(140);

            ClickWebViewPoint(targets.PasswordX, targets.PasswordY);
            await Task.Delay(120);
            SendUnicodeText(password);
            await Task.Delay(140);

            if (targets.RememberExists && !targets.RememberChecked)
            {
                ClickWebViewPoint(targets.RememberX, targets.RememberY);
                await Task.Delay(120);
            }

            if (targets.SubmitExists)
            {
                ClickWebViewPoint(targets.SubmitX, targets.SubmitY);
            }
            else
            {
                SendVirtualKey(VkReturn);
            }
            await Task.Delay(220);
        }

        private void ClickWebViewPoint(double webX, double webY)
        {
            Point screenPoint = _webView.PointToScreen(new Point(webX, webY));
            SetCursorPos((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y));
            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        }

        private static void SendUnicodeText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            foreach (char ch in text)
            {
                SendKeyboardInput(0, ch, KeyeventfUnicode);
                SendKeyboardInput(0, ch, KeyeventfUnicode | KeyeventfKeyup);
            }
        }

        private static void SendVirtualKey(ushort virtualKey)
        {
            SendKeyboardInput(virtualKey, '\0', 0);
            SendKeyboardInput(virtualKey, '\0', KeyeventfKeyup);
        }

        private static void SendKeyboardInput(ushort virtualKey, char unicodeChar, uint flags)
        {
            var input = new INPUT
            {
                type = 1,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = virtualKey,
                        wScan = unicodeChar,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            INPUT[] inputs = { input };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static string DecodeWebViewString(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.StartsWith("\"", StringComparison.Ordinal) && text.EndsWith("\"", StringComparison.Ordinal) && text.Length >= 2)
            {
                text = text.Substring(1, text.Length - 2)
                    .Replace("\\\\", "\\")
                    .Replace("\\\"", "\"")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\u003C", "<")
                    .Replace("\\u003E", ">")
                    .Replace("\\u0026", "&");
            }

            return string.Equals(text, "null", StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
        }

        private static string ToJavaScriptStringLiteral(string value)
        {
            string text = value ?? string.Empty;
            return "\"" + text
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("</", "<\\/") + "\"";
        }

        private async Task CompleteAsync()
        {
            if (_webView?.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                await RefreshResolvedSessionAsync();
                SelectedTranslatedLanguages = await CaptureSelectedLanguagesAsync();
                _wasCompleted = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    (_isVietnamese ? "Không thể lưu phiên login: " : "Failed to save login session: ") + ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                MessageBoxImage.Error);
            }
        }

        private sealed class LoginUiTargets
        {
            internal double EmailX { get; private set; }
            internal double EmailY { get; private set; }
            internal double PasswordX { get; private set; }
            internal double PasswordY { get; private set; }
            internal double RememberX { get; private set; }
            internal double RememberY { get; private set; }
            internal double SubmitX { get; private set; }
            internal double SubmitY { get; private set; }
            internal bool RememberExists { get; private set; }
            internal bool RememberChecked { get; private set; }
            internal bool SubmitExists { get; private set; }

            internal static LoginUiTargets TryParse(string raw)
            {
                Match[] matches = Regex.Matches(raw ?? string.Empty, @"-?\d+(?:\.\d+)?")
                    .Cast<Match>()
                    .ToArray();
                if (matches.Length < 4)
                {
                    return null;
                }

                bool rememberExists = raw.IndexOf(@"""rememberExists"":true", StringComparison.OrdinalIgnoreCase) >= 0;
                bool submitExists = raw.IndexOf(@"""submitExists"":true", StringComparison.OrdinalIgnoreCase) >= 0;
                int index = 0;
                return new LoginUiTargets
                {
                    EmailX = ParseDouble(matches[index++].Value),
                    EmailY = ParseDouble(matches[index++].Value),
                    PasswordX = ParseDouble(matches[index++].Value),
                    PasswordY = ParseDouble(matches[index++].Value),
                    RememberX = rememberExists && matches.Length >= index + 2 ? ParseDouble(matches[index++].Value) : 0d,
                    RememberY = rememberExists && matches.Length >= index + 1 ? ParseDouble(matches[index++].Value) : 0d,
                    SubmitX = submitExists && matches.Length >= index + 2 ? ParseDouble(matches[index++].Value) : 0d,
                    SubmitY = submitExists && matches.Length >= index + 1 ? ParseDouble(matches[index++].Value) : 0d,
                    RememberExists = rememberExists,
                    RememberChecked = raw.IndexOf(@"""rememberChecked"":true", StringComparison.OrdinalIgnoreCase) >= 0,
                    SubmitExists = submitExists
                };
            }

            private static double ParseDouble(string value)
            {
                return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
                    ? parsed
                    : 0d;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            internal uint type;
            internal InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            internal KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            internal ushort wVk;
            internal ushort wScan;
            internal uint dwFlags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
    }

}
