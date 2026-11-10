using Folderss.Services;
using System;
using System.IO;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 메뉴 "전환 > 이미지": 블록 글이 이미지 주소·경로일 때만 이미지 블록으로 바꾼다.</summary>
    public sealed class BlockImageTurnIntoTests
    {
        private static bool IsImage(string path)
        {
            var ext = Path.GetExtension(path);
            return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("https://example.com/a.png", "https://example.com/a.png")]
        [InlineData("  https://example.com/img?id=1  ", "https://example.com/img?id=1")]   // 확장자 없는 이미지 URL도
        [InlineData("<https://example.com/a.png>", "https://example.com/a.png")]           // 자동 링크 표기
        [InlineData("images/a.png", "images/a.png")]
        [InlineData(@"C:\My Pics\a.PNG", @"C:\My Pics\a.PNG")]                              // 공백 있는 로컬 경로
        public void Accepts(string text, string expected)
        {
            Assert.Equal(expected, BlockMarkdown.ImageSourceOf(text, IsImage));
        }

        [Theory]
        [InlineData("")]
        [InlineData("그냥 문장입니다")]
        [InlineData("notes.txt")]
        [InlineData("https://example.com/a.png 설명")]   // 주소 뒤에 글이 붙음
        [InlineData("https://a.com/x.png\nhttps://b.com/y.png")]
        [InlineData("ftp://example.com/a")]
        public void Rejects(string text)
        {
            Assert.Null(BlockMarkdown.ImageSourceOf(text, IsImage));
        }
    }
}
