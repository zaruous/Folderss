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
        }

        public ObservableCollection<FavoriteGroup> Groups { get; set; }
    }
}
