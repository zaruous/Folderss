using System;

namespace Folderss.Models
{
    /// <summary>검색 대상. 둘 다 고르면 파일명과 내용을 함께 검색한다.</summary>
    [Flags]
    public enum SearchTarget
    {
        Content = 1,
        FileName = 2
    }
}
