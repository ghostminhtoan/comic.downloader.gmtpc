using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace get_link_manga
{
    public class SearchState
    {
        public string Positive { get; set; } = string.Empty;
        public string NonNegative { get; set; } = string.Empty;
        public string NonPositive { get; set; } = string.Empty;
        public string Negative { get; set; } = string.Empty;
        public bool IsExactWord { get; set; } = false;
    }

    public class AdvancedSearchViewModel : INotifyPropertyChanged
    {
        private readonly Action _applyCallback;
        private readonly DispatcherTimer _debounceTimer;
        private readonly Stack<SearchState> _undoStack = new Stack<SearchState>();
        private readonly Stack<SearchState> _redoStack = new Stack<SearchState>();
        private SearchState _pendingStateForUndo;
        private bool _isRestoringState;

        private string _positiveText = string.Empty;
        public string PositiveText
        {
            get => _positiveText;
            set
            {
                if (_positiveText != value)
                {
                    _positiveText = value;
                    OnPropertyChanged();
                    OnTextChanged();
                }
            }
        }

        private string _nonNegativeText = string.Empty;
        public string NonNegativeText
        {
            get => _nonNegativeText;
            set
            {
                if (_nonNegativeText != value)
                {
                    _nonNegativeText = value;
                    OnPropertyChanged();
                    OnTextChanged();
                }
            }
        }

        private string _nonPositiveText = string.Empty;
        public string NonPositiveText
        {
            get => _nonPositiveText;
            set
            {
                if (_nonPositiveText != value)
                {
                    _nonPositiveText = value;
                    OnPropertyChanged();
                    OnTextChanged();
                }
            }
        }

        private string _negativeText = string.Empty;
        public string NegativeText
        {
            get => _negativeText;
            set
            {
                if (_negativeText != value)
                {
                    _negativeText = value;
                    OnPropertyChanged();
                    OnTextChanged();
                }
            }
        }

        private bool _isExactWord;
        public bool IsExactWord
        {
            get => _isExactWord;
            set
            {
                if (_isExactWord != value)
                {
                    _isExactWord = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsContainsMode));
                    OnTextChanged();
                }
            }
        }

        public bool IsContainsMode
        {
            get => !_isExactWord;
            set
            {
                if (value)
                {
                    IsExactWord = false;
                }
            }
        }

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

        public ICommand ClearCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        public AdvancedSearchViewModel(Action applyCallback)
        {
            _applyCallback = applyCallback;

            ClearCommand = new RelayCommand(o => Clear());
            UndoCommand = new RelayCommand(o => Undo(), o => CanUndo());
            RedoCommand = new RelayCommand(o => Redo(), o => CanRedo());

            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            _debounceTimer.Tick += DebounceTimer_Tick;
        }

        private void OnTextChanged()
        {
            if (_isRestoringState) return;

            ApplySearch();

            if (_pendingStateForUndo == null)
            {
                _pendingStateForUndo = CreateSnapshot();
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
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
                Positive = PositiveText ?? string.Empty,
                NonNegative = NonNegativeText ?? string.Empty,
                NonPositive = NonPositiveText ?? string.Empty,
                Negative = NegativeText ?? string.Empty,
                IsExactWord = IsExactWord
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
                PositiveText = state.Positive;
                NonNegativeText = state.NonNegative;
                NonPositiveText = state.NonPositive;
                NegativeText = state.Negative;
                IsExactWord = state.IsExactWord;

                var pos = GetActivePositiveKeywords();
                var nonNeg = GetActiveNonNegativeKeywords();
                var nonPos = GetActiveNonPositiveKeywords();
                var neg = GetActiveNegativeKeywords();
                IsActive = pos.Any() || nonNeg.Any() || nonPos.Any() || neg.Any() || IsExactWord;

                _applyCallback?.Invoke();
            }
            finally
            {
                _isRestoringState = false;
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private void Clear()
        {
            PushUndoState();
            _isRestoringState = true;
            try
            {
                PositiveText = string.Empty;
                NonNegativeText = string.Empty;
                NonPositiveText = string.Empty;
                NegativeText = string.Empty;
                IsExactWord = false;
                IsActive = false;
                _applyCallback?.Invoke();
            }
            finally
            {
                _isRestoringState = false;
            }
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
            var nonNeg = GetActiveNonNegativeKeywords();
            var nonPos = GetActiveNonPositiveKeywords();
            var neg = GetActiveNegativeKeywords();
            IsActive = pos.Any() || nonNeg.Any() || nonPos.Any() || neg.Any() || IsExactWord;

            _applyCallback?.Invoke();
        }

        private List<string> ParseKeywords(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<string>();
            return text.Split(';')
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();
        }

        public List<string> GetActivePositiveKeywords()
        {
            return ParseKeywords(PositiveText);
        }

        public List<string> GetActiveNonNegativeKeywords()
        {
            return ParseKeywords(NonNegativeText);
        }

        public List<string> GetActiveNonPositiveKeywords()
        {
            return ParseKeywords(NonPositiveText);
        }

        public List<string> GetActiveNegativeKeywords()
        {
            return ParseKeywords(NegativeText);
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
