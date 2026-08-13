using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

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

    public class SearchState
    {
        public List<string> Positive { get; set; }
        public List<string> NonPositive { get; set; }
        public List<string> Negative { get; set; }
    }

    public class AdvancedSearchViewModel : INotifyPropertyChanged
    {
        private readonly Action _applyCallback;
        private readonly DispatcherTimer _debounceTimer;
        private readonly Stack<SearchState> _undoStack = new Stack<SearchState>();
        private readonly Stack<SearchState> _redoStack = new Stack<SearchState>();
        private SearchState _pendingStateForUndo;
        private bool _isRestoringState;

        public ObservableCollection<KeywordItem> PositiveKeywords { get; } = new ObservableCollection<KeywordItem>();
        public ObservableCollection<KeywordItem> NonPositiveKeywords { get; } = new ObservableCollection<KeywordItem>();
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
        public ICommand AddNonPositiveKeywordCommand { get; }
        public ICommand RemoveNonPositiveKeywordCommand { get; }
        public ICommand AddNegativeKeywordCommand { get; }
        public ICommand RemoveNegativeKeywordCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        public AdvancedSearchViewModel(Action applyCallback)
        {
            _applyCallback = applyCallback;

            AddPositiveKeywordCommand = new RelayCommand(o => AddPositiveKeyword());
            RemovePositiveKeywordCommand = new RelayCommand(item => RemovePositiveKeyword(item as KeywordItem));
            AddNonPositiveKeywordCommand = new RelayCommand(o => AddNonPositiveKeyword());
            RemoveNonPositiveKeywordCommand = new RelayCommand(item => RemoveNonPositiveKeyword(item as KeywordItem));
            AddNegativeKeywordCommand = new RelayCommand(o => AddNegativeKeyword());
            RemoveNegativeKeywordCommand = new RelayCommand(item => RemoveNegativeKeyword(item as KeywordItem));
            ClearCommand = new RelayCommand(o => Clear());
            UndoCommand = new RelayCommand(o => Undo(), o => CanUndo());
            RedoCommand = new RelayCommand(o => Redo(), o => CanRedo());

            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _debounceTimer.Tick += DebounceTimer_Tick;

            PositiveKeywords.CollectionChanged += Keywords_CollectionChanged;
            NonPositiveKeywords.CollectionChanged += Keywords_CollectionChanged;
            NegativeKeywords.CollectionChanged += Keywords_CollectionChanged;

            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            _isRestoringState = true;
            try
            {
                PositiveKeywords.Clear();
                NonPositiveKeywords.Clear();
                NegativeKeywords.Clear();

                PositiveKeywords.Add(new KeywordItem());
                PositiveKeywords.Add(new KeywordItem());

                NonPositiveKeywords.Add(new KeywordItem());
                NonPositiveKeywords.Add(new KeywordItem());

                NegativeKeywords.Add(new KeywordItem());
                NegativeKeywords.Add(new KeywordItem());
            }
            finally
            {
                _isRestoringState = false;
            }
        }

        private void Keywords_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_isRestoringState) return;

            if (e.NewItems != null)
            {
                foreach (KeywordItem item in e.NewItems)
                {
                    item.PropertyChanged += Keyword_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (KeywordItem item in e.OldItems)
                {
                    item.PropertyChanged -= Keyword_PropertyChanged;
                }
            }

            PushUndoState();
            ApplySearch();
        }

        private void Keyword_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (_isRestoringState) return;

            if (e.PropertyName == nameof(KeywordItem.Value))
            {
                ApplySearch();

                if (_pendingStateForUndo == null)
                {
                    _pendingStateForUndo = CreateSnapshot();
                }

                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            if (_pendingStateForUndo != null && !_isRestoringState)
            {
                _undoStack.Push(_pendingStateForUndo);
                _redoStack.Clear();
                _pendingStateForUndo = null;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private SearchState CreateSnapshot()
        {
            return new SearchState
            {
                Positive = PositiveKeywords.Select(k => k.Value ?? string.Empty).ToList(),
                NonPositive = NonPositiveKeywords.Select(k => k.Value ?? string.Empty).ToList(),
                Negative = NegativeKeywords.Select(k => k.Value ?? string.Empty).ToList()
            };
        }

        private void PushUndoState()
        {
            _undoStack.Push(CreateSnapshot());
            _redoStack.Clear();
            _pendingStateForUndo = null;
            CommandManager.InvalidateRequerySuggested();
        }

        private void RestoreSnapshot(SearchState state)
        {
            _isRestoringState = true;
            try
            {
                foreach (var k in PositiveKeywords) k.PropertyChanged -= Keyword_PropertyChanged;
                foreach (var k in NonPositiveKeywords) k.PropertyChanged -= Keyword_PropertyChanged;
                foreach (var k in NegativeKeywords) k.PropertyChanged -= Keyword_PropertyChanged;

                PositiveKeywords.Clear();
                foreach (var val in state.Positive)
                {
                    var item = new KeywordItem { Value = val };
                    item.PropertyChanged += Keyword_PropertyChanged;
                    PositiveKeywords.Add(item);
                }

                NonPositiveKeywords.Clear();
                foreach (var val in state.NonPositive)
                {
                    var item = new KeywordItem { Value = val };
                    item.PropertyChanged += Keyword_PropertyChanged;
                    NonPositiveKeywords.Add(item);
                }

                NegativeKeywords.Clear();
                foreach (var val in state.Negative)
                {
                    var item = new KeywordItem { Value = val };
                    item.PropertyChanged += Keyword_PropertyChanged;
                    NegativeKeywords.Add(item);
                }

                IsActive = GetActivePositiveKeywords().Any() || GetActiveNonPositiveKeywords().Any() || GetActiveNegativeKeywords().Any();
                _applyCallback?.Invoke();
            }
            finally
            {
                _isRestoringState = false;
            }
            CommandManager.InvalidateRequerySuggested();
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

        private void AddNonPositiveKeyword()
        {
            NonPositiveKeywords.Add(new KeywordItem());
        }

        private void RemoveNonPositiveKeyword(KeywordItem item)
        {
            if (item != null && NonPositiveKeywords.Contains(item))
            {
                NonPositiveKeywords.Remove(item);
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

        private void Clear()
        {
            PushUndoState();
            InitializeDefaults();
            IsActive = false;
            _applyCallback?.Invoke();
        }

        private bool CanUndo()
        {
            return _undoStack.Count > 0;
        }

        private void Undo()
        {
            if (CanUndo())
            {
                var currentState = CreateSnapshot();
                _redoStack.Push(currentState);
                var previousState = _undoStack.Pop();
                RestoreSnapshot(previousState);
            }
        }

        private bool CanRedo()
        {
            return _redoStack.Count > 0;
        }

        private void Redo()
        {
            if (CanRedo())
            {
                var currentState = CreateSnapshot();
                _undoStack.Push(currentState);
                var nextState = _redoStack.Pop();
                RestoreSnapshot(nextState);
            }
        }

        private void ApplySearch()
        {
            var pos = GetActivePositiveKeywords();
            var nonPos = GetActiveNonPositiveKeywords();
            var neg = GetActiveNegativeKeywords();
            IsActive = pos.Any() || nonPos.Any() || neg.Any();

            _applyCallback?.Invoke();
        }

        public List<string> GetActivePositiveKeywords()
        {
            return PositiveKeywords
                .Select(k => k.Value?.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();
        }

        public List<string> GetActiveNonPositiveKeywords()
        {
            return NonPositiveKeywords
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
