using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace get_link_manga
{
    public class KeywordItem : INotifyPropertyChanged
    {
        private string _value = string.Empty;
        public string Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class AdvancedSearchViewModel : INotifyPropertyChanged
    {
        private readonly Action _applyCallback;

        public ObservableCollection<KeywordItem> PositiveKeywords { get; } = new ObservableCollection<KeywordItem>();
        public ObservableCollection<KeywordItem> NegativeKeywords { get; } = new ObservableCollection<KeywordItem>();

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand AddPositiveKeywordCommand { get; }
        public ICommand RemovePositiveKeywordCommand { get; }
        public ICommand AddNegativeKeywordCommand { get; }
        public ICommand RemoveNegativeKeywordCommand { get; }
        public ICommand ApplySearchCommand { get; }
        public ICommand ResetCommand { get; }

        public AdvancedSearchViewModel(Action applyCallback)
        {
            _applyCallback = applyCallback;

            AddPositiveKeywordCommand = new RelayCommand(o => AddPositiveKeyword());
            RemovePositiveKeywordCommand = new RelayCommand(item => RemovePositiveKeyword(item as KeywordItem));
            AddNegativeKeywordCommand = new RelayCommand(o => AddNegativeKeyword());
            RemoveNegativeKeywordCommand = new RelayCommand(item => RemoveNegativeKeyword(item as KeywordItem));
            ApplySearchCommand = new RelayCommand(o => ApplySearch());
            ResetCommand = new RelayCommand(o => Reset());

            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            PositiveKeywords.Clear();
            NegativeKeywords.Clear();

            // Mặc định luôn tự tạo sẵn 2 phần tử rỗng
            PositiveKeywords.Add(new KeywordItem());
            PositiveKeywords.Add(new KeywordItem());

            NegativeKeywords.Add(new KeywordItem());
            NegativeKeywords.Add(new KeywordItem());
        }

        private void AddPositiveKeyword()
        {
            PositiveKeywords.Add(new KeywordItem());
        }

        private void RemovePositiveKeyword(KeywordItem item)
        {
            if (item != null && PositiveKeywords.Contains(item))
            {
                PositiveKeywords.Remove(item);
            }
        }

        private void AddNegativeKeyword()
        {
            NegativeKeywords.Add(new KeywordItem());
        }

        private void RemoveNegativeKeyword(KeywordItem item)
        {
            if (item != null && NegativeKeywords.Contains(item))
            {
                NegativeKeywords.Remove(item);
            }
        }

        private void ApplySearch()
        {
            // Kiểm tra xem thực tế có từ khóa nào được áp dụng không
            var pos = GetActivePositiveKeywords();
            var neg = GetActiveNegativeKeywords();
            IsActive = pos.Any() || neg.Any();

            _applyCallback?.Invoke();
        }

        private void Reset()
        {
            InitializeDefaults();
            IsActive = false;
            _applyCallback?.Invoke();
        }

        public List<string> GetActivePositiveKeywords()
        {
            return PositiveKeywords
                .Select(k => k.Value?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();
        }

        public List<string> GetActiveNegativeKeywords()
        {
            return NegativeKeywords
                .Select(k => k.Value?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
