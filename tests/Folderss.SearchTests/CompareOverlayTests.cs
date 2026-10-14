using Folderss.Models;
using Folderss.Services;
using System;
using System.IO;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// 비교 결과를 폴더 패널 한쪽의 현재 폴더 항목에 입히는 규칙 검증. 결과는 손으로 만들어 파일 시스템 없이 확인한다.
    /// </summary>
    public sealed class CompareOverlayTests
    {
        private static readonly char S = Path.DirectorySeparatorChar;
        private static readonly string LeftRoot = Path.Combine(Path.GetTempPath(), "L");
        private static readonly string RightRoot = Path.Combine(Path.GetTempPath(), "R");

        private static string Rel(string forwardSlashed) => forwardSlashed.Replace('/', S);

        private static CompareEntry E(string relative, CompareStatus status, bool isDirectory = false,
            DateTime? leftModified = null, DateTime? rightModified = null)
        {
            relative = Rel(relative);
            return new CompareEntry
            {
                RelativePath = relative,
                Name = Path.GetFileName(relative),
                IsDirectory = isDirectory,
                Status = status,
                LeftExists = status != CompareStatus.RightOnly,
                RightExists = status != CompareStatus.LeftOnly,
                LeftModified = leftModified,
                RightModified = rightModified
            };
        }

        private static CompareResult Result(params CompareEntry[] entries)
        {
            var result = new CompareResult { LeftRoot = LeftRoot, RightRoot = RightRoot, Options = new DirectoryCompareOptions() };
            result.Entries.AddRange(entries);
            return result;
        }

        [Fact]
        public void RelativeFolder_InsideRootOrRoot_OutsideIsNull()
        {
            Assert.Equal(string.Empty, CompareOverlay.GetRelativeFolder(LeftRoot, LeftRoot));
            Assert.Equal(string.Empty, CompareOverlay.GetRelativeFolder(LeftRoot, LeftRoot + S));
            Assert.Equal(Rel("src/deep"), CompareOverlay.GetRelativeFolder(LeftRoot, Path.Combine(LeftRoot, "src", "deep")));
            Assert.Null(CompareOverlay.GetRelativeFolder(LeftRoot, RightRoot));
            // 이름이 기준으로 시작할 뿐인 형제 폴더는 밖이다.
            Assert.Null(CompareOverlay.GetRelativeFolder(LeftRoot, LeftRoot + "2"));
            Assert.Null(CompareOverlay.GetRelativeFolder(LeftRoot, Path.GetDirectoryName(LeftRoot)));
        }

        [Fact]
        public void EntriesFor_ReturnsOnlyDirectChildrenExistingOnThatSide()
        {
            var result = Result(
                E("src", CompareStatus.Different, isDirectory: true),
                E("src/a.txt", CompareStatus.Same),
                E("src/deep", CompareStatus.Different, isDirectory: true),
                E("src/deep/b.txt", CompareStatus.Different),
                E("src/new.txt", CompareStatus.LeftOnly),
                E("src/old.txt", CompareStatus.RightOnly),
                E("top.txt", CompareStatus.Same));

            var left = CompareOverlay.EntriesFor(result, CompareSide.Left, Path.Combine(LeftRoot, "src"));
            Assert.Equal(new[] { "a.txt", "deep", "new.txt" }, Sorted(left.Keys));

            var right = CompareOverlay.EntriesFor(result, CompareSide.Right, Path.Combine(RightRoot, "src"));
            Assert.Equal(new[] { "a.txt", "deep", "old.txt" }, Sorted(right.Keys));

            var rootLeft = CompareOverlay.EntriesFor(result, CompareSide.Left, LeftRoot);
            Assert.Equal(new[] { "src", "top.txt" }, Sorted(rootLeft.Keys));
        }

        [Fact]
        public void EntriesFor_MatchesNamesIgnoringCase()
        {
            var result = Result(E("Src", CompareStatus.Same, isDirectory: true), E("Src/Readme.MD", CompareStatus.Same));

            var map = CompareOverlay.EntriesFor(result, CompareSide.Right, Path.Combine(RightRoot, "SRC"));

            Assert.True(map.ContainsKey("README.md"));
        }

        [Fact]
        public void EntriesFor_OutsideRoot_IsNull()
        {
            var result = Result(E("a.txt", CompareStatus.Same));

            Assert.Null(CompareOverlay.EntriesFor(result, CompareSide.Left, RightRoot));
            Assert.Null(CompareOverlay.EntriesFor(null, CompareSide.Left, LeftRoot));
        }

        [Theory]
        [InlineData(CompareStatus.Same, CompareSide.Left, OverlayStatus.Same)]
        [InlineData(CompareStatus.Different, CompareSide.Right, OverlayStatus.Different)]
        [InlineData(CompareStatus.LeftOnly, CompareSide.Left, OverlayStatus.ThisSideOnly)]
        [InlineData(CompareStatus.LeftOnly, CompareSide.Right, OverlayStatus.OtherSideOnly)]
        [InlineData(CompareStatus.RightOnly, CompareSide.Right, OverlayStatus.ThisSideOnly)]
        [InlineData(CompareStatus.RightOnly, CompareSide.Left, OverlayStatus.OtherSideOnly)]
        [InlineData(CompareStatus.TypeMismatch, CompareSide.Left, OverlayStatus.TypeMismatch)]
        [InlineData(CompareStatus.Unknown, CompareSide.Left, OverlayStatus.Unknown)]
        public void SideStatus_IsRelativeToThePanel(CompareStatus status, CompareSide side, OverlayStatus expected)
        {
            Assert.Equal(expected, CompareOverlay.ToSideStatus(status, side));
        }

        [Fact]
        public void Direction_NewerOlderByModifiedTime_WithinToleranceIsNone()
        {
            var t = new DateTime(2026, 10, 1, 9, 0, 0);
            var tolerance = TimeSpan.FromSeconds(2);
            var leftNewer = E("a.txt", CompareStatus.Different, leftModified: t.AddMinutes(5), rightModified: t);

            Assert.Equal(CompareDirection.Newer, CompareOverlay.GetDirection(leftNewer, CompareSide.Left, tolerance));
            Assert.Equal(CompareDirection.Older, CompareOverlay.GetDirection(leftNewer, CompareSide.Right, tolerance));

            // 크기만 다르고 시각은 허용 오차 안 — 방향 없음.
            var sizeOnly = E("b.txt", CompareStatus.Different, leftModified: t.AddSeconds(1), rightModified: t);
            Assert.Equal(CompareDirection.None, CompareOverlay.GetDirection(sizeOnly, CompareSide.Left, tolerance));

            // 같음·한쪽만·폴더는 방향 없음.
            var same = E("c.txt", CompareStatus.Same, leftModified: t.AddMinutes(5), rightModified: t);
            Assert.Equal(CompareDirection.None, CompareOverlay.GetDirection(same, CompareSide.Left, tolerance));
            var folder = E("d", CompareStatus.Different, isDirectory: true, leftModified: t.AddMinutes(5), rightModified: t);
            Assert.Equal(CompareDirection.None, CompareOverlay.GetDirection(folder, CompareSide.Left, tolerance));
        }

        private static string[] Sorted(System.Collections.Generic.IEnumerable<string> keys)
        {
            var array = new System.Collections.Generic.List<string>(keys).ToArray();
            Array.Sort(array, StringComparer.OrdinalIgnoreCase);
            return array;
        }
    }
}
