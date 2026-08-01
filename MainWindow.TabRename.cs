using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text;

namespace get_link_manga
{
    public partial class MainWindow : Window
    {
        // Models
        public class RenameFileItem : INotifyPropertyChanged
        {
            private string _newName;
            private bool _isValid = true;
            private string _statusText = "";

            public string OriginalPath { get; set; }
            public string OriginalName => Path.GetFileName(OriginalPath);
            public bool IsDirectory { get; set; }

            public string NewName
            {
                get => _newName;
                set
                {
                    if (_newName != value)
                    {
                        _newName = value;
                        OnPropertyChanged(nameof(NewName));
                    }
                }
            }

            public bool IsValid
            {
                get => _isValid;
                set
                {
                    if (_isValid != value)
                    {
                        _isValid = value;
                        OnPropertyChanged(nameof(IsValid));
                        OnPropertyChanged(nameof(StatusIcon));
                        OnPropertyChanged(nameof(StatusColor));
                    }
                }
            }

            public string StatusText
            {
                get => _statusText;
                set
                {
                    if (_statusText != value)
                    {
                        _statusText = value;
                        OnPropertyChanged(nameof(StatusText));
                    }
                }
            }

            public string StatusIcon => IsValid ? "✓" : "✗";
            public Brush StatusColor => IsValid ? new SolidColorBrush(Color.FromRgb(0x28, 0xFF, 0x7A)) : new SolidColorBrush(Color.FromRgb(0xFF, 0x2A, 0x85));

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public abstract class RenameMethodBase : INotifyPropertyChanged
        {
            private bool _isEnabled = true;
            private bool _backwards = false;
            private bool _useRegex = false;
            private string _applyTo = "Name"; // Name, Extension, Name and extension

            public string Name { get; protected set; }
            public string IconText { get; protected set; }

            public bool IsEnabled
            {
                get => _isEnabled;
                set
                {
                    if (_isEnabled != value)
                    {
                        _isEnabled = value;
                        OnPropertyChanged(nameof(IsEnabled));
                        TriggerUpdate();
                    }
                }
            }

            public bool Backwards
            {
                get => _backwards;
                set
                {
                    if (_backwards != value)
                    {
                        _backwards = value;
                        OnPropertyChanged(nameof(Backwards));
                        TriggerUpdate();
                    }
                }
            }

            public bool UseRegex
            {
                get => _useRegex;
                set
                {
                    if (_useRegex != value)
                    {
                        _useRegex = value;
                        OnPropertyChanged(nameof(UseRegex));
                        TriggerUpdate();
                    }
                }
            }

            public string ApplyTo
            {
                get => _applyTo;
                set
                {
                    if (_applyTo != value)
                    {
                        _applyTo = value;
                        OnPropertyChanged(nameof(ApplyTo));
                        TriggerUpdate();
                    }
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            public event Action RequestUpdate;

            protected void OnPropertyChanged(string name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }

            protected void TriggerUpdate()
            {
                RequestUpdate?.Invoke();
            }

            protected void AddCommonConfigControls(StackPanel panel)
            {
                var mainWin = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() ?? Application.Current.MainWindow as MainWindow;
                bool vi = mainWin != null && mainWin._isVietnameseUi;

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Margin = new Thickness(0, 10, 0, 5),
                    Padding = new Thickness(0, 5, 0, 0)
                };
                panel.Children.Add(border);

                var chkBackwards = new CheckBox
                {
                    Content = vi ? "Quét ngược (Backwards)" : "Backwards",
                    IsChecked = Backwards,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 3, 0, 5)
                };
                chkBackwards.Checked += (s, e) => Backwards = true;
                chkBackwards.Unchecked += (s, e) => Backwards = false;
                panel.Children.Add(chkBackwards);

                var chkRegex = new CheckBox
                {
                    Content = vi ? "Sử dụng Biểu thức chính quy (Regex)" : "Use regular expressions",
                    IsChecked = UseRegex,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 3, 0, 5)
                };
                chkRegex.Checked += (s, e) => UseRegex = true;
                chkRegex.Unchecked += (s, e) => UseRegex = false;
                panel.Children.Add(chkRegex);

                panel.Children.Add(new TextBlock 
                { 
                    Text = vi ? "Áp dụng cho:" : "Apply to:", 
                    Foreground = Brushes.White, 
                    Margin = new Thickness(0, 5, 0, 3) 
                });

                var cmbApplyTo = new ComboBox
                {
                    Height = 26,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("CyberpunkComboBox") as Style
                };
                
                cmbApplyTo.Items.Add(vi ? "Tên file (không đuôi)" : "Name");
                cmbApplyTo.Items.Add(vi ? "Đuôi mở rộng" : "Extension");
                cmbApplyTo.Items.Add(vi ? "Tên và đuôi mở rộng" : "Name and extension");

                if (ApplyTo == "Extension") cmbApplyTo.SelectedIndex = 1;
                else if (ApplyTo == "Name and extension") cmbApplyTo.SelectedIndex = 2;
                else cmbApplyTo.SelectedIndex = 0;

                cmbApplyTo.SelectionChanged += (s, e) =>
                {
                    int idx = cmbApplyTo.SelectedIndex;
                    if (idx == 1) ApplyTo = "Extension";
                    else if (idx == 2) ApplyTo = "Name and extension";
                    else ApplyTo = "Name";
                };
                panel.Children.Add(cmbApplyTo);

                if (mainWin != null)
                {
                    mainWin.StyleComboBoxPopup(cmbApplyTo);
                }
            }

            public abstract string Apply(string currentName, int index, int totalCount, string originalPath);
            public abstract FrameworkElement CreateConfigUI();
        }

        // Concrete Rename Methods
        public class NewNameMethod : RenameMethodBase
        {
            private string _format = "<Name>";

            public NewNameMethod()
            {
                Name = "New name";
                IconText = "🏷️";
            }

            public string Format
            {
                get => _format;
                set
                {
                    if (_format != value)
                    {
                        _format = value;
                        OnPropertyChanged(nameof(Format));
                        TriggerUpdate();
                    }
                }
            }

            public override string Apply(string currentName, int index, int totalCount, string originalPath)
            {
                if (string.IsNullOrEmpty(Format)) return currentName;

                string namePart = Path.GetFileNameWithoutExtension(currentName);
                string ext = Path.GetExtension(currentName);
                if (ext.StartsWith(".")) ext = ext.Substring(1);

                string result = Format;

                // Replace tags
                result = result.Replace("<Name>", namePart);
                result = result.Replace("<Ext>", ext);

                // <Counter:N>
                result = Regex.Replace(result, @"<Counter:(\d+)>", m =>
                {
                    int padding = int.Parse(m.Groups[1].Value);
                    return (index + 1).ToString().PadLeft(padding, '0');
                });
                result = result.Replace("<Counter>", (index + 1).ToString());

                // <Date:format>
                result = Regex.Replace(result, @"<Date:([^>]+)>", m =>
                {
                    try
                    {
                        return DateTime.Now.ToString(m.Groups[1].Value);
                    }
                    catch
                    {
                        return DateTime.Now.ToString("yyyy-MM-dd");
                    }
                });
                result = result.Replace("<Date>", DateTime.Now.ToString("yyyy-MM-dd"));

                // Apply to logic
                if (ApplyTo == "Name")
                {
                    return string.IsNullOrEmpty(ext) ? result : result + "." + ext;
                }
                else if (ApplyTo == "Extension")
                {
                    return string.IsNullOrEmpty(result) ? namePart : namePart + "." + result;
                }
                return result;
            }

            public override FrameworkElement CreateConfigUI()
            {
                var mainWin = MainWindow.Instance;
                bool vi = mainWin != null && mainWin._isVietnameseUi;

                var panel = new StackPanel { Margin = new Thickness(5) };
                panel.Children.Add(new TextBlock
                {
                    Text = vi ? "Định dạng tên mới:" : "New name format:",
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 5),
                    FontWeight = FontWeights.Bold
                });

                var txtFormat = new TextBox
                {
                    Text = Format,
                    Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White,
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(5, 0, 5, 0)
                };
                txtFormat.TextChanged += (s, e) => Format = txtFormat.Text;
                panel.Children.Add(txtFormat);

                panel.Children.Add(new TextBlock
                {
                    Text = vi ? "Các thẻ tag hỗ trợ:\n- <Name>: Tên gốc (không gồm đuôi)\n- <Ext>: Đuôi file gốc\n- <Counter:3>: Số thứ tự tăng dần (ví dụ: 001)\n- <Date:yyyy-MM-dd>: Ngày tháng hiện tại"
                             : "Supported tags:\n- <Name>: Original name (no extension)\n- <Ext>: Original extension\n- <Counter:3>: Auto-increment index (e.g. 001)\n- <Date:yyyy-MM-dd>: Current date",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0x9E, 0xB2)),
                    Margin = new Thickness(0, 10, 0, 0),
                    FontSize = 11,
                    LineHeight = 16
                });

