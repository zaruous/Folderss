using System;

namespace Folderss.Models
{
    [Serializable]
    public sealed class FavoriteLocation
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }
}
