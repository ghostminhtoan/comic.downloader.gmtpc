using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace get_link_manga
{
    internal sealed class ThrottledObservableCollection<T> : ObservableCollection<T>
    {
        private readonly DispatcherTimer _flushTimer;
        private bool _hasPendingNotifications;

        public ThrottledObservableCollection()
        {
            Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            _flushTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _flushTimer.Tick += (s, e) => FlushPendingNotifications();
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            QueueNotification();
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            QueueNotification();
        }

        internal void FlushPendingNotifications()
        {
            if (!_hasPendingNotifications)
            {
                return;
            }

            _hasPendingNotifications = false;
            _flushTimer.Stop();

            using (BlockReentrancy())
            {
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        private void QueueNotification()
        {
            _hasPendingNotifications = true;
            if (!_flushTimer.IsEnabled)
            {
                _flushTimer.Start();
            }
        }

        internal void RemoveRange(System.Collections.Generic.IEnumerable<T> items)
        {
            if (items == null) return;
            var set = new System.Collections.Generic.HashSet<T>(items);
            if (set.Count == 0) return;

            var remaining = new System.Collections.Generic.List<T>();
            foreach (var item in Items)
            {
                if (!set.Contains(item))
                {
                    remaining.Add(item);
                }
            }

            if (remaining.Count == Items.Count) return;

            Items.Clear();
            foreach (var item in remaining)
            {
                Items.Add(item);
            }
            FlushPendingNotifications();
        }
    }
}
