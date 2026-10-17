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

        [Fact]
        public void Describe_MarksAndTexts()
        {
            var t = new DateTime(2026, 10, 1, 9, 0, 0);
            var tolerance = TimeSpan.FromSeconds(2);
            var leftNewer = E("a.txt", CompareStatus.Different, leftModified: t.AddMinutes(5), rightModified: t);

            Assert.Equal("Different", CompareOverlay.Describe(leftNewer, CompareSide.Left, false, tolerance).Mark);
            Assert.Equal("Newer", CompareOverlay.Describe(leftNewer, CompareSide.Left, true, tolerance).Mark);
            Assert.Equal("Older", CompareOverlay.Describe(leftNewer, CompareSide.Right, true, tolerance).Mark);
            Assert.Equal("≠ 하위 차이", CompareOverlay.Describe(E("d", CompareStatus.Different, isDirectory: true), CompareSide.Left, true, tolerance).Text);
            Assert.Equal("Same", CompareOverlay.Describe(E("s", CompareStatus.Same), CompareSide.Left, false, tolerance).Mark);

            var leftOnly = CompareOverlay.Describe(E("n", CompareStatus.LeftOnly), CompareSide.Left, false, tolerance);
            Assert.Equal("ThisOnly", leftOnly.Mark);
            Assert.Equal("◀ 왼쪽만", leftOnly.Text);
            Assert.Equal("▶ 오른쪽만", CompareOverlay.Describe(E("o", CompareStatus.RightOnly), CompareSide.Right, false, tolerance).Text);
            Assert.Equal("TypeMismatch", CompareOverlay.Describe(E("x", CompareStatus.TypeMismatch), CompareSide.Left, false, tolerance).Mark);
            Assert.Equal("Unknown", CompareOverlay.Describe(E("u", CompareStatus.Unknown), CompareSide.Left, false, tolerance).Mark);

            // 비교 결과에 없는 항목은 색 없이 표시만(차이만 보기로 숨기지 않는다).
            var missing = CompareOverlay.Describe(null, CompareSide.Left, false, tolerance);
            Assert.Null(missing.Mark);
            Assert.Equal("—", missing.Text);
        }

        [Fact]
        public void CounterpartPath_UsesRelativePathFromCompareRoots()
        {
            var result = Result(E("a.txt", CompareStatus.Same));

            Assert.Equal(Path.Combine(RightRoot, "src", "New", "README.md"),
                CompareOverlay.CounterpartPath(result, CompareSide.Left, Path.Combine(LeftRoot, "src", "New", "README.md")));
            Assert.Equal(Path.Combine(LeftRoot, "a.txt"),
                CompareOverlay.CounterpartPath(result, CompareSide.Right, Path.Combine(RightRoot, "a.txt")));
            // 기준 밖이거나 비교 전이면 짝이 없다(반대쪽 패널이 우연히 보고 있는 폴더로 짐작하지 않는다).
            Assert.Null(CompareOverlay.CounterpartPath(result, CompareSide.Left, Path.Combine(RightRoot, "a.txt")));
            Assert.Null(CompareOverlay.CounterpartPath(null, CompareSide.Left, Path.Combine(LeftRoot, "a.txt")));
        }

        private static string[] Sorted(System.Collections.Generic.IEnumerable<string> keys)
        {
            var array = new System.Collections.Generic.List<string>(keys).ToArray();
            Array.Sort(array, StringComparer.OrdinalIgnoreCase);
            return array;
        }

        [Fact]
        public void CrossPanelFilePair_OneFileEachSide_LeftThenRight()
        {
            var left = new[] { (@"L\a.txt", false) };
            var right = new[] { (@"R\b.txt", false) };
            Assert.Equal(new[] { @"L\a.txt", @"R\b.txt" }, CompareOverlay.CrossPanelFilePair(left, right));
        }

        [Fact]
        public void CrossPanelFilePair_NullUnlessExactlyOneFileEachSide()
        {
            var file = new[] { (@"L\a.txt", false) };
            Assert.Null(CompareOverlay.CrossPanelFilePair(file, new (string, bool)[0]));                               // 한쪽 선택 없음
            Assert.Null(CompareOverlay.CrossPanelFilePair(file, new[] { (@"R\a.txt", false), (@"R\b.txt", false) })); // 두 개
            Assert.Null(CompareOverlay.CrossPanelFilePair(file, new[] { (@"R\src", true) }));                         // 폴더
            Assert.Null(CompareOverlay.CrossPanelFilePair(null, file));
        }
    }
}
