using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace Folderss.Models
{
    [Serializable]
    public sealed class FavoriteLocation : INotifyPropertyChanged
    {
        private string _name;

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name == value)
                    return;
                _name = value;
                OnPropertyChanged();
            }
        }

        public string Path { get; set; }

        public bool IsFile { get; set; }

        /// <summary>true면 Path는 실제 경로가 아니라 SpecialKind를 가리키는 식별자다 (예: 디스크 사용량 보기).</summary>
        public bool IsSpecial { get; set; }

        public string SpecialKind { get; set; }

        [field: XmlIgnore]
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [Serializable]
    public sealed class FavoriteGroup : INotifyPropertyChanged
    {
        private string _name;

        public FavoriteGroup()
        {
            Favorites = new ObservableCollection<FavoriteLocation>();
        }

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name == value)
                    return;
                _name = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<FavoriteLocation> Favorites { get; set; }

        [field: XmlIgnore]
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    [Serializable]
    public sealed class FavoritesConfiguration
    {
        public FavoritesConfiguration()
        {
            Groups = new ObservableCollection<FavoriteGroup>();
            Pinned = new ObservableCollection<FavoriteLocation>();
        }

        public ObservableCollection<FavoriteGroup> Groups { get; set; }

        /// <summary>그룹 트리 위쪽에 항상 고정 표시되는 특수 바로가기 목록 (예: 디스크 사용량 보기).</summary>
        public ObservableCollection<FavoriteLocation> Pinned { get; set; }
    }
}