                AddCommonConfigControls(panel);
                return panel;
            }
        }

        public class ReplaceMethod : RenameMethodBase
        {
            private string _findText = "";
            private string _replaceText = "";
            private bool _caseSensitive = false;

            public ReplaceMethod()
            {
                Name = "Replace";
                IconText = "🔀";
            }

            public string FindText
            {
                get => _findText;
                set
                {
                    if (_findText != value)
                    {
                        _findText = value;
                        OnPropertyChanged(nameof(FindText));
                        TriggerUpdate();
                    }
                }
            }

            public string ReplaceText
            {
                get => _replaceText;
                set
                {
                    if (_replaceText != value)
                    {
                        _replaceText = value;
                        OnPropertyChanged(nameof(ReplaceText));
                        TriggerUpdate();
                    }
                }
            }

            public bool CaseSensitive
            {
                get => _caseSensitive;
                set
                {
                    if (_caseSensitive != value)
                    {
                        _caseSensitive = value;
                        OnPropertyChanged(nameof(CaseSensitive));
                        TriggerUpdate();
                    }
                }
            }

            public override string Apply(string currentName, int index, int totalCount, string originalPath)
            {
                if (string.IsNullOrEmpty(FindText)) return currentName;

                string namePart = Path.GetFileNameWithoutExtension(currentName);
                string extPart = Path.GetExtension(currentName);

                string target = currentName;
                if (ApplyTo == "Name") target = namePart;
                else if (ApplyTo == "Extension") target = extPart.StartsWith(".") ? extPart.Substring(1) : extPart;

                string result = target;

                if (UseRegex)
                {
                    try
                    {
                        var options = CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                        if (Backwards)
                        {
                            var matches = Regex.Matches(target, FindText, options);
                            if (matches.Count > 0)
                            {
                                var lastMatch = matches[matches.Count - 1];
                                result = target.Substring(0, lastMatch.Index) + ReplaceText + target.Substring(lastMatch.Index + lastMatch.Length);
                            }
                        }
                        else
                        {
                            result = Regex.Replace(target, FindText, ReplaceText, options);
                        }
                    }
                    catch { }
                }
                else
                {
                    var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                    if (Backwards)
                    {
                        int pos = target.LastIndexOf(FindText, comparison);
                        if (pos >= 0)
                        {
                            result = target.Substring(0, pos) + ReplaceText + target.Substring(pos + FindText.Length);
                        }
                    }
                    else
                    {
                        int pos;
                        while ((pos = result.IndexOf(FindText, comparison)) >= 0)
                        {
                            result = result.Substring(0, pos) + ReplaceText + result.Substring(pos + FindText.Length);
                        }
                    }
                }

                if (ApplyTo == "Name")
                {
                    return result + extPart;
                }
                else if (ApplyTo == "Extension")
                {
                    return extPart.StartsWith(".") ? namePart + "." + result : namePart + result;
                }
                return result;
            }

            public override FrameworkElement CreateConfigUI()
            {
                var mainWin = MainWindow.Instance;
                bool vi = mainWin != null && mainWin._isVietnameseUi;

                var panel = new StackPanel { Margin = new Thickness(5) };

                panel.Children.Add(new TextBlock { Text = vi ? "Tìm kiếm chuỗi:" : "Find text:", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
                var txtFind = new TextBox
                {
                    Text = FindText,
                    Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White,
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                txtFind.TextChanged += (s, e) => FindText = txtFind.Text;
                panel.Children.Add(txtFind);

                panel.Children.Add(new TextBlock { Text = vi ? "Thay thế bằng:" : "Replace with:", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 5) });
                var txtReplace = new TextBox
                {
                    Text = ReplaceText,
                    Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White,
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                txtReplace.TextChanged += (s, e) => ReplaceText = txtReplace.Text;
                panel.Children.Add(txtReplace);

                var chkCase = new CheckBox
                {
                    Content = vi ? "Phân biệt chữ hoa / thường" : "Case sensitive",
                    IsChecked = CaseSensitive,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                chkCase.Checked += (s, e) => CaseSensitive = true;
                chkCase.Unchecked += (s, e) => CaseSensitive = false;
                panel.Children.Add(chkCase);

                AddCommonConfigControls(panel);
                return panel;
            }
        }

        public class RenumberMethod : RenameMethodBase
        {
            private string _position = "Append"; // Prepend, Append
            private int _startNumber = 1;
            private int _step = 1;
            private int _padding = 3;

            public RenumberMethod()
            {
                Name = "Renumber";
                IconText = "🔢";
            }

            public string Position
            {
                get => _position;
                set
                {
                    if (_position != value)
                    {
                        _position = value;
                        OnPropertyChanged(nameof(Position));
                        TriggerUpdate();
                    }
                }
            }

            public int StartNumber
            {
                get => _startNumber;
                set
                {
                    if (_startNumber != value)
                    {
                        _startNumber = value;
                        OnPropertyChanged(nameof(StartNumber));
                        TriggerUpdate();
                    }
                }
            }

            public int Step
            {
                get => _step;
                set
                {
                    if (_step != value)
                    {
                        _step = value;
                        OnPropertyChanged(nameof(Step));
                        TriggerUpdate();
                    }
                }
            }

            public int Padding
            {
                get => _padding;
                set
                {
                    if (_padding != value)
                    {
                        _padding = value;
                        OnPropertyChanged(nameof(Padding));
                        TriggerUpdate();
                    }
                }
            }

            public override string Apply(string currentName, int index, int totalCount, string originalPath)
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(currentName);
                string extPart = Path.GetExtension(currentName);

                string target = currentName;
                if (ApplyTo == "Name") target = nameWithoutExt;
                else if (ApplyTo == "Extension") target = extPart.StartsWith(".") ? extPart.Substring(1) : extPart;

                int num = StartNumber + (index * Step);
                string numStr = num.ToString().PadLeft(Padding, '0');

                string result = target;
                if (Position == "Prepend")
                {
                    result = numStr + "_" + target;
                }
                else
                {
                    result = target + "_" + numStr;
                }

                if (ApplyTo == "Name")
                {
                    return result + extPart;
                }
                else if (ApplyTo == "Extension")
                {
                    return extPart.StartsWith(".") ? nameWithoutExt + "." + result : nameWithoutExt + result;
                }
                return result;
            }

            public override FrameworkElement CreateConfigUI()
            {
                var mainWin = MainWindow.Instance;
                bool vi = mainWin != null && mainWin._isVietnameseUi;
                var panel = new StackPanel { Margin = new Thickness(5) };

                panel.Children.Add(new TextBlock { Text = vi ? "Vị trí đặt số:" : "Number position:", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
                var comboPos = new ComboBox
                {
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("CyberpunkComboBox") as Style
                };
                comboPos.Items.Add(vi ? "Prepend (Thêm vào đầu)" : "Prepend");
                comboPos.Items.Add(vi ? "Append (Thêm vào cuối)" : "Append");
                comboPos.SelectedIndex = Position == "Prepend" ? 0 : 1;
                comboPos.SelectionChanged += (s, e) => Position = comboPos.SelectedIndex == 0 ? "Prepend" : "Append";
                panel.Children.Add(comboPos);

                if (mainWin != null)
                {
                    mainWin.StyleComboBoxPopup(comboPos);
                }

                panel.Children.Add(new TextBlock { Text = vi ? "Bắt đầu từ số:" : "Start number:", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 5) });
                var txtStart = new TextBox
                {
                    Text = StartNumber.ToString(),
                    Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White,
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                txtStart.TextChanged += (s, e) => { if (int.TryParse(txtStart.Text, out int val)) StartNumber = val; };
                panel.Children.Add(txtStart);

                panel.Children.Add(new TextBlock { Text = vi ? "Bước nhảy:" : "Step:", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 5) });
                var txtStep = new TextBox
                {
                    Text = Step.ToString(),
                    Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White,
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                txtStep.TextChanged += (s, e) => { if (int.TryParse(txtStep.Text, out int val)) Step = val; };
                panel.Children.Add(txtStep);

                panel.Children.Add(new TextBlock { Text = vi ? "Độ rộng chữ số (Padding):" : "Number padding:", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 5) });
                var txtPad = new TextBox
                {
                    Text = Padding.ToString(),
                    Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White,
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                txtPad.TextChanged += (s, e) => { if (int.TryParse(txtPad.Text, out int val)) Padding = val; };
                panel.Children.Add(txtPad);

                AddCommonConfigControls(panel);
                return panel;
            }
        }

        public class NewCaseMethod : RenameMethodBase
        {
            private string _caseType = "UPPERCASE"; // UPPERCASE, lowercase, Title Case, Sentence case

            public NewCaseMethod()
            {
                Name = "New case";
                IconText = "🔠";
            }

            public string CaseType
            {
                get => _caseType;
                set
                {
                    if (_caseType != value)
                    {
                        _caseType = value;
                        OnPropertyChanged(nameof(CaseType));
                        TriggerUpdate();
                    }
                }
            }

            public override string Apply(string currentName, int index, int totalCount, string originalPath)
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(currentName);
                string extPart = Path.GetExtension(currentName);

                string target = currentName;
                if (ApplyTo == "Name") target = nameWithoutExt;
                else if (ApplyTo == "Extension") target = extPart.StartsWith(".") ? extPart.Substring(1) : extPart;

                string result = target;
                switch (CaseType)
                {
                    case "UPPERCASE":
                        result = result.ToUpperInvariant();
                        break;
                    case "lowercase":
                        result = result.ToLowerInvariant();
                        break;
                    case "Title Case":
                        result = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(result.ToLowerInvariant());
                        break;
                    case "Sentence case":
                        if (result.Length > 0)
                        {
                            result = char.ToUpperInvariant(result[0]) + result.Substring(1).ToLowerInvariant();
                        }
                        break;
                }

                if (ApplyTo == "Name")
                {
                    return result + extPart;
                }
                else if (ApplyTo == "Extension")
                {
                    return extPart.StartsWith(".") ? nameWithoutExt + "." + result : nameWithoutExt + result;
                }
                return result;
            }

            public override FrameworkElement CreateConfigUI()
            {
                var mainWin = MainWindow.Instance;
                bool vi = mainWin != null && mainWin._isVietnameseUi;
                var panel = new StackPanel { Margin = new Thickness(5) };

                panel.Children.Add(new TextBlock { Text = vi ? "Chuyển đổi kiểu chữ:" : "Change case:", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
                var comboCase = new ComboBox
                {
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = Application.Current.TryFindResource("CyberpunkComboBox") as Style
                };
                comboCase.Items.Add(vi ? "UPPERCASE (VIẾT HOA)" : "UPPERCASE");
                comboCase.Items.Add(vi ? "lowercase (viết thường)" : "lowercase");
                comboCase.Items.Add(vi ? "Title Case (Viết Hoa Chữ Đầu)" : "Title Case");
                comboCase.Items.Add(vi ? "Sentence case (Viết hoa đầu câu)" : "Sentence case");

                int idx = 0;
                if (CaseType == "lowercase") idx = 1;
                else if (CaseType == "Title Case") idx = 2;
                else if (CaseType == "Sentence case") idx = 3;

                comboCase.SelectedIndex = idx;
                comboCase.SelectionChanged += (s, e) =>
                {
                    if (comboCase.SelectedIndex == 0) CaseType = "UPPERCASE";
                    else if (comboCase.SelectedIndex == 1) CaseType = "lowercase";
                    else if (comboCase.SelectedIndex == 2) CaseType = "Title Case";
                    else if (comboCase.SelectedIndex == 3) CaseType = "Sentence case";
                };
                panel.Children.Add(comboCase);

                if (mainWin != null)
                {
                    mainWin.StyleComboBoxPopup(comboCase);
                }

                AddCommonConfigControls(panel);
                return panel;
            }
        }

         public class RemoveMethod : RenameMethodBase
         {
             private int _startPos = 1;
             private int _length = 1;
             private bool _toEnd = false;
             private string _deleteAfterText = string.Empty;
 
             public RemoveMethod()
             {
                 Name = "Remove";
                 IconText = "✂️";
             }
 
             public int StartPos
             {
                 get => _startPos;
                 set
                 {
                     if (_startPos != value)
                     {
                         _startPos = value;
                         OnPropertyChanged(nameof(StartPos));
                         TriggerUpdate();
                     }
                 }
             }
 
             public int Length
             {
                 get => _length;
                 set
                 {
                     if (_length != value)
                     {
                         _length = value;
                         OnPropertyChanged(nameof(Length));
                         TriggerUpdate();
                     }
                 }
             }
 
             public bool ToEnd
             {
                 get => _toEnd;
                 set
                 {
                     if (_toEnd != value)
                     {
                         _toEnd = value;
                         OnPropertyChanged(nameof(ToEnd));
                         TriggerUpdate();
                     }
                 }
             }

             public string DeleteAfterText
             {
                 get => _deleteAfterText;
                 set
                 {
                     if (_deleteAfterText != value)
                     {
                         _deleteAfterText = value;
                         OnPropertyChanged(nameof(DeleteAfterText));
                         TriggerUpdate();
                     }
                 }
             }
 
             public override string Apply(string currentName, int index, int totalCount, string originalPath)
             {
                 string nameWithoutExt = Path.GetFileNameWithoutExtension(currentName);
                 string extPart = Path.GetExtension(currentName);
 
                 string target = currentName;
                 if (ApplyTo == "Name") target = nameWithoutExt;
                 else if (ApplyTo == "Extension") target = extPart.StartsWith(".") ? extPart.Substring(1) : extPart;
 
                 if (target.Length == 0) return currentName;
 
                 string result = target;

                 // Tính năng "delete after" (xóa từ chuỗi khớp đến cuối hàng, bao gồm cả chuỗi đó)
                 if (!string.IsNullOrEmpty(DeleteAfterText))
                 {
                     int idx = target.IndexOf(DeleteAfterText, StringComparison.OrdinalIgnoreCase);
                     if (idx >= 0)
                     {
                         result = target.Substring(0, idx);
                     }
                 }
                 else if (Backwards)
                 {
                     if (StartPos <= 0)
                     {
                         // Bỏ qua không xóa gì
                     }
                     else
                     {
                         int startIdx = target.Length - StartPos;
                         if (startIdx < 0) startIdx = 0;
                         if (startIdx >= target.Length) startIdx = target.Length - 1;
 
                         if (ToEnd)
                         {
                             result = target.Substring(startIdx + 1);
                         }
                         else
                         {
                             int removeStart = Math.Max(0, startIdx - Length + 1);
                             int len = Math.Min(Length, startIdx + 1);
                             if (len > 0)
                             {
                                 result = target.Remove(removeStart, len);
                             }
                         }
                     }
                 }
                 else
                 {
                     if (StartPos <= 0)
                     {
                         // Bỏ qua không xóa gì
                     }
                     else
                     {
                         int startIdx = StartPos - 1; // 1-based to 0-based
                         if (startIdx < target.Length)
                         {
                             if (ToEnd)
                             {
                                 result = target.Substring(0, startIdx);
                             }
                             else
                             {
                                 int len = Math.Min(Length, target.Length - startIdx);
                                 if (len > 0)
                                 {
                                     result = target.Remove(startIdx, len);
                                 }
                             }
                         }
                     }
                 }
 
                 if (ApplyTo == "Name")
                 {
                     return result + extPart;
                 }
                 else if (ApplyTo == "Extension")
                 {
                     return extPart.StartsWith(".") ? nameWithoutExt + "." + result : nameWithoutExt + result;
                 }
                 return result;
             }
 
             public override FrameworkElement CreateConfigUI()
             {
                 var mainWin = MainWindow.Instance;
                 bool vi = mainWin != null && mainWin._isVietnameseUi;
                 var panel = new StackPanel { Margin = new Thickness(5) };
 
                 panel.Children.Add(new TextBlock { Text = vi ? "Vị trí bắt đầu xóa (tính từ 1):" : "Start position (1-based):", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 5) });
                 
                 var startGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
                 startGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                 startGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                 var txtStart = new TextBox
                 {
                     Text = StartPos.ToString(),
                     Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)),
                     BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                     Foreground = Brushes.White,
                     CaretBrush = Brushes.White,
                     Height = 28,
                     VerticalContentAlignment = VerticalAlignment.Center
                 };
                 txtStart.TextChanged += (s, e) => { if (int.TryParse(txtStart.Text, out int val)) StartPos = val; };
                 Grid.SetColumn(txtStart, 0);
                 startGrid.Children.Add(txtStart);

                 var startSpinPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 0, 0, 0) };
                 var btnStartUp = new Button
                 {
                     Content = "▲",
                     Width = 26,
                     Height = 28,
                     Style = mainWin?.TryFindResource("CompactCyanButton") as Style,
                     FontWeight = FontWeights.Bold,
                     VerticalContentAlignment = VerticalAlignment.Center,
                     HorizontalContentAlignment = HorizontalAlignment.Center
                 };
                 btnStartUp.Click += (s, e) =>
                 {
                     StartPos++;
                     txtStart.Text = StartPos.ToString();
                 };
                 var btnStartDown = new Button
                 {
                     Content = "▼",
                     Width = 26,
                     Height = 28,
                     Style = mainWin?.TryFindResource("CompactPinkButton") as Style,
                     FontWeight = FontWeights.Bold,
                     VerticalContentAlignment = VerticalAlignment.Center,
                     HorizontalContentAlignment = HorizontalAlignment.Center
                 };
                 btnStartDown.Click += (s, e) =>
                 {
                     if (StartPos > 1)
                     {
                         StartPos--;
                         txtStart.Text = StartPos.ToString();
                     }
                 };
                 startSpinPanel.Children.Add(btnStartUp);
                 startSpinPanel.Children.Add(btnStartDown);
                 Grid.SetColumn(startSpinPanel, 1);
                 startGrid.Children.Add(startSpinPanel);
                 panel.Children.Add(startGrid);
 
                 var chkToEnd = new CheckBox
                 {
                     Content = vi ? "Xóa đến hết tên file" : "Remove to end of filename",
                     IsChecked = ToEnd,
                     Foreground = Brushes.White,
                     Margin = new Thickness(0, 8, 0, 0)
                 };
                 panel.Children.Add(chkToEnd);
 
                 var lblLen = new TextBlock { Text = vi ? "Số ký tự muốn xóa:" : "Number of characters to remove:", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 5) };
                 panel.Children.Add(lblLen);
 
                 var lenGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
                 lenGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                 lenGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
 
                 var txtLen = new TextBox
                 {
                     Text = Length.ToString(),
                     Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)),
                     BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                     Foreground = Brushes.White,
                     CaretBrush = Brushes.White,
                     Height = 28,
                     VerticalContentAlignment = VerticalAlignment.Center
                 };
                 txtLen.TextChanged += (s, e) => { if (int.TryParse(txtLen.Text, out int val)) Length = val; };
                 Grid.SetColumn(txtLen, 0);
                 lenGrid.Children.Add(txtLen);
 
                 var spinPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 0, 0, 0) };
                 var btnUp = new Button
                 {
                     Content = "▲",
                     Width = 26,
                     Height = 28,
                     Style = mainWin?.TryFindResource("CompactCyanButton") as Style,
                     FontWeight = FontWeights.Bold,
                     VerticalContentAlignment = VerticalAlignment.Center,
                     HorizontalContentAlignment = HorizontalAlignment.Center
                 };
                 btnUp.Click += (s, e) =>
                 {
                     Length++;
                     txtLen.Text = Length.ToString();
                 };
                 var btnDown = new Button
                 {
                     Content = "▼",
                     Width = 26,
                     Height = 28,
                     Style = mainWin?.TryFindResource("CompactPinkButton") as Style,
                     FontWeight = FontWeights.Bold,
                     VerticalContentAlignment = VerticalAlignment.Center,
                     HorizontalContentAlignment = HorizontalAlignment.Center
                 };
                 btnDown.Click += (s, e) =>
                 {
                     if (Length > 0)
                     {
                         Length--;
                         txtLen.Text = Length.ToString();
                     }
                 };
                 spinPanel.Children.Add(btnUp);
                 spinPanel.Children.Add(btnDown);
                 Grid.SetColumn(spinPanel, 1);
                 lenGrid.Children.Add(spinPanel);
 
                 panel.Children.Add(lenGrid);

                 // Tính năng delete after
                 panel.Children.Add(new TextBlock { Text = vi ? "Xóa từ chữ (Delete after):" : "Delete after text:", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 5) });
                 var txtDeleteAfter = new TextBox
                 {
                     Text = DeleteAfterText,
                     Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)),
                     BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                     Foreground = Brushes.White,
                     CaretBrush = Brushes.White,
                     Height = 28,
                     VerticalContentAlignment = VerticalAlignment.Center
                 };
                 txtDeleteAfter.TextChanged += (s, e) => { DeleteAfterText = txtDeleteAfter.Text; };
                 panel.Children.Add(txtDeleteAfter);
 
                 chkToEnd.Checked += (s, e) =>
                 {
                     ToEnd = true;
                     txtLen.IsEnabled = false;
                     btnUp.IsEnabled = false;
                     btnDown.IsEnabled = false;
                     lblLen.Opacity = 0.5;
                 };
                 chkToEnd.Unchecked += (s, e) =>
                 {
                     ToEnd = false;
                     txtLen.IsEnabled = true;
                     btnUp.IsEnabled = true;
                     btnDown.IsEnabled = true;
                     lblLen.Opacity = 1.0;
                 };
 
                 // Init visual state
                 if (ToEnd)
                 {
                     txtLen.IsEnabled = false;
                     btnUp.IsEnabled = false;
                     btnDown.IsEnabled = false;
                     lblLen.Opacity = 0.5;
                 }
 
                 AddCommonConfigControls(panel);
                 return panel;
             }
         }

        public class OptimizeZeroMethod : RenameMethodBase
        {
            private int _zeroPadding = 4;

            public OptimizeZeroMethod()
            {
                Name = "Optimize zero";
                IconText = "0️⃣";
            }

            public int ZeroPadding
            {
                get => _zeroPadding;
                set
                {
                    if (_zeroPadding != value)
                    {
                        _zeroPadding = value;
                        OnPropertyChanged(nameof(ZeroPadding));
                        TriggerUpdate();
                    }
                }
            }

            public override string Apply(string currentName, int index, int totalCount, string originalPath)
            {
                string nameWithoutExt = Path.GetFileNameWithoutExtension(currentName);
                string extPart = Path.GetExtension(currentName);

                string target = currentName;
                if (ApplyTo == "Name") target = nameWithoutExt;
                else if (ApplyTo == "Extension") target = extPart.StartsWith(".") ? extPart.Substring(1) : extPart;

                string result = Regex.Replace(target, @"\d+", m => m.Value.PadLeft(ZeroPadding, '0'));

                if (ApplyTo == "Name")
                {
                    return result + extPart;
                }
                else if (ApplyTo == "Extension")
                {
                    return extPart.StartsWith(".") ? nameWithoutExt + "." + result : nameWithoutExt + result;
                }
                return result;
            }

            public override FrameworkElement CreateConfigUI()
            {
                var mainWin = MainWindow.Instance;
                bool vi = mainWin != null && mainWin._isVietnameseUi;
                var panel = new StackPanel { Margin = new Thickness(5) };

                panel.Children.Add(new TextBlock 
                { 
                    Text = vi ? "Độ rộng chữ số (Zero padding):" : "Zero padding width:", 
                    Foreground = Brushes.White, 
                    Margin = new Thickness(0, 0, 0, 5) 
                });

                var cmbPad = new ComboBox
                {
                    Height = 28,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Style = mainWin?.TryFindResource("CyberpunkComboBox") as Style
                };
                cmbPad.Items.Add("4");
                cmbPad.Items.Add("5");
                cmbPad.Items.Add("6");
                cmbPad.Items.Add("7");

                if (ZeroPadding == 5) cmbPad.SelectedIndex = 1;
                else if (ZeroPadding == 6) cmbPad.SelectedIndex = 2;
                else if (ZeroPadding == 7) cmbPad.SelectedIndex = 3;
                else cmbPad.SelectedIndex = 0;

                cmbPad.SelectionChanged += (s, e) =>
                {
                    int idx = cmbPad.SelectedIndex;
                    if (idx == 1) ZeroPadding = 5;
                    else if (idx == 2) ZeroPadding = 6;
                    else if (idx == 3) ZeroPadding = 7;
                    else ZeroPadding = 4;
                };
                panel.Children.Add(cmbPad);

                if (mainWin != null)
                {
                    mainWin.StyleComboBoxPopup(cmbPad);
                }

                AddCommonConfigControls(panel);
                return panel;
            }
        }

        // Rename UI States and Collections
        private ObservableCollection<RenameFileItem> _renameFiles = new ObservableCollection<RenameFileItem>();
        private ObservableCollection<RenameMethodBase> _renameMethods = new ObservableCollection<RenameMethodBase>();
         private ListBox _lstRenameMethods;
         private ContentControl _renameConfigContainer;
         private TextBlock _lblRenameStatus;

        // Undo/Redo history stacks for Rename Methods
        private readonly Stack<List<RenameMethodBase>> _renameMethodsUndoStack = new Stack<List<RenameMethodBase>>();
        private readonly Stack<List<RenameMethodBase>> _renameMethodsRedoStack = new Stack<List<RenameMethodBase>>();
        private bool _isApplyingHistoryState = false;

        private void SaveRenameMethodsStateToHistory()
        {
            if (_isApplyingHistoryState) return;
            
            // Create deep copy/clone of current methods state
            var stateSnapshot = new List<RenameMethodBase>();
            foreach (var m in _renameMethods)
            {
                // Clone basic details
                RenameMethodBase cloned = null;
                if (m is NewNameMethod n) cloned = new NewNameMethod { Format = n.Format };
                else if (m is ReplaceMethod r) cloned = new ReplaceMethod { FindText = r.FindText, ReplaceText = r.ReplaceText, CaseSensitive = r.CaseSensitive };
                else if (m is RenumberMethod rn) cloned = new RenumberMethod { Position = rn.Position, StartNumber = rn.StartNumber, Step = rn.Step, Padding = rn.Padding };
                else if (m is NewCaseMethod nc) cloned = new NewCaseMethod { CaseType = nc.CaseType };
                else if (m is RemoveMethod rm) cloned = new RemoveMethod { StartPos = rm.StartPos, Length = rm.Length, ToEnd = rm.ToEnd, DeleteAfterText = rm.DeleteAfterText };
                else if (m is OptimizeZeroMethod oz) cloned = new OptimizeZeroMethod { ZeroPadding = oz.ZeroPadding };

                if (cloned != null)
                {
                    cloned.IsEnabled = m.IsEnabled;
                    cloned.Backwards = m.Backwards;
                    cloned.UseRegex = m.UseRegex;
                    cloned.ApplyTo = m.ApplyTo;
                    stateSnapshot.Add(cloned);
                }
            }
            
            _renameMethodsUndoStack.Push(stateSnapshot);
            _renameMethodsRedoStack.Clear(); // Clear redo on new action
            UpdateUndoRedoButtonStates();
        }

        private void UndoRenameMethodsState()
        {
            if (_renameMethodsUndoStack.Count == 0) return;

            _isApplyingHistoryState = true;
            try
            {
                // Push current state to Redo stack
                var currentState = new List<RenameMethodBase>();
                foreach (var m in _renameMethods)
                {
                    RenameMethodBase cloned = null;
                    if (m is NewNameMethod n) cloned = new NewNameMethod { Format = n.Format };
                    else if (m is ReplaceMethod r) cloned = new ReplaceMethod { FindText = r.FindText, ReplaceText = r.ReplaceText, CaseSensitive = r.CaseSensitive };
                    else if (m is RenumberMethod rn) cloned = new RenumberMethod { Position = rn.Position, StartNumber = rn.StartNumber, Step = rn.Step, Padding = rn.Padding };
                    else if (m is NewCaseMethod nc) cloned = new NewCaseMethod { CaseType = nc.CaseType };
                    else if (m is RemoveMethod rm) cloned = new RemoveMethod { StartPos = rm.StartPos, Length = rm.Length, ToEnd = rm.ToEnd, DeleteAfterText = rm.DeleteAfterText };
                    else if (m is OptimizeZeroMethod oz) cloned = new OptimizeZeroMethod { ZeroPadding = oz.ZeroPadding };

                    if (cloned != null)
                    {
                        cloned.IsEnabled = m.IsEnabled;
                        cloned.Backwards = m.Backwards;
                        cloned.UseRegex = m.UseRegex;
                        cloned.ApplyTo = m.ApplyTo;
                        currentState.Add(cloned);
                    }
                }
                _renameMethodsRedoStack.Push(currentState);

                // Restore previous state
                var prevState = _renameMethodsUndoStack.Pop();
                
                // Clear and re-add
                foreach (var m in _renameMethods)
                {
                    m.RequestUpdate -= RecalculateRenamePreviews;
                }
                _renameMethods.Clear();

                foreach (var m in prevState)
                {
                    m.RequestUpdate += RecalculateRenamePreviews;
                    _renameMethods.Add(m);
                }

                if (_renameMethods.Count > 0)
                {
                    _lstRenameMethods.SelectedItem = _renameMethods[0];
                }
                
                RecalculateRenamePreviews();
                ShowSelectedMethodConfig();
                UpdateUndoRedoButtonStates();
            }
            finally
            {
                _isApplyingHistoryState = false;
            }
        }

        private void RedoRenameMethodsState()
        {
            if (_renameMethodsRedoStack.Count == 0) return;

            _isApplyingHistoryState = true;
            try
            {
                // Push current state to Undo stack
                var currentState = new List<RenameMethodBase>();
                foreach (var m in _renameMethods)
                {
                    RenameMethodBase cloned = null;
                    if (m is NewNameMethod n) cloned = new NewNameMethod { Format = n.Format };
                    else if (m is ReplaceMethod r) cloned = new ReplaceMethod { FindText = r.FindText, ReplaceText = r.ReplaceText, CaseSensitive = r.CaseSensitive };
                    else if (m is RenumberMethod rn) cloned = new RenumberMethod { Position = rn.Position, StartNumber = rn.StartNumber, Step = rn.Step, Padding = rn.Padding };
                    else if (m is NewCaseMethod nc) cloned = new NewCaseMethod { CaseType = nc.CaseType };
                    else if (m is RemoveMethod rm) cloned = new RemoveMethod { StartPos = rm.StartPos, Length = rm.Length, ToEnd = rm.ToEnd, DeleteAfterText = rm.DeleteAfterText };
                    else if (m is OptimizeZeroMethod oz) cloned = new OptimizeZeroMethod { ZeroPadding = oz.ZeroPadding };

                    if (cloned != null)
                    {
                        cloned.IsEnabled = m.IsEnabled;
                        cloned.Backwards = m.Backwards;
                        cloned.UseRegex = m.UseRegex;
                        cloned.ApplyTo = m.ApplyTo;
                        currentState.Add(cloned);
                    }
                }
                _renameMethodsUndoStack.Push(currentState);

                // Restore next state
                var nextState = _renameMethodsRedoStack.Pop();
                
                // Clear and re-add
                foreach (var m in _renameMethods)
                {
                    m.RequestUpdate -= RecalculateRenamePreviews;
                }
                _renameMethods.Clear();

                foreach (var m in nextState)
                {
                    m.RequestUpdate += RecalculateRenamePreviews;
                    _renameMethods.Add(m);
                }

                if (_renameMethods.Count > 0)
                {
                    _lstRenameMethods.SelectedItem = _renameMethods[0];
                }
                
                RecalculateRenamePreviews();
                ShowSelectedMethodConfig();
                UpdateUndoRedoButtonStates();
            }
            finally
            {
                _isApplyingHistoryState = false;
            }
        }

        private void UpdateUndoRedoButtonStates()
        {
            if (_btnRenameUndo != null) _btnRenameUndo.IsEnabled = _renameMethodsUndoStack.Count > 0;
            if (_btnRenameRedo != null) _btnRenameRedo.IsEnabled = _renameMethodsRedoStack.Count > 0;
        }

        // Struct to hold individual file rename operation details for undoing/redoing
        private struct FileRenameOperation
        {
            public string OldPath { get; set; }
            public string NewPath { get; set; }
            public bool IsDirectory { get; set; }
        }

        // History stacks for actual batch renaming operations
        private readonly Stack<List<FileRenameOperation>> _batchRenameUndoStack = new Stack<List<FileRenameOperation>>();
        private readonly Stack<List<FileRenameOperation>> _batchRenameRedoStack = new Stack<List<FileRenameOperation>>();

        private void UpdateBatchUndoRedoButtons()
        {
            if (_btnBatchUndo != null) _btnBatchUndo.IsEnabled = _batchRenameUndoStack.Count > 0;
            if (_btnBatchRedo != null) _btnBatchRedo.IsEnabled = _batchRenameRedoStack.Count > 0;
        }

        private void UndoBatchRename()
        {
            if (_batchRenameUndoStack.Count == 0) return;

            var operations = _batchRenameUndoStack.Pop();
            var redoOps = new List<FileRenameOperation>();
            int successCount = 0;
            int failCount = 0;

            // Rollback in reverse order
            for (int i = operations.Count - 1; i >= 0; i--)
            {
                var op = operations[i];
                try
                {
                    // To undo, we move NewPath back to OldPath
                    if (op.IsDirectory)
                    {
                        if (Directory.Exists(op.NewPath))
                        {
                            Directory.Move(op.NewPath, op.OldPath);
                            successCount++;
                            redoOps.Add(op);
                        }
                    }
                    else
                    {
                        if (File.Exists(op.NewPath))
                        {
                            File.Move(op.NewPath, op.OldPath);
                            successCount++;
                            redoOps.Add(op);
                        }
                    }

                    // Update UI model path if matching
                    var match = _renameFiles.FirstOrDefault(f => string.Equals(f.OriginalPath, op.NewPath, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        match.OriginalPath = op.OldPath;
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    Log($"[Undo Rename Error] {ex.Message}");
                }
            }

            if (redoOps.Count > 0)
            {
                _batchRenameRedoStack.Push(redoOps);
            }

            RecalculateRenamePreviews();
            UpdateBatchUndoRedoButtons();
            MessageBox.Show($"Hoàn tất Undo đổi tên thực tế!\nThành công: {successCount}\nThất bại: {failCount}", "Rename Undo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RedoBatchRename()
        {
            if (_batchRenameRedoStack.Count == 0) return;

            var operations = _batchRenameRedoStack.Pop();
            var undoOps = new List<FileRenameOperation>();
            int successCount = 0;
            int failCount = 0;

            // Apply forward
            foreach (var op in operations)
            {
                try
                {
                    // To redo, we move OldPath to NewPath
                    if (op.IsDirectory)
                    {
                        if (Directory.Exists(op.OldPath))
                        {
                            Directory.Move(op.OldPath, op.NewPath);
                            successCount++;
                            undoOps.Add(op);
                        }
                    }
                    else
                    {
                        if (File.Exists(op.OldPath))
                        {
                            File.Move(op.OldPath, op.NewPath);
                            successCount++;
                            undoOps.Add(op);
                        }
                    }

                    // Update UI model path if matching
                    var match = _renameFiles.FirstOrDefault(f => string.Equals(f.OriginalPath, op.OldPath, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        match.OriginalPath = op.NewPath;
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    Log($"[Redo Rename Error] {ex.Message}");
                }
            }

            if (undoOps.Count > 0)
            {
                _batchRenameUndoStack.Push(undoOps);
            }

            RecalculateRenamePreviews();
            UpdateBatchUndoRedoButtons();
            MessageBox.Show($"Hoàn tất Redo đổi tên thực tế!\nThành công: {successCount}\nThất bại: {failCount}", "Rename Redo", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Fields for localization & hotkey control in Tab Rename
        private Button _btnAddFile;
        private Button _btnAddFolder;
        private Button _btnDeleteFile;
        private Button _btnStartBatch;
        private DataGrid _dgRename;
        private DataGridTextColumn _colRenameOriginalName;
        private DataGridTextColumn _colRenameNewName;
        private DataGridTemplateColumn _colRenameStatus;
        private TextBlock _lblRenameTitle;
        private TextBlock _lblRenameMethodTitle;
        private Button _btnRenameAddMethod;
        private Button _btnRenameUndo;
        private Button _btnRenameRedo;
        private Button _btnBatchUndo;
        private Button _btnBatchRedo;
        private TextBlock _lblRenameConfigTitle;
        private TextBlock _lblRenameDragDropPrompt;

        public FrameworkElement CreateWatchRenameTabContent()
        {
            var mainGrid = new Grid
            {
                Background = Brushes.Transparent,
                Margin = new Thickness(10)
            };

            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Toolbar
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Left/Right Panels
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status bar

            // 1. Toolbar
            var toolbarPanel = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            toolbarPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbarPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbarPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            toolbarPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            toolbarPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Batch Undo
            toolbarPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Batch Redo
            toolbarPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Start Batch

            _btnAddFile = CreateReaderMiniButton("+ Thêm file", (s, e) => AddFilesToRenameList(), 110);
            _btnAddFolder = CreateReaderMiniButton("📂 Thêm thư mục", (s, e) => AddFoldersToRenameList(), 130);
            _btnDeleteFile = CreateReaderMiniButton("🗑️ Xóa", (s, e) => RemoveSelectedRenameFiles(), 90);
            
            // Apply margins to mini buttons for professional spacing
            _btnAddFile.Margin = new Thickness(0, 0, 8, 0);
            _btnAddFolder.Margin = new Thickness(0, 0, 8, 0);
            _btnDeleteFile.Margin = new Thickness(0, 0, 8, 0);

            _btnBatchUndo = CreateReaderMiniButton("Undo đổi tên", (s, e) => UndoBatchRename(), 110);
            _btnBatchUndo.Style = TryFindResource("CompactDarkBlueButton") as Style;
            _btnBatchUndo.IsEnabled = false;
            _btnBatchUndo.Margin = new Thickness(0, 0, 8, 0);

            _btnBatchRedo = CreateReaderMiniButton("Redo đổi tên", (s, e) => RedoBatchRename(), 110);
            _btnBatchRedo.Style = TryFindResource("CompactDarkBlueButton") as Style;
            _btnBatchRedo.IsEnabled = false;
            _btnBatchRedo.Margin = new Thickness(0, 0, 8, 0);

            _btnStartBatch = new Button
            {
                Content = "▶ Start batch",
                Style = TryFindResource("CompactPinkButton") as Style ?? TryFindResource("CompactCyanButton") as Style,
                FontWeight = FontWeights.Bold,
                MinWidth = 130,
                Height = 26
            };
            _btnStartBatch.Click += (s, e) => ExecuteRenameBatch();

            Grid.SetColumn(_btnAddFile, 0);
            Grid.SetColumn(_btnAddFolder, 1);
            Grid.SetColumn(_btnDeleteFile, 2);
            Grid.SetColumn(_btnBatchUndo, 4);
            Grid.SetColumn(_btnBatchRedo, 5);
            Grid.SetColumn(_btnStartBatch, 6);

            toolbarPanel.Children.Add(_btnAddFile);
            toolbarPanel.Children.Add(_btnAddFolder);
            toolbarPanel.Children.Add(_btnDeleteFile);
            toolbarPanel.Children.Add(_btnBatchUndo);
            toolbarPanel.Children.Add(_btnBatchRedo);
            toolbarPanel.Children.Add(_btnStartBatch);

            // 2. Left / Right Split Panels
            var panelsGrid = new Grid();
            panelsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) }); // Left: file list
            panelsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Right: method list & config

            // Left panel: DataGrid
            var leftBorder = new Border
            {
                Background = (Brush)TryFindResource("CyberpunkCardBrush") ?? new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 10, 0)
            };

            var dgGrid = new Grid();
            dgGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dgGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            dgGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Drag drop prompt

            _lblRenameTitle = new TextBlock
            {
                Text = "Danh sách file",
                Foreground = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow,
                FontSize = 13,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            dgGrid.Children.Add(_lblRenameTitle);

            _dgRename = new DataGrid
            {
                Style = TryFindResource("CyberpunkDataGrid") as Style,
                RowStyle = TryFindResource("CyberpunkDataGridRow") as Style,
                ColumnHeaderStyle = TryFindResource("CyberpunkDataGridColumnHeader") as Style,
                CellStyle = TryFindResource("CyberpunkDataGridCell") as Style,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                ItemsSource = _renameFiles,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                VerticalGridLinesBrush = Brushes.Transparent
            };

            // Keyboard/Mouse handlers on DataGrid
            _dgRename.MouseDoubleClick += (s, e) =>
            {
                var item = _dgRename.SelectedItem as RenameFileItem;
                if (item != null)
                {
                    try
                    {
                        Clipboard.SetText(item.OriginalName);
                    }
                    catch { }
                }
            };

            _dgRename.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Delete)
                {
                    RemoveSelectedRenameFiles();
                    e.Handled = true;
                }
                else if (e.Key == Key.Space)
                {
                    var currentItem = _dgRename.CurrentItem as RenameFileItem;
                    if (currentItem != null)
                    {
                        if (_dgRename.SelectedItems.Contains(currentItem))
                        {
                            _dgRename.SelectedItems.Remove(currentItem);
                        }
                        else
                        {
                            _dgRename.SelectedItems.Add(currentItem);
                        }
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    _dgRename.SelectAll();
                    e.Handled = true;
                }
                else if (e.Key == Key.Up && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                {
                    MoveSelectedRenameFiles(-1);
                    e.Handled = true;
                }
                else if (e.Key == Key.Down && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                {
                    MoveSelectedRenameFiles(1);
                    e.Handled = true;
                }
            };

             _colRenameOriginalName = new DataGridTextColumn
             {
                 Header = "Tên gốc",
                 Binding = new Binding(nameof(RenameFileItem.OriginalName)),
                 SortMemberPath = nameof(RenameFileItem.OriginalName),
                 Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                 IsReadOnly = true
             };
             _dgRename.Columns.Add(_colRenameOriginalName);
 
             _colRenameNewName = new DataGridTextColumn
             {
                 Header = "Tên mới",
                 Binding = new Binding(nameof(RenameFileItem.NewName)),
                 SortMemberPath = nameof(RenameFileItem.NewName),
                 Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                 IsReadOnly = true
             };
             // Dynamic text color for new name to show premium visual
             _colRenameNewName.ElementStyle = new Style(typeof(TextBlock))
             {
                 Setters = { new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xFF, 0x2A, 0x85))) }
             };
             _dgRename.Columns.Add(_colRenameNewName);
 
             _colRenameStatus = new DataGridTemplateColumn
             {
                 Header = "Trạng thái",
                 SortMemberPath = nameof(RenameFileItem.IsValid),
                 Width = new DataGridLength(70),
                 CellTemplate = CreateStatusCellTemplate()
             };
             _dgRename.Columns.Add(_colRenameStatus);

             _dgRename.Sorting += DgRename_Sorting;
             InitializeRenameContextMenu();

            Grid.SetRow(_dgRename, 1);
            dgGrid.Children.Add(_dgRename);

            // Drag drop helper text
            _lblRenameDragDropPrompt = new TextBlock
            {
                Text = "Kéo-thả file/folder vào đây để thêm",
                Foreground = (Brush)TryFindResource("CyberpunkMutedTextBrush"),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(_lblRenameDragDropPrompt, 2);
            dgGrid.Children.Add(_lblRenameDragDropPrompt);

            leftBorder.Child = dgGrid;

            // Enable Drag-Drop on Left panel
            leftBorder.AllowDrop = true;
            leftBorder.DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effects = DragDropEffects.Copy;
                else
                    e.Effects = DragDropEffects.None;
                e.Handled = true;
            };
            leftBorder.Drop += (s, e) =>
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (paths != null)
                    {
                        foreach (var p in paths) AddPathToRenameList(p);
                        RecalculateRenamePreviews();
                    }
                }
            };

            // Right panel: Method List and Configuration Panel
            var rightBorder = new Border
            {
                Background = (Brush)TryFindResource("CyberpunkCardBrush") ?? new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F)),
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10)
            };

            var rightGrid = new Grid();
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Method Title
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.2, GridUnitType.Star) }); // Method List Box
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Add Method button
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Separator line
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Config Title & Container

            _lblRenameMethodTitle = new TextBlock
            {
                Text = "Danh sách method",
                Foreground = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow,
                FontSize = 13,
                FontWeight = FontWeights.ExtraBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            rightGrid.Children.Add(_lblRenameMethodTitle);

            _lstRenameMethods = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ItemsSource = _renameMethods,
                ItemTemplate = CreateMethodItemTemplate()
            };
            _lstRenameMethods.SelectionChanged += (s, e) => ShowSelectedMethodConfig();
            Grid.SetRow(_lstRenameMethods, 1);
            rightGrid.Children.Add(_lstRenameMethods);

            // Add Method, Undo, and Redo buttons panel
            var methodActionsGrid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            methodActionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) }); // Add Method button
            methodActionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) }); // Undo button
            methodActionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) }); // Redo button

            _btnRenameAddMethod = new Button
            {
                Content = "+ Thêm method",
                Style = TryFindResource("CompactCyanButton") as Style,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold
            };
            _btnRenameAddMethod.Click += (s, e) => ShowAddMethodMenu(_btnRenameAddMethod);
            Grid.SetColumn(_btnRenameAddMethod, 0);

            _btnRenameUndo = new Button
            {
                Content = "Undo",
                Style = TryFindResource("CompactDarkBlueButton") as Style,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 0),
                IsEnabled = false
            };
            _btnRenameUndo.Click += (s, e) => UndoRenameMethodsState();
            Grid.SetColumn(_btnRenameUndo, 1);

            _btnRenameRedo = new Button
            {
                Content = "Redo",
                Style = TryFindResource("CompactDarkBlueButton") as Style,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 0, 0, 0),
                IsEnabled = false
            };
            _btnRenameRedo.Click += (s, e) => RedoRenameMethodsState();
            Grid.SetColumn(_btnRenameRedo, 2);

            methodActionsGrid.Children.Add(_btnRenameAddMethod);
            methodActionsGrid.Children.Add(_btnRenameUndo);
            methodActionsGrid.Children.Add(_btnRenameRedo);

            Grid.SetRow(methodActionsGrid, 2);
            rightGrid.Children.Add(methodActionsGrid);

            // Separator
            var separator = new Border
            {
                BorderBrush = (Brush)TryFindResource("CyberpunkBorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 4, 0, 8)
            };
            Grid.SetRow(separator, 3);
            rightGrid.Children.Add(separator);

            // Config Panel area
            var configAreaGrid = new Grid();
            configAreaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            configAreaGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _lblRenameConfigTitle = new TextBlock
            {
                Text = "Cấu hình chi tiết",
                Foreground = (Brush)TryFindResource("CyberpunkYellowBrush") ?? Brushes.Yellow,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5)
            };
            configAreaGrid.Children.Add(_lblRenameConfigTitle);

             _renameConfigContainer = new ContentControl();
             Grid.SetRow(_renameConfigContainer, 1);
             configAreaGrid.Children.Add(_renameConfigContainer);
 
             Grid.SetRow(configAreaGrid, 4);
             rightGrid.Children.Add(configAreaGrid);
 
             rightBorder.Child = rightGrid;

            Grid.SetColumn(leftBorder, 0);
            Grid.SetColumn(rightBorder, 1);
            panelsGrid.Children.Add(leftBorder);
            panelsGrid.Children.Add(rightBorder);

            Grid.SetRow(toolbarPanel, 0);
            Grid.SetRow(panelsGrid, 1);

            mainGrid.Children.Add(toolbarPanel);
            mainGrid.Children.Add(panelsGrid);

            // 3. Status Bar
            _lblRenameStatus = new TextBlock
            {
                Text = "0/0 file hợp lệ",
                Foreground = (Brush)TryFindResource("CyberpunkMutedTextBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(_lblRenameStatus, 2);
            mainGrid.Children.Add(_lblRenameStatus);

            // Apply default language dynamically at construction
            ApplyRenameTabLanguage(_isVietnameseUi);

            return mainGrid;
        }

        private void MoveSelectedRenameFiles(int direction)
        {
            if (_dgRename == null || _dgRename.SelectedItems.Count == 0) return;

            var selectedItems = _dgRename.SelectedItems.Cast<RenameFileItem>().ToList();
            if (direction < 0)
            {
                var sortedSelected = selectedItems.OrderBy(item => _renameFiles.IndexOf(item)).ToList();
                if (_renameFiles.IndexOf(sortedSelected.First()) == 0) return;

                foreach (var item in sortedSelected)
                {
                    int oldIdx = _renameFiles.IndexOf(item);
                    _renameFiles.RemoveAt(oldIdx);
                    _renameFiles.Insert(oldIdx - 1, item);
                }
            }
            else
            {
                var sortedSelected = selectedItems.OrderByDescending(item => _renameFiles.IndexOf(item)).ToList();
                if (_renameFiles.IndexOf(sortedSelected.First()) == _renameFiles.Count - 1) return;

                foreach (var item in sortedSelected)
                {
                    int oldIdx = _renameFiles.IndexOf(item);
                    _renameFiles.RemoveAt(oldIdx);
                    _renameFiles.Insert(oldIdx + 1, item);
                }
            }

            _dgRename.SelectedItems.Clear();
            foreach (var item in selectedItems)
            {
                _dgRename.SelectedItems.Add(item);
            }
            _dgRename.Focus();
            RecalculateRenamePreviews();
        }

        private void ApplyRenameTabLanguage(bool vietnamese)
        {
            if (_btnAddFile != null) _btnAddFile.Content = vietnamese ? "+ Thêm file" : "+ Add file";
            if (_btnAddFolder != null) _btnAddFolder.Content = vietnamese ? "📂 Thêm thư mục" : "📂 Add folder";
            if (_btnDeleteFile != null) _btnDeleteFile.Content = vietnamese ? "🗑️ Xóa" : "🗑️ Delete";
            if (_btnStartBatch != null) _btnStartBatch.Content = vietnamese ? "▶ Start batch" : "▶ Start batch";
            if (_btnBatchUndo != null) _btnBatchUndo.Content = vietnamese ? "Undo đổi tên" : "Undo batch";
            if (_btnBatchRedo != null) _btnBatchRedo.Content = vietnamese ? "Redo đổi tên" : "Redo batch";
            
            if (_colRenameOriginalName != null) _colRenameOriginalName.Header = vietnamese ? "Tên gốc" : "Original name";
            if (_colRenameNewName != null) _colRenameNewName.Header = vietnamese ? "Tên mới" : "New name";
            if (_colRenameStatus != null) _colRenameStatus.Header = vietnamese ? "Trạng thái" : "Status";

            if (_lblRenameTitle != null) _lblRenameTitle.Text = vietnamese ? "Danh sách file" : "File list";
            if (_lblRenameMethodTitle != null) _lblRenameMethodTitle.Text = vietnamese ? "Danh sách method" : "Method list";
            if (_btnRenameAddMethod != null) _btnRenameAddMethod.Content = vietnamese ? "+ Thêm method" : "+ Add method";
            if (_lblRenameConfigTitle != null) _lblRenameConfigTitle.Text = vietnamese ? "Cấu hình chi tiết" : "Configuration details";
            if (_lblRenameDragDropPrompt != null) _lblRenameDragDropPrompt.Text = vietnamese ? "Kéo-thả file/folder vào đây để thêm" : "Drag-drop files/folders here to add";

            // Update status text
            if (_lblRenameStatus != null)
            {
                int validCount = _renameFiles.Count(f => f.IsValid);
                _lblRenameStatus.Text = vietnamese 
                    ? $"{validCount}/{_renameFiles.Count} file hợp lệ" 
                    : $"{validCount}/{_renameFiles.Count} valid files";
            }

            // Gọi cấu hình chi tiết để render lại theo ngôn ngữ vừa đổi
            ShowSelectedMethodConfig();
        }

        private DataTemplate CreateStatusCellTemplate()
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty, new Binding(nameof(RenameFileItem.StatusIcon)));
            factory.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(RenameFileItem.StatusColor)));
            factory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            factory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            factory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

            // Add tooltip binding for warning message
            factory.SetBinding(TextBlock.ToolTipProperty, new Binding(nameof(RenameFileItem.StatusText)));

            template.VisualTree = factory;
            return template;
        }

        private DataTemplate CreateMethodItemTemplate()
        {
            var template = new DataTemplate();
            var dockFactory = new FrameworkElementFactory(typeof(DockPanel));
            dockFactory.SetValue(DockPanel.LastChildFillProperty, true);
            dockFactory.SetValue(DockPanel.MarginProperty, new Thickness(2));

            // Checkbox
            var chk = new FrameworkElementFactory(typeof(CheckBox));
            chk.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(RenameMethodBase.IsEnabled)));
            chk.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center);
            chk.SetValue(CheckBox.MarginProperty, new Thickness(0, 0, 8, 0));
            chk.SetValue(DockPanel.DockProperty, Dock.Left);
            dockFactory.AppendChild(chk);

            // Icon
            var icon = new FrameworkElementFactory(typeof(TextBlock));
            icon.SetBinding(TextBlock.TextProperty, new Binding(nameof(RenameMethodBase.IconText)));
            icon.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
            icon.SetValue(DockPanel.DockProperty, Dock.Left);
            dockFactory.AppendChild(icon);

            // Delete Button
            var btnDel = new FrameworkElementFactory(typeof(Button));
            btnDel.SetValue(Button.ContentProperty, "✕");
            btnDel.SetValue(Button.StyleProperty, TryFindResource("CompactPinkButton") as Style);
            btnDel.SetValue(Button.HeightProperty, 20.0);
            btnDel.SetValue(Button.WidthProperty, 20.0);
            btnDel.SetValue(Button.PaddingProperty, new Thickness(0));
            btnDel.SetValue(Button.MarginProperty, new Thickness(4, 0, 0, 0));
            btnDel.SetValue(DockPanel.DockProperty, Dock.Right);
            btnDel.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var method = (s as Button)?.DataContext as RenameMethodBase;
                if (method != null) DeleteRenameMethod(method);
            }));
            dockFactory.AppendChild(btnDel);

             // Down Button
             var btnDown = new FrameworkElementFactory(typeof(Button));
             btnDown.SetValue(Button.ContentProperty, "⏷");
             btnDown.SetValue(Button.StyleProperty, TryFindResource("CompactCyanButton") as Style);
             btnDown.SetValue(Button.HeightProperty, 20.0);
             btnDown.SetValue(Button.WidthProperty, 20.0);
             btnDown.SetValue(Button.PaddingProperty, new Thickness(0));
             btnDown.SetValue(Button.MarginProperty, new Thickness(4, 0, 0, 0));
             btnDown.SetValue(DockPanel.DockProperty, Dock.Right);
             btnDown.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
             {
                 var method = (s as Button)?.DataContext as RenameMethodBase;
                 if (method != null) MoveMethodDown(method);
             }));
             dockFactory.AppendChild(btnDown);
 
             // Up Button
             var btnUp = new FrameworkElementFactory(typeof(Button));
             btnUp.SetValue(Button.ContentProperty, "⏶");
             btnUp.SetValue(Button.StyleProperty, TryFindResource("CompactCyanButton") as Style);
             btnUp.SetValue(Button.HeightProperty, 20.0);
             btnUp.SetValue(Button.WidthProperty, 20.0);
             btnUp.SetValue(Button.PaddingProperty, new Thickness(0));
            btnUp.SetValue(Button.MarginProperty, new Thickness(4, 0, 0, 0));
            btnUp.SetValue(DockPanel.DockProperty, Dock.Right);
            btnUp.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                var method = (s as Button)?.DataContext as RenameMethodBase;
                if (method != null) MoveMethodUp(method);
            }));
            dockFactory.AppendChild(btnUp);

            // Name
            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding(nameof(RenameMethodBase.Name)));
            name.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            name.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            dockFactory.AppendChild(name);

            template.VisualTree = dockFactory;
            return template;
        }

        // Methods list actions
        private void MoveMethodUp(RenameMethodBase method)
        {
            int idx = _renameMethods.IndexOf(method);
            if (idx > 0)
            {
                SaveRenameMethodsStateToHistory();
                _renameMethods.RemoveAt(idx);
                _renameMethods.Insert(idx - 1, method);
                _lstRenameMethods.SelectedItem = method;
                RecalculateRenamePreviews();
            }
        }

        private void MoveMethodDown(RenameMethodBase method)
        {
            int idx = _renameMethods.IndexOf(method);
            if (idx >= 0 && idx < _renameMethods.Count - 1)
            {
                SaveRenameMethodsStateToHistory();
                _renameMethods.RemoveAt(idx);
                _renameMethods.Insert(idx + 1, method);
                _lstRenameMethods.SelectedItem = method;
                RecalculateRenamePreviews();
            }
        }

        private void DeleteRenameMethod(RenameMethodBase method)
        {
            SaveRenameMethodsStateToHistory();
            method.RequestUpdate -= RecalculateRenamePreviews;
            _renameMethods.Remove(method);
            RecalculateRenamePreviews();
            ShowSelectedMethodConfig();
        }

        private void ShowSelectedMethodConfig()
        {
            if (_renameConfigContainer == null || _lstRenameMethods == null)
            {
                return;
            }
            _renameConfigContainer.Content = null;
            var selected = _lstRenameMethods.SelectedItem as RenameMethodBase;
            if (selected != null)
            {
                var ui = selected.CreateConfigUI();
                if (ui != null)
                {
                    var scroll = new ScrollViewer
                    {
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        Content = ui
                    };
                    _renameConfigContainer.Content = scroll;
                }
            }
        }

        private void ShowAddMethodMenu(Button anchorButton)
        {
            var menu = new ContextMenu();

            var mNewName = new MenuItem { Header = "New name (Đặt lại tên)" };
            mNewName.Click += (s, e) => AddMethodToList(new NewNameMethod());
            menu.Items.Add(mNewName);

            var mReplace = new MenuItem { Header = "Replace (Tìm kiếm & Thay thế)" };
            mReplace.Click += (s, e) => AddMethodToList(new ReplaceMethod());
            menu.Items.Add(mReplace);

            var mRenumber = new MenuItem { Header = "Renumber (Đánh số thứ tự)" };
            mRenumber.Click += (s, e) => AddMethodToList(new RenumberMethod());
            menu.Items.Add(mRenumber);

            var mNewCase = new MenuItem { Header = "New case (Đổi kiểu chữ)" };
            mNewCase.Click += (s, e) => AddMethodToList(new NewCaseMethod());
            menu.Items.Add(mNewCase);

            var mRemove = new MenuItem { Header = "Remove (Xóa ký tự)" };
            mRemove.Click += (s, e) => AddMethodToList(new RemoveMethod());
            menu.Items.Add(mRemove);

            var mOptZero = new MenuItem { Header = "Optimize zero (Chuẩn hóa số không)" };
            mOptZero.Click += (s, e) => AddMethodToList(new OptimizeZeroMethod());
            menu.Items.Add(mOptZero);

            menu.PlacementTarget = anchorButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AddMethodToList(RenameMethodBase method)
        {
            SaveRenameMethodsStateToHistory();
            method.RequestUpdate += RecalculateRenamePreviews;
            _renameMethods.Add(method);
            _lstRenameMethods.SelectedItem = method;
            RecalculateRenamePreviews();
        }

        // Files list actions
        private void AddFilesToRenameList()
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Chọn các file cần đổi tên"
            };
            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    AddPathToRenameList(file);
                }
                RecalculateRenamePreviews();
            }
        }

        private void AddFoldersToRenameList()
        {
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                Multiselect = true,
                FileName = "Folder Selection.",
                Title = "Chọn thư mục chứa các thư mục/file cần đổi tên"
            };

            try
            {
                var type = dialog.GetType();
                var setOptionMethod = type.GetMethod("SetOption", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (setOptionMethod != null)
                {
                    setOptionMethod.Invoke(dialog, new object[] { 0x00000020, true });
                }
            }
            catch { }

            if (dialog.ShowDialog() == true)
            {
                var selectedPaths = new List<string>();
                if (dialog.FileNames != null)
                {
                    foreach (var fn in dialog.FileNames)
                    {
                        string path = Path.GetDirectoryName(fn);
                        if (!string.IsNullOrEmpty(path) && !selectedPaths.Contains(path))
                        {
                            selectedPaths.Add(path);
                        }
                    }
                }
                if (selectedPaths.Count > 0)
                {
                    ShowAddFolderOptionsDialog(selectedPaths);
                }
            }
        }

        private void ShowAddFolderOptionsDialog(List<string> selectedPaths)
        {
            bool vietnamese = _isVietnameseUi;
            var win = new Window
            {
                Title = vietnamese ? "Tùy chọn thêm thư mục" : "Add folder options",
                Width = 520,
                Height = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x12, 0x1F)),
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var mainGrid = new Grid { Margin = new Thickness(15) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // OK/Cancel buttons

            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

            // Left side: Add folders
            var panelLeft = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            var rbAddFolders = new RadioButton
            {
                Content = vietnamese ? "Thêm bản thân thư mục" : "Add the folders",
                IsChecked = true,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                GroupName = "AddFolderMode",
                Margin = new Thickness(0, 0, 0, 10)
            };
            panelLeft.Children.Add(rbAddFolders);

            var subPanelRight = new StackPanel { Margin = new Thickness(15, 0, 0, 0) };
            var chkFolderAddSub = new CheckBox
            {
                Content = vietnamese ? "Thêm thư mục con" : "Add subfolders",
                IsChecked = false,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            subPanelRight.Children.Add(chkFolderAddSub);

            var chkFolderAddRoot = new CheckBox
            {
                Content = vietnamese ? "Thêm thư mục gốc" : "Add root folders",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            subPanelRight.Children.Add(chkFolderAddRoot);
            panelLeft.Children.Add(subPanelRight);

            // Right side: Add files
            var panelRight = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            var rbAddFiles = new RadioButton
            {
                Content = vietnamese ? "Thêm các file trong thư mục" : "Add the files in the folders",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                GroupName = "AddFolderMode",
                Margin = new Thickness(0, 0, 0, 10)
            };
            panelRight.Children.Add(rbAddFiles);

            var subPanelLeft = new StackPanel { Margin = new Thickness(15, 0, 0, 0) };
            var chkIncludeSubfolders = new CheckBox
            {
                Content = vietnamese ? "Gồm thư mục con" : "Include subfolders",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            subPanelLeft.Children.Add(chkIncludeSubfolders);

            subPanelLeft.Children.Add(new TextBlock { Text = vietnamese ? "Bộ lọc tên file (Filename mask):" : "Filename mask:", Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 3) });
            var txtFileMask = new TextBox
            {
                Text = "*.*",
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x1B, 0x26)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x38, 0x4E)),
                CaretBrush = Brushes.White,
                Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            subPanelLeft.Children.Add(txtFileMask);

            subPanelLeft.Children.Add(new TextBlock { Text = vietnamese ? "Khớp biểu thức chính quy (Regex):" : "Regular expression match:", Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 3) });
            var txtRegex = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x1B, 0x26)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x38, 0x4E)),
                CaretBrush = Brushes.White,
                Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            subPanelLeft.Children.Add(txtRegex);

            var chkNotMatching = new CheckBox
            {
                Content = vietnamese ? "Không khớp (Phép phủ định)" : "Not matching",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            subPanelLeft.Children.Add(chkNotMatching);
            panelRight.Children.Add(subPanelLeft);

            // Set up radio events
            rbAddFiles.Checked += (s, e) =>
            {
                subPanelLeft.IsEnabled = true;
                subPanelRight.IsEnabled = false;
            };
            rbAddFolders.Checked += (s, e) =>
            {
                subPanelLeft.IsEnabled = false;
                subPanelRight.IsEnabled = true;
            };
            // Default select Folders on left
            subPanelLeft.IsEnabled = false;
            subPanelRight.IsEnabled = true;

            Grid.SetColumn(panelLeft, 0);
            Grid.SetColumn(panelRight, 1);
            contentGrid.Children.Add(panelLeft);
            contentGrid.Children.Add(panelRight);

            Grid.SetRow(contentGrid, 0);
            mainGrid.Children.Add(contentGrid);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            var btnOk = new Button
            {
                Content = "OK",
                Style = TryFindResource("CompactCyanButton") as Style,
                Width = 80,
                Height = 26,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var btnCancel = new Button
            {
                Content = vietnamese ? "Hủy" : "Cancel",
                Style = TryFindResource("CompactPinkButton") as Style,
                Width = 80,
                Height = 26,
                FontWeight = FontWeights.Bold
            };

            btnCancel.Click += (s, e) => win.Close();
            btnOk.Click += (s, e) =>
            {
                bool addFiles = rbAddFiles.IsChecked == true;
                if (addFiles)
                {
                    bool incSub = chkIncludeSubfolders.IsChecked == true;
                    string mask = txtFileMask.Text?.Trim();
                    if (string.IsNullOrEmpty(mask)) mask = "*.*";
                    string regexPattern = txtRegex.Text;
                    bool notMatch = chkNotMatching.IsChecked == true;

                    foreach (var path in selectedPaths)
                    {
                        ScanAndAddFiles(path, incSub, mask, regexPattern, notMatch);
                    }
                }
                else
                {
                    bool addSub = chkFolderAddSub.IsChecked == true;
                    bool addRoot = chkFolderAddRoot.IsChecked == true;

                    foreach (var path in selectedPaths)
                    {
                        ScanAndAddFolders(path, addSub, addRoot);
                    }
                }
                win.Close();
            };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, 1);
            mainGrid.Children.Add(btnPanel);

            win.Content = mainGrid;
            win.ShowDialog();
        }

        private void ScanAndAddFiles(string rootPath, bool includeSubfolders, string fileMask, string regexPattern, bool notMatching)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

            try
            {
                var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(rootPath, fileMask, searchOption);

                Regex regex = null;
                if (!string.IsNullOrEmpty(regexPattern))
                {
                    try
                    {
                        regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show((_isVietnameseUi ? "Lỗi cú pháp Regex: " : "Regex syntax error: ") + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (regex != null)
                    {
                        bool isMatch = regex.IsMatch(fileName);
                        if (notMatching)
                        {
                            if (isMatch) continue;
                        }
                        else
                        {
                            if (!isMatch) continue;
                        }
                    }
                    AddPathToRenameList(file);
                }

                RecalculateRenamePreviews();
            }
            catch (Exception ex)
            {
                Log($"[Rename Scan Error] {ex.Message}");
            }
        }

        private void ScanAndAddFolders(string rootPath, bool addSubfolders, bool addRootFolder)
        {
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

            try
            {
                if (addRootFolder)
                {
                    AddPathToRenameList(rootPath);
                }

                if (addSubfolders)
                {
                    var dirs = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories);
                    foreach (var dir in dirs)
                    {
                        AddPathToRenameList(dir);
                    }
                }

                RecalculateRenamePreviews();
            }
            catch (Exception ex)
            {
                Log($"[Rename Scan Error] {ex.Message}");
            }
        }

        private void AddPathToRenameList(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (_renameFiles.Any(f => string.Equals(f.OriginalPath, path, StringComparison.OrdinalIgnoreCase))) return;

            bool isDir = Directory.Exists(path);
            _renameFiles.Add(new RenameFileItem
            {
                OriginalPath = path,
                IsDirectory = isDir,
                NewName = Path.GetFileName(path)
            });
        }

        private void RemoveSelectedRenameFiles()
        {
            // Note: Since DataGrid selection might contain multiple rows, let's look at what is selected in the UI
            // We can search for the DataGrid
            var grid = FindVisualChild<DataGrid>(_readerRootGrid);
            if (grid != null && grid.SelectedItems != null)
            {
                var selected = grid.SelectedItems.Cast<RenameFileItem>().ToList();
                foreach (var item in selected)
                {
                    _renameFiles.Remove(item);
                }
                RecalculateRenamePreviews();
            }
        }

        private void RecalculateRenamePreviews()
        {
            if (_renameFiles.Count == 0)
            {
                UpdateRenameStatusText();
                return;
            }

            int totalCount = _renameFiles.Count;
            for (int i = 0; i < totalCount; i++)
            {
                var item = _renameFiles[i];
                string tempName = Path.GetFileName(item.OriginalPath);

                // Apply enabled rename methods sequentially
                foreach (var method in _renameMethods)
                {
                    if (method.IsEnabled)
                    {
                        tempName = method.Apply(tempName, i, totalCount, item.OriginalPath);
                    }
                }

                item.NewName = tempName;
                item.IsValid = true;
                item.StatusText = "";

                // Check for invalid characters
                char[] invalidChars = Path.GetInvalidFileNameChars();
                if (tempName.IndexOfAny(invalidChars) >= 0)
                {
                    item.IsValid = false;
                    item.StatusText = "Tên chứa ký tự không hợp lệ";
                }
            }

            // Check for duplicate new names (in the same target directories)
            var groups = _renameFiles.GroupBy(f => Path.Combine(Path.GetDirectoryName(f.OriginalPath) ?? "", f.NewName), StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                if (g.Count() > 1)
                {
                    foreach (var item in g)
                    {
                        item.IsValid = false;
                        item.StatusText = "Trùng tên file mới";
                    }
                }
            }

            UpdateRenameStatusText();
        }

        private void UpdateRenameStatusText()
        {
            if (_lblRenameStatus == null) return;

            int validCount = _renameFiles.Count(f => f.IsValid);
            int totalCount = _renameFiles.Count;
            int errorCount = totalCount - validCount;

            if (errorCount > 0)
            {
                _lblRenameStatus.Text = $"{validCount}/{totalCount} file hợp lệ | {errorCount} lỗi (Ví dụ: trùng tên hoặc ký tự không hợp lệ)";
                _lblRenameStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x2A, 0x85));
            }
            else
            {
                _lblRenameStatus.Text = $"{validCount}/{totalCount} file hợp lệ";
                _lblRenameStatus.Foreground = (Brush)TryFindResource("CyberpunkMutedTextBrush");
            }
        }

        private void ExecuteRenameBatch()
        {
            if (_renameFiles.Count == 0)
            {
                MessageBox.Show("Danh sách file trống!", "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int invalidCount = _renameFiles.Count(f => !f.IsValid);
            if (invalidCount > 0)
            {
                var result = MessageBox.Show($"Có {invalidCount} file không hợp lệ hoặc trùng tên. Bạn có muốn tiếp tục đổi tên những file hợp lệ không?", "Rename", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
            }

            int successCount = 0;
            int failCount = 0;

            var opList = new List<FileRenameOperation>();

            foreach (var item in _renameFiles)
            {
                if (!item.IsValid) continue;

                try
                {
                    string dir = Path.GetDirectoryName(item.OriginalPath);
                    string newPath = Path.Combine(dir ?? "", item.NewName);

                    if (string.Equals(item.OriginalPath, newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // No change
                        successCount++;
                        continue;
                    }

                    // Save details before changing
                    var op = new FileRenameOperation
                    {
                        OldPath = item.OriginalPath,
                        NewPath = newPath,
                        IsDirectory = item.IsDirectory
                    };

                    if (item.IsDirectory)
                    {
                        Directory.Move(item.OriginalPath, newPath);
                    }
                    else
                    {
                        File.Move(item.OriginalPath, newPath);
                    }

                    opList.Add(op);
                    item.OriginalPath = newPath; // Update path
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    item.IsValid = false;
                    item.StatusText = $"Lỗi: {ex.Message}";
                }
            }

            if (opList.Count > 0)
            {
                _batchRenameUndoStack.Push(opList);
                _batchRenameRedoStack.Clear(); // Clear redo on new batch execution
                UpdateBatchUndoRedoButtons();
            }

            RecalculateRenamePreviews();
            MessageBox.Show($"Hoàn tất batch rename!\nThành công: {successCount}\nThất bại: {failCount}", "Rename", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DgRename_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (e.Column == null || string.IsNullOrEmpty(e.Column.SortMemberPath)) return;
            e.Handled = true;

            ListSortDirection direction = (e.Column.SortDirection != ListSortDirection.Ascending) 
                ? ListSortDirection.Ascending 
                : ListSortDirection.Descending;

            foreach (var c in _dgRename.Columns)
            {
                c.SortDirection = null;
            }
            e.Column.SortDirection = direction;

            string propName = e.Column.SortMemberPath;
            List<RenameFileItem> list = _renameFiles.ToList();

            list.Sort((x, y) =>
            {
                string valX = GetPropValueString(x, propName);
                string valY = GetPropValueString(y, propName);
                int cmp = CompareNatural(valX, valY);
                return direction == ListSortDirection.Ascending ? cmp : -cmp;
            });

            _renameFiles.Clear();
            foreach (var item in list)
            {
                _renameFiles.Add(item);
            }
        }

        private string GetPropValueString(RenameFileItem item, string propName)
        {
            if (item == null) return string.Empty;
            if (propName == nameof(RenameFileItem.OriginalName)) return item.OriginalName ?? string.Empty;
            if (propName == nameof(RenameFileItem.NewName)) return item.NewName ?? string.Empty;
            if (propName == nameof(RenameFileItem.IsValid)) return item.IsValid.ToString();
            return string.Empty;
        }

        private int CompareNatural(string x, string y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int ix = 0;
            int iy = 0;

            while (ix < x.Length && iy < y.Length)
            {
                if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
                {
                    int startX = ix;
                    while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                    string numXStr = x.Substring(startX, ix - startX);

                    int startY = iy;
                    while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                    string numYStr = y.Substring(startY, iy - startY);

                    if (double.TryParse(numXStr, out double numX) && double.TryParse(numYStr, out double numY))
                     {
                         if (numX != numY)
                         {
                             return numX.CompareTo(numY);
                         }
                     }
                }
                else
                {
                    int cmp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                    if (cmp != 0) return cmp;
                    ix++;
                    iy++;
                }
            }

             return x.Length.CompareTo(y.Length);
         }

         private void InitializeRenameContextMenu()
         {
             if (_dgRename == null) return;

             var contextMenu = new ContextMenu();
             
             // 1. Move Selected SubMenu
             var moveSub = new MenuItem { Header = "Move Selected" };
             var mTop = new MenuItem { Header = "⏶ Move Top" };
             mTop.Click += (s, e) => MoveSelectedRenameFilesToLimit(true);
             var mUp = new MenuItem { Header = "↑ Move Up" };
             mUp.Click += (s, e) => MoveSelectedRenameFiles(-1);
             var mDown = new MenuItem { Header = "↓ Move Down" };
             mDown.Click += (s, e) => MoveSelectedRenameFiles(1);
             var mBottom = new MenuItem { Header = "⏷ Move Bottom" };
             mBottom.Click += (s, e) => MoveSelectedRenameFilesToLimit(false);
             moveSub.Items.Add(mTop);
             moveSub.Items.Add(mUp);
             moveSub.Items.Add(mDown);
             moveSub.Items.Add(mBottom);
             contextMenu.Items.Add(moveSub);

             // 2. Select SubMenu
             var selectSub = new MenuItem { Header = "Select" };
             var sAll = new MenuItem { Header = "Select all" };
             sAll.Click += (s, e) => _dgRename.SelectAll();
             var sNone = new MenuItem { Header = "Select none" };
             sNone.Click += (s, e) => _dgRename.SelectedItems.Clear();
             var sInverse = new MenuItem { Header = "Inverse selection" };
             sInverse.Click += (s, e) =>
             {
                 var currentSelected = _dgRename.SelectedItems.Cast<RenameFileItem>().ToList();
                 _dgRename.SelectedItems.Clear();
                 foreach (var item in _renameFiles)
                 {
                     if (!currentSelected.Contains(item))
                     {
                         _dgRename.SelectedItems.Add(item);
                     }
                 }
             };
             selectSub.Items.Add(sAll);
             selectSub.Items.Add(sNone);
             selectSub.Items.Add(sInverse);
             contextMenu.Items.Add(selectSub);

             // 3. Sort by SubMenu
             var sortSub = new MenuItem { Header = "Sort by" };
             var sColName = new MenuItem { Header = "Foldername" };
             sColName.Click += (s, e) => SortRenameFilesByProp(nameof(RenameFileItem.OriginalName));
             var sColNewName = new MenuItem { Header = "New Foldername" };
             sColNewName.Click += (s, e) => SortRenameFilesByProp(nameof(RenameFileItem.NewName));
             sortSub.Items.Add(sColName);
             sortSub.Items.Add(sColNewName);
             contextMenu.Items.Add(sortSub);

             contextMenu.Items.Add(new Separator());

             // 4. Add / Remove
             var mAdd = new MenuItem { Header = "Add... (Ins)" };
             mAdd.Click += (s, e) => AddFilesToRenameList();
             var mRemove = new MenuItem { Header = "✕ Remove (Del)" };
             mRemove.Click += (s, e) => RemoveSelectedRenameFiles();
             contextMenu.Items.Add(mAdd);
             contextMenu.Items.Add(mRemove);

             contextMenu.Items.Add(new Separator());

             // 5. Actions
             var mOpenDir = new MenuItem { Header = "Open containing folder" };
             mOpenDir.Click += (s, e) =>
             {
                 var item = _dgRename.SelectedItem as RenameFileItem;
                 if (item != null)
                 {
                     try
                     {
                         string dir = Path.GetDirectoryName(item.OriginalPath);
                         if (Directory.Exists(dir)) Process.Start("explorer.exe", dir);
                     }
                     catch (Exception ex) { Log("[Rename Context] " + ex.Message); }
                 }
             };
             contextMenu.Items.Add(mOpenDir);

             var mProp = new MenuItem { Header = "Properties..." };
             mProp.Click += (s, e) =>
             {
                 var item = _dgRename.SelectedItem as RenameFileItem;
                 if (item != null)
                 {
                     MessageBox.Show($"Original Path: {item.OriginalPath}\nIs Directory: {item.IsDirectory}\nValid: {item.IsValid}\nStatus: {item.StatusText}", "Properties", MessageBoxButton.OK, MessageBoxImage.Information);
                 }
             };
             contextMenu.Items.Add(mProp);

             var mOverride = new MenuItem { Header = "✏ Override new filename... (F2)" };
             mOverride.Click += (s, e) => RenameSelectedFileItemOverride();
             contextMenu.Items.Add(mOverride);

             contextMenu.Items.Add(new Separator());

             // 6. Save list sub
             var saveSub = new MenuItem { Header = "Save List" };
             var sTxt = new MenuItem { Header = "To text file..." };
             sTxt.Click += (s, e) => SaveRenameListToFile(false);
             var sCsv = new MenuItem { Header = "To CSV file..." };
             sCsv.Click += (s, e) => SaveRenameListToFile(true);
             saveSub.Items.Add(sTxt);
             saveSub.Items.Add(sCsv);
             contextMenu.Items.Add(saveSub);

             var mClear = new MenuItem { Header = "🗑 Clear List" };
             mClear.Click += (s, e) =>
             {
                 _renameFiles.Clear();
                 RecalculateRenamePreviews();
             };
             contextMenu.Items.Add(mClear);

             var mRandom = new MenuItem { Header = "🎲 Randomize sorting" };
             mRandom.Click += (s, e) =>
             {
                 var rand = new Random();
                 var list = _renameFiles.OrderBy(x => rand.Next()).ToList();
                 _renameFiles.Clear();
                 foreach (var item in list) _renameFiles.Add(item);
                 RecalculateRenamePreviews();
             };
             contextMenu.Items.Add(mRandom);

             var mRefresh = new MenuItem { Header = "🔄 Refresh (F5)" };
             mRefresh.Click += (s, e) => RecalculateRenamePreviews();
             contextMenu.Items.Add(mRefresh);

             _dgRename.ContextMenu = contextMenu;

             // Đăng ký F2 và F5 hotkey trên DataGrid
             _dgRename.PreviewKeyDown += (s, e) =>
             {
                 if (e.Key == Key.F2)
                 {
                     RenameSelectedFileItemOverride();
                     e.Handled = true;
                 }
                 else if (e.Key == Key.F5)
                 {
                     RecalculateRenamePreviews();
                     e.Handled = true;
                 }
             };
         }

         private void MoveSelectedRenameFilesToLimit(bool toTop)
         {
             if (_dgRename == null || _dgRename.SelectedItems.Count == 0) return;
             var selectedItems = _dgRename.SelectedItems.Cast<RenameFileItem>().ToList();
             var sorted = toTop 
                 ? selectedItems.OrderBy(item => _renameFiles.IndexOf(item)).ToList()
                 : selectedItems.OrderByDescending(item => _renameFiles.IndexOf(item)).ToList();

             foreach (var item in sorted)
             {
                 _renameFiles.Remove(item);
                 if (toTop)
                 {
                     _renameFiles.Insert(0, item);
                 }
                 else
                 {
                     _renameFiles.Add(item);
                 }
             }

             _dgRename.SelectedItems.Clear();
             foreach (var item in selectedItems) _dgRename.SelectedItems.Add(item);
             _dgRename.Focus();
             RecalculateRenamePreviews();
         }

         private void SortRenameFilesByProp(string propName)
         {
             List<RenameFileItem> list = _renameFiles.ToList();
             list.Sort((x, y) => CompareNatural(GetPropValueString(x, propName), GetPropValueString(y, propName)));
             _renameFiles.Clear();
             foreach (var item in list) _renameFiles.Add(item);
             RecalculateRenamePreviews();
         }

         private void RenameSelectedFileItemOverride()
         {
             var item = _dgRename.SelectedItem as RenameFileItem;
             if (item == null) return;

             var win = new Window
             {
                 Title = "Override Filename",
                 Width = 400,
                 Height = 150,
                 WindowStartupLocation = WindowStartupLocation.CenterOwner,
                 Owner = this,
                 Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x14, 0x1D)),
                 ResizeMode = ResizeMode.NoResize
             };

             var grid = new Grid { Margin = new Thickness(15) };
             grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
             grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

             var txt = new TextBox
             {
                 Text = item.NewName,
                 Height = 28,
                 Margin = new Thickness(0, 10, 0, 15),
                 Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A)),
                 BorderBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x24, 0x36)),
                 Foreground = Brushes.White,
                 CaretBrush = Brushes.White,
                 VerticalContentAlignment = VerticalAlignment.Center
             };

             var btn = new Button
             {
                 Content = "APPLY",
                 Width = 100,
                 Height = 28,
                 Style = TryFindResource("CompactCyanButton") as Style,
                 HorizontalAlignment = HorizontalAlignment.Right
             };

             btn.Click += (s, e) =>
             {
                 if (!string.IsNullOrWhiteSpace(txt.Text))
                 {
                     item.NewName = txt.Text;
                     RecalculateRenamePreviews();
                 }
                 win.Close();
             };

             Grid.SetRow(txt, 0);
             Grid.SetRow(btn, 1);
             grid.Children.Add(txt);
             grid.Children.Add(btn);
             win.Content = grid;
             win.ShowDialog();
         }

         private void SaveRenameListToFile(bool csvFormat)
         {
             var sfd = new Microsoft.Win32.SaveFileDialog
             {
                 Filter = csvFormat ? "CSV Files (*.csv)|*.csv" : "Text Files (*.txt)|*.txt",
                 Title = "Save Rename List"
             };

             if (sfd.ShowDialog() == true)
             {
                 try
                 {
                     var sb = new StringBuilder();
                     if (csvFormat)
                     {
                         sb.AppendLine("OriginalName,NewName,OriginalPath");
                         foreach (var item in _renameFiles)
                         {
                             sb.AppendLine($"\"{item.OriginalName}\",\"{item.NewName}\",\"{item.OriginalPath}\"");
                         }
                     }
                     else
                     {
                         foreach (var item in _renameFiles)
                         {
                             sb.AppendLine($"{item.OriginalName} -> {item.NewName}");
                         }
                     }
                     File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                     MessageBox.Show("Saved list successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                 }
                 catch (Exception ex)
                 {
                     MessageBox.Show("Error saving file: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 }
             }
         }
     }
 }
