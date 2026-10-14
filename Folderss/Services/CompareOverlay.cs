using System;
using System.Collections.Generic;
using System.IO;
using Folderss.Models;

namespace Folderss.Services
{
    public enum CompareSide { Left, Right }

    /// <summary>폴더 패널 한쪽에서 본 항목 상태. 왼쪽만/오른쪽만을 그 패널 기준 이쪽만/저쪽만으로 바꾼 값.</summary>
    public enum OverlayStatus { Same, Different, ThisSideOnly, OtherSideOnly, TypeMismatch, Unknown }

    /// <summary>방향 색(최신/오래됨)용. 수정 시각으로만 정하므로 시각만 바뀐 파일은 잘못 "최신"이 될 수 있다.</summary>
    public enum CompareDirection { None, Newer, Older }

    /// <summary>
    /// 비교 결과(<see cref="CompareResult"/>)를 폴더 패널 한쪽의 현재 폴더 목록에 입히는 규칙. 순수 계산이라 UI 없이 테스트한다.
    /// 패널은 실제 폴더 내용만 보이므로 그 쪽에 있는 직속 항목만 돌려준다(반대쪽에만 있는 항목은 반대쪽 패널에서 보인다).
    /// </summary>
    public static class CompareOverlay
    {
        /// <summary>비교 기준 아래면 기준 기준 상대 경로(기준 자체는 ""), 밖이면 null. 대소문자 무시(Windows 경로 규칙).</summary>
        public static string GetRelativeFolder(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
                return null;

            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            var prefix = normalizedRoot + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? normalizedPath.Substring(prefix.Length)
                : null;
        }

        /// <summary>
        /// <paramref name="currentPath"/>(그 쪽 패널의 현재 폴더)의 직속 항목 중 그 쪽에 있는 것의 판정. 키는 이름(대소문자 무시).
        /// 기준 밖이거나 결과가 없으면 null — 패널은 오버레이 없이 보인다.
        /// </summary>
        public static Dictionary<string, CompareEntry> EntriesFor(CompareResult result, CompareSide side, string currentPath)
        {
            if (result == null)
                return null;

            var relative = GetRelativeFolder(side == CompareSide.Left ? result.LeftRoot : result.RightRoot, currentPath);
            if (relative == null)
                return null;

            var map = new Dictionary<string, CompareEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in result.Entries)
            {
                if (!(side == CompareSide.Left ? entry.LeftExists : entry.RightExists))
                    continue;
                if (string.Equals(ParentOf(entry.RelativePath), relative, StringComparison.OrdinalIgnoreCase))
                    map[entry.Name] = entry;
            }
            return map;
        }

        public static OverlayStatus ToSideStatus(CompareStatus status, CompareSide side)
        {
            switch (status)
            {
                case CompareStatus.Same: return OverlayStatus.Same;
                case CompareStatus.Different: return OverlayStatus.Different;
                case CompareStatus.LeftOnly: return side == CompareSide.Left ? OverlayStatus.ThisSideOnly : OverlayStatus.OtherSideOnly;
                case CompareStatus.RightOnly: return side == CompareSide.Right ? OverlayStatus.ThisSideOnly : OverlayStatus.OtherSideOnly;
                case CompareStatus.TypeMismatch: return OverlayStatus.TypeMismatch;
                default: return OverlayStatus.Unknown;
            }
        }

        /// <summary>다른 파일 쌍에서 이 쪽이 더 늦게 수정됐으면 Newer, 이르면 Older. 허용 오차 안이거나 파일 쌍이 아니면 None.</summary>
        public static CompareDirection GetDirection(CompareEntry entry, CompareSide side, TimeSpan tolerance)
        {
            if (entry == null || entry.IsDirectory || entry.Status != CompareStatus.Different ||
                entry.LeftModified == null || entry.RightModified == null)
                return CompareDirection.None;

            var gap = entry.LeftModified.Value - entry.RightModified.Value;
            if (gap.Duration() <= tolerance)
                return CompareDirection.None;

            var leftNewer = gap > TimeSpan.Zero;
            return leftNewer == (side == CompareSide.Left) ? CompareDirection.Newer : CompareDirection.Older;
        }

        private static string ParentOf(string relativePath)
        {
            var index = relativePath.LastIndexOf(Path.DirectorySeparatorChar);
            return index < 0 ? string.Empty : relativePath.Substring(0, index);
        }
    }
}
