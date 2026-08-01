using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace get_link_manga
{
    public class LightNovelChapterRecord : INotifyPropertyChanged
    {
        private string _chapterTitle;
        private string _chapterLink;
        private string _plainText;
        private string _markdownText;
        private string _markdownFilePath;
        private string _volumeTitle;
        private int _volumeOrder;
        private int _sequenceIndex;
        private bool _isChecked = true;

        public string ChapterTitle
        {
            get => _chapterTitle;
            set
            {
                if (_chapterTitle != value)
                {
                    _chapterTitle = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public string ChapterLink
        {
            get => _chapterLink;
            set
            {
                if (_chapterLink != value)
                {
                    _chapterLink = value;
                    OnPropertyChanged();
                }
            }
        }

        public string PlainText
        {
            get => _plainText;
            set
            {
                if (_plainText != value)
                {
                    _plainText = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PreviewText));
                }
            }
        }

        public string MarkdownText
        {
            get => _markdownText;
            set
            {
                if (_markdownText != value)
                {
                    _markdownText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string MarkdownFilePath
        {
            get => _markdownFilePath;
            set
            {
                if (_markdownFilePath != value)
                {
                    _markdownFilePath = value;
                    OnPropertyChanged();
                }
            }
        }

        public string VolumeTitle
        {
            get => _volumeTitle;
            set
            {
                if (_volumeTitle != value)
                {
                    _volumeTitle = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public int VolumeOrder
        {
            get => _volumeOrder;
            set
            {
                if (_volumeOrder != value)
                {
                    _volumeOrder = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SequenceIndex
        {
            get => _sequenceIndex;
            set
            {
                if (_sequenceIndex != value)
                {
                    _sequenceIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayTitle
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_volumeTitle))
                {
                    return _chapterTitle ?? string.Empty;
                }

                return $"[{_volumeTitle}] {_chapterTitle}";
            }
        }

        public string PreviewText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_plainText))
                {
                    return string.Empty;
                }

                string compact = _plainText.Replace("\r", " ").Replace("\n", " ").Trim();
                return compact.Length <= 180 ? compact : compact.Substring(0, 180) + "...";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
