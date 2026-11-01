using Folderss.Models;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>블록 편집 ↔ 미리보기 전환(#81): 열 뷰어 고르기, 위치 대응(블록 ↔ 제목·줄 번호), 파일별 마지막 보기·기본 보기 저장.</summary>
    public sealed class MarkdownViewSwitchTests
    {
        private const string Blocks = ViewerConfigService.BuiltInBlocksKey;
        private const string Markdown = ViewerConfigService.BuiltInMarkdownKey;
        private const string Monaco = ViewerConfigService.BuiltInMonacoKey;

        private static EditorBlock B(BlockKind kind, string text, int indent = 0)
        {
            return new EditorBlock(kind, text, indent);
        }

        // ── 열 뷰어 고르기 ─────────────────────────────────────────────────

        [Fact]
        public void Choose_DefaultFollowMapping_KeepsMapping()
        {
            Assert.Equal(Markdown, MarkdownViewSwitch.ChooseViewerKey(".md", Markdown, null, MarkdownViewSwitch.DefaultFollowMapping));
            Assert.Equal(Blocks, MarkdownViewSwitch.ChooseViewerKey(".md", Blocks, null, MarkdownViewSwitch.DefaultFollowMapping));
        }

        [Fact]
        public void Choose_RememberedViewWinsOverDefault()
        {
            Assert.Equal(Blocks, MarkdownViewSwitch.ChooseViewerKey(".md", Markdown, "blocks", "preview"));
            Assert.Equal(Markdown, MarkdownViewSwitch.ChooseViewerKey(".markdown", Blocks, "preview", "blocks"));
        }

        [Fact]
        public void Choose_DefaultViewUsedWhenNothingRemembered()
        {
            Assert.Equal(Blocks, MarkdownViewSwitch.ChooseViewerKey(".MD", Markdown, null, "blocks"));
            Assert.Equal(Markdown, MarkdownViewSwitch.ChooseViewerKey(".md", Blocks, null, "preview"));
        }

        [Fact]
        public void Choose_OtherMappingsOrExtensionsAreUntouched()
        {
            // .md를 Monaco로 매핑했으면 기억·기본 보기를 쓰지 않는다.
            Assert.Equal(Monaco, MarkdownViewSwitch.ChooseViewerKey(".md", Monaco, "blocks", "blocks"));
            Assert.Equal(Markdown, MarkdownViewSwitch.ChooseViewerKey(".txt", Markdown, "blocks", "blocks"));
            Assert.Null(MarkdownViewSwitch.ChooseViewerKey(".md", null, "blocks", "blocks"));
        }

        // ── 블록 → 제목 앵커 ───────────────────────────────────────────────

        [Fact]
        public void HeadingIdAbove_NearestHeadingWithDuplicateSuffix()
        {
            var doc = new List<EditorBlock>
            {
                B(BlockKind.Paragraph, "intro"),          // 0
                B(BlockKind.Heading1, "설치 방법"),       // 1
                B(BlockKind.Paragraph, "a"),              // 2
                B(BlockKind.Heading2, "설치 방법"),       // 3 → 설치-방법-1
                B(BlockKind.Paragraph, "b"),              // 4
                B(BlockKind.Heading3, "**굵은** 제목!"), // 5 → 굵은-제목
            };
            Assert.Null(MarkdownViewSwitch.HeadingIdAbove(doc, 0));
            Assert.Equal("설치-방법", MarkdownViewSwitch.HeadingIdAbove(doc, 1));
            Assert.Equal("설치-방법", MarkdownViewSwitch.HeadingIdAbove(doc, 2));
            Assert.Equal("설치-방법-1", MarkdownViewSwitch.HeadingIdAbove(doc, 4));
            Assert.Equal("굵은-제목", MarkdownViewSwitch.HeadingIdAbove(doc, 5));
        }

        [Fact]
        public void HeadingId_RoundTripsThroughFindHeading()
        {
            var doc = new List<EditorBlock>
            {
                B(BlockKind.Heading1, "A"), B(BlockKind.Paragraph, "x"),
                B(BlockKind.Heading2, "A"), B(BlockKind.Paragraph, "y"),
                B(BlockKind.Heading2, "B"),
            };
            for (var i = 0; i < doc.Count; i++)
            {
                var id = MarkdownViewSwitch.HeadingIdAbove(doc, i);
                var heading = BlockFind.FindHeading(doc, id);
                Assert.True(heading <= i && doc[heading].GetHeadingLevel() > 0, "block " + i + " → " + id + " → " + heading);
                Assert.Equal(id, MarkdownViewSwitch.HeadingIdAbove(doc, heading));
            }
        }

        // ── 블록 ↔ 줄 번호 ─────────────────────────────────────────────────

        private const string Normalized = "# A\n\npara one\n\n## B\n\n- x\n- y\n\npara two\n";

        [Fact]
        public void LineOfBlock_MatchesSerializedSource()
        {
            var doc = BlockMarkdown.Parse(Normalized);
            Assert.Equal(Normalized, BlockMarkdown.Serialize(doc));   // 전제: 정리된 형식이면 원문과 같다
            Assert.Equal(new[] { 1, 3, 5, 7, 8, 10 }, Enumerable.Range(0, doc.Count).Select(i => MarkdownViewSwitch.LineOfBlock(doc, i)).ToArray());
        }

        [Fact]
        public void BlockAtLine_FindsBlockContainingLine()
        {
            var doc = BlockMarkdown.Parse(Normalized);
            Assert.Equal(0, MarkdownViewSwitch.BlockAtLine(doc, 1));
            Assert.Equal(0, MarkdownViewSwitch.BlockAtLine(doc, 2));    // 제목 아래 빈 줄은 앞 블록
            Assert.Equal(2, MarkdownViewSwitch.BlockAtLine(doc, 5));
            Assert.Equal(4, MarkdownViewSwitch.BlockAtLine(doc, 9));
            Assert.Equal(5, MarkdownViewSwitch.BlockAtLine(doc, 999));
            Assert.Equal(-1, MarkdownViewSwitch.BlockAtLine(new List<EditorBlock>(), 3));
        }

        [Fact]
        public void LineOfBlock_RoundTripsOnLargerDocument()
        {
            var lines = new List<string>();
            for (var i = 0; i < 60; i++)
            {
                lines.Add("## 절 " + i);
                lines.Add(string.Empty);
                lines.Add("본문 " + i + "\n둘째 줄");
                lines.Add(string.Empty);
                lines.Add("```cs\nvar a = " + i + ";\nvar b = 2;\n```");
                lines.Add(string.Empty);
            }
            var doc = BlockMarkdown.Parse(string.Join("\n", lines));
            var previous = 0;
            for (var i = 0; i < doc.Count; i++)
            {
                var line = MarkdownViewSwitch.LineOfBlock(doc, i);
                Assert.True(line > previous, "lines must increase at block " + i);
                Assert.Equal(i, MarkdownViewSwitch.BlockAtLine(doc, line));
                previous = line;
            }
        }

        [Fact]
        public void ToBlock_PrefersHeadingThenLine()
        {
            var doc = BlockMarkdown.Parse(Normalized);
            Assert.Equal(2, MarkdownViewSwitch.ToBlock(doc, new MarkdownViewPosition { HeadingId = "b", Line = 1 }));
            // 원문 편집 화면에서 잡은 위치는 줄 번호가 먼저
            Assert.Equal(4, MarkdownViewSwitch.ToBlock(doc, new MarkdownViewPosition { HeadingId = "a", Line = 8, PreferLine = true }));
            // 없는 제목이면 줄 번호
            Assert.Equal(3, MarkdownViewSwitch.ToBlock(doc, new MarkdownViewPosition { HeadingId = "없음", Line = 7 }));
            Assert.Equal(-1, MarkdownViewSwitch.ToBlock(doc, new MarkdownViewPosition()));
            Assert.Equal(-1, MarkdownViewSwitch.ToBlock(doc, null));
        }

        [Fact]
        public void FromBlock_GivesHeadingAndLine()
        {
            var doc = BlockMarkdown.Parse(Normalized);
            var position = MarkdownViewSwitch.FromBlock(doc, 3);
            Assert.Equal("b", position.HeadingId);
            Assert.Equal(7, position.Line);
            Assert.False(position.PreferLine);
            Assert.Equal(3, MarkdownViewSwitch.ToBlock(doc, new MarkdownViewPosition { Line = position.Line, PreferLine = true }));
            Assert.Null(MarkdownViewSwitch.FromBlock(doc, -1));
        }

        // ── 파일별 마지막 보기·기본 보기 저장 ───────────────────────────────

        private static string TempPath()
        {
            return Path.Combine(Path.GetTempPath(), "folderss-mdview-" + Guid.NewGuid().ToString("N"), "markdown-view.json");
        }

        [Fact]
        public void Settings_DefaultsWhenFileMissing()
        {
            var settings = new MarkdownViewSettings(TempPath());
            Assert.Equal(MarkdownViewSwitch.DefaultFollowMapping, settings.DefaultView);
            Assert.Null(settings.GetRemembered(@"C:\docs\a.md"));
        }

        [Fact]
        public void Settings_RememberAndDefault_RoundTripThroughFile()
        {
            var path = TempPath();
            try
            {
                var settings = new MarkdownViewSettings(path);
                settings.RememberAndSave(@"C:\docs\a.md", "blocks");
                settings.RememberAndSave(@"C:\docs\b.md", "preview");
                settings.RememberAndSave(@"c:\DOCS\A.md", "preview");   // 같은 파일(대소문자 무시)은 덮어쓴다
                settings.SaveDefaultView("blocks");

                var reloaded = new MarkdownViewSettings(path);
                Assert.Equal("blocks", reloaded.DefaultView);
                Assert.Equal("preview", reloaded.GetRemembered(@"C:\docs\a.md"));
                Assert.Equal("preview", reloaded.GetRemembered(@"C:\docs\b.md"));
                Assert.Equal(2, reloaded.RememberedCount);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(path), true);
            }
        }

        [Fact]
        public void Settings_KeepsMostRecent200()
        {
            var settings = new MarkdownViewSettings(TempPath());
            for (var i = 0; i < MarkdownViewSettings.MaxRememberedFiles + 5; i++)
                settings.Remember(@"C:\docs\f" + i + ".md", "blocks");
            settings.Remember(@"C:\docs\f5.md", "preview");   // 다시 쓰면 가장 최근으로 옮겨져 남는다
            settings.Remember(@"C:\docs\new.md", "blocks");
            Assert.Equal(MarkdownViewSettings.MaxRememberedFiles, settings.RememberedCount);
            Assert.Null(settings.GetRemembered(@"C:\docs\f0.md"));
            Assert.Null(settings.GetRemembered(@"C:\docs\f6.md"));
            Assert.Equal("preview", settings.GetRemembered(@"C:\docs\f5.md"));
            Assert.Equal("blocks", settings.GetRemembered(@"C:\docs\new.md"));
        }

        [Fact]
        public void Settings_IgnoresBrokenOrUnknownValues()
        {
            var settings = new MarkdownViewSettings(TempPath());
            settings.Parse("{ not json");
            Assert.Equal(MarkdownViewSwitch.DefaultFollowMapping, settings.DefaultView);
            settings.Parse("{\"version\":1,\"defaultView\":\"monaco\",\"files\":[{\"path\":\"C:\\\\a.md\",\"view\":\"edit\"},{\"path\":\"C:\\\\b.md\",\"view\":\"Blocks\"},{\"view\":\"preview\"}]}");
            Assert.Equal(MarkdownViewSwitch.DefaultFollowMapping, settings.DefaultView);
            Assert.Null(settings.GetRemembered(@"C:\a.md"));
            Assert.Equal("blocks", settings.GetRemembered(@"C:\b.md"));
            Assert.Equal(1, settings.RememberedCount);
            Assert.False(settings.Remember(@"C:\c.md", "split"));
            Assert.False(settings.Remember(@"C:\b.md", "blocks"));   // 이미 가장 최근·같은 값이면 바뀐 것 없음
        }
    }
}
