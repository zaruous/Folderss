namespace Folderss.Models
{
    public sealed class SearchResult
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string FolderPath { get; set; }
        public int LineNumber { get; set; }
        public string LineText { get; set; }
    }
}
