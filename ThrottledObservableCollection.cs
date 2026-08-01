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
                Interval = TimeSpan.FromMilliseconds(90)
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
    }
}
