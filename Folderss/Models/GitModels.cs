using System;
using System.Collections.Generic;
using System.Linq;

namespace Folderss.Models
{
    /// <summary>탐색으로 찾은 Git 저장소 하나.</summary>
    public sealed class GitRepositoryInfo
    {
        public string RootPath { get; set; }

        /// <summary>기준 폴더 기준 상대 경로. 기준 폴더 자신이면 ".", 상위 저장소면 전체 경로.</summary>
        public string DisplayPath { get; set; }

        /// <summary>기준 폴더를 품은 상위 저장소인지.</summary>
        public bool IsAncestor { get; set; }

        /// <summary>.git이 폴더가 아니라 파일(워크트리·서브모듈)인지.</summary>
        public bool IsGitFile { get; set; }
    }

    public sealed class GitStatusEntry
    {
        public string Path { get; set; }
        public string OriginalPath { get; set; }

        /// <summary>porcelain v2 XY의 X(인덱스) — '.'은 변경 없음.</summary>
        public char IndexState { get; set; } = '.';

        /// <summary>porcelain v2 XY의 Y(작업 트리) — '.'은 변경 없음.</summary>
        public char WorkTreeState { get; set; } = '.';

        public bool IsUntracked { get; set; }
        public bool IsConflicted { get; set; }

        public bool IsStaged => !IsUntracked && !IsConflicted && IndexState != '.';
        public bool IsUnstaged => IsUntracked || IsConflicted || WorkTreeState != '.';

        public string DisplayText
        {
            get
            {
                var name = string.IsNullOrEmpty(OriginalPath) ? Path : OriginalPath + " → " + Path;
                if (IsUntracked) return "?  " + name;
                if (IsConflicted) return "!  " + name;
                return string.Format("{0}{1} {2}", IndexState, WorkTreeState, name);
            }
        }
    }

    /// <summary>
    /// 변경 사항 트리 보기의 노드. <see cref="Entry"/>가 있으면 파일, 없으면 폴더.
    /// 자식이 폴더 하나뿐인 폴더 체인은 한 노드로 합친다(<c>src/app/core</c>).
    /// </summary>
    public sealed class GitChangeNode
    {
        public string Name { get; set; }

        /// <summary>저장소 기준 폴더 경로(슬래시 구분). 파일 노드는 파일 경로.</summary>
        public string Path { get; set; }
        public GitStatusEntry Entry { get; set; }
        public List<GitChangeNode> Children { get; } = new List<GitChangeNode>();

        public bool IsFolder => Entry == null;

        /// <summary>이 노드 아래(자신 포함) 파일 항목 전부.</summary>
        public IEnumerable<GitStatusEntry> Entries
        {
            get
            {
                if (Entry != null)
                {
                    yield return Entry;
                    yield break;
                }
                foreach (var child in Children)
                    foreach (var entry in child.Entries)
                        yield return entry;
            }
        }

        public string DisplayText
        {
            get
            {
                if (IsFolder)
                    return string.Format("📁 {0}  ({1})", Name, Entries.Count());
                var mark = Entry.IsUntracked ? "? " : Entry.IsConflicted ? "! " : string.Format("{0}{1}", Entry.IndexState, Entry.WorkTreeState);
                return mark + " " + Name + (string.IsNullOrEmpty(Entry.OriginalPath) ? string.Empty : "  ← " + Entry.OriginalPath);
            }
        }
    }

    public sealed class GitStatusSnapshot
    {
        public string Branch { get; set; }
        public string Upstream { get; set; }
        public int Ahead { get; set; }
        public int Behind { get; set; }
        public bool IsDetached { get; set; }

        /// <summary>HEAD 커밋 해시. 최초 커밋 전이면 null.</summary>
        public string HeadOid { get; set; }

        public List<GitStatusEntry> Entries { get; } = new List<GitStatusEntry>();

        public int StagedCount => Entries.Count(e => e.IsStaged);
        public int UnstagedCount => Entries.Count(e => !e.IsUntracked && !e.IsConflicted && e.WorkTreeState != '.');
        public int UntrackedCount => Entries.Count(e => e.IsUntracked);
        public int ConflictCount => Entries.Count(e => e.IsConflicted);

        public string BranchDisplay
        {
            get
            {
                if (!IsDetached)
                    return Branch ?? string.Empty;
                return HeadOid == null ? "(detached)" : "(detached @" + HeadOid.Substring(0, Math.Min(7, HeadOid.Length)) + ")";
            }
        }
    }

    public sealed class GitCommitInfo
    {
        public string Hash { get; set; }
        public string[] Parents { get; set; } = Array.Empty<string>();
        public string Author { get; set; }
        public DateTimeOffset Time { get; set; }
        public string Refs { get; set; }
        public string Subject { get; set; }

        public string ShortHash => Hash == null ? string.Empty : Hash.Substring(0, Math.Min(7, Hash.Length));
        public string TimeText => Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        public string ListText => ShortHash + "  " + Subject;

        /// <summary>브랜치 그래프 한 행. 로그 목록에서만 채운다.</summary>
        public GitGraphRow Graph { get; set; }
    }

    /// <summary>그래프 한 행의 선분. Y는 0=행 위, 1=가운데(커밋 점), 2=행 아래. 색은 도착 레인 기준.</summary>
    public struct GitGraphSegment
    {
        public int FromLane;
        public int FromY;
        public int ToLane;
        public int ToY;

        public GitGraphSegment(int fromLane, int fromY, int toLane, int toY)
        {
            FromLane = fromLane;
            FromY = fromY;
            ToLane = toLane;
            ToY = toY;
        }

        public override string ToString() => string.Format("{0}:{1}->{2}:{3}", FromLane, FromY, ToLane, ToY);
    }

    public sealed class GitGraphRow
    {
        /// <summary>커밋 점이 놓이는 레인.</summary>
        public int NodeLane { get; set; }

        /// <summary>이 행에서 쓰는 레인 수(그리기 폭 계산용).</summary>
        public int LaneCount { get; set; }

        public bool IsMerge { get; set; }
        public List<GitGraphSegment> Segments { get; } = new List<GitGraphSegment>();
    }

    public enum GitDiffLineKind
    {
        /// <summary>파일 헤더(diff --git, index, ---/+++, rename, Binary files…)와 첫 파일 앞의 머리말.</summary>
        Header,
        Hunk,
        Context,
        Added,
        Removed,
        /// <summary>"\ No newline at end of file", 생략 안내 등.</summary>
        Meta
    }

    public sealed class GitDiffLine
    {
        public GitDiffLineKind Kind { get; set; }
        public string Text { get; set; }

        /// <summary>변경 전 줄 번호(삭제·문맥 줄). 없으면 null.</summary>
        public int? OldLine { get; set; }

        /// <summary>변경 후 줄 번호(추가·문맥 줄). 없으면 null.</summary>
        public int? NewLine { get; set; }
    }

    public sealed class GitBranchInfo
    {
        /// <summary>표시·명령용 이름 (예: main, origin/main).</summary>
        public string Name { get; set; }
        public bool IsRemote { get; set; }
        public bool IsCurrent { get; set; }
        public string Upstream { get; set; }
        public string Subject { get; set; }

        public string DisplayText => (IsCurrent ? "● " : "   ") + Name + (string.IsNullOrEmpty(Upstream) ? string.Empty : "  → " + Upstream);
    }
}
