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

    /// <summary>항목이 어느 목록에 보이는지. 같은 파일도 목록마다 상태가 다를 수 있다(예: 스테이지 쪽 "추가", 작업 트리 쪽 "수정").</summary>
    public enum GitChangeSide
    {
        /// <summary>변경됨(작업 트리 ↔ 인덱스) — Y 코드 기준.</summary>
        WorkTree,
        /// <summary>스테이지됨(인덱스 ↔ HEAD) — X 코드 기준.</summary>
        Index
    }

    /// <summary>목록에 보이는 상태 종류. 배지 글자·색을 정한다.</summary>
    public enum GitChangeKind
    {
        Modified,
        Added,
        Deleted,
        Renamed,
        Copied,
        TypeChanged,
        Conflict,
        Untracked,
        Unchanged
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

        /// <summary>추적 중이고 바뀌지 않은 파일("변경 없음" 목록용). 스테이지·언스테이지 대상이 아니다.</summary>
        public bool IsUnchanged { get; set; }

        public bool IsStaged => !IsUntracked && !IsConflicted && IndexState != '.';
        public bool IsUnstaged => IsUntracked || IsConflicted || WorkTreeState != '.';

        /// <summary>이 항목이 놓인 목록. 상태 배지를 X/Y 중 어느 코드로 볼지 정한다.</summary>
        public GitChangeSide Side { get; set; } = GitChangeSide.WorkTree;

        public GitChangeKind Kind
        {
            get
            {
                if (IsUnchanged) return GitChangeKind.Unchanged;
                if (IsConflicted) return GitChangeKind.Conflict;
                if (IsUntracked) return GitChangeKind.Untracked;
                switch (Side == GitChangeSide.Index ? IndexState : WorkTreeState)
                {
                    case 'A': return GitChangeKind.Added;
                    case 'D': return GitChangeKind.Deleted;
                    case 'R': return GitChangeKind.Renamed;
                    case 'C': return GitChangeKind.Copied;
                    case 'T': return GitChangeKind.TypeChanged;
                    default: return GitChangeKind.Modified;
                }
            }
        }

        public string StatusLabel => LabelOf(Kind);

        public static string LabelOf(GitChangeKind kind)
        {
            switch (kind)
            {
                case GitChangeKind.Added: return "추가";
                case GitChangeKind.Deleted: return "삭제";
                case GitChangeKind.Renamed: return "이름 변경";
                case GitChangeKind.Copied: return "복사";
                case GitChangeKind.TypeChanged: return "형식 변경";
                case GitChangeKind.Conflict: return "충돌";
                case GitChangeKind.Untracked: return "새 파일";
                case GitChangeKind.Unchanged: return "변경 없음";
                default: return "수정";
            }
        }

        /// <summary>목록에 보일 경로. 이름 변경·복사는 원래 경로를 함께.</summary>
        public string PathText => (Kind == GitChangeKind.Renamed || Kind == GitChangeKind.Copied) && !string.IsNullOrEmpty(OriginalPath)
            ? Path + "  ← " + OriginalPath
            : Path;

        /// <summary>같은 파일을 다른 목록에 둘 사본(명령에는 Path/OriginalPath만 쓰므로 안전).</summary>
        public GitStatusEntry ForSide(GitChangeSide side)
        {
            var copy = (GitStatusEntry)MemberwiseClone();
            copy.Side = side;
            return copy;
        }

        /// <summary>목록 제목용 종류별 개수: "수정 3 · 새 파일 2". 많은 순(같으면 종류 순).</summary>
        public static string Summarize(IEnumerable<GitStatusEntry> entries)
        {
            return string.Join(" · ", entries
                .GroupBy(e => e.Kind)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Select(g => LabelOf(g.Key) + " " + g.Count()));
        }

        public string DisplayText
        {
            get
            {
                var name = string.IsNullOrEmpty(OriginalPath) ? Path : OriginalPath + " → " + Path;
                if (IsUnchanged) return "   " + name;
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

        /// <summary>파일 노드의 상태 배지(폴더는 null).</summary>
        public string StatusLabel => Entry?.StatusLabel;
        public GitChangeKind? Kind => Entry?.Kind;

        /// <summary>배지 옆에 보일 글자: 폴더는 "📁 이름 (개수)", 파일은 이름(이름 변경은 원래 경로 포함).</summary>
        public string LabelText
        {
            get
            {
                if (IsFolder)
                    return string.Format("📁 {0}  ({1})", Name, Entries.Count());
                var renamed = (Entry.Kind == GitChangeKind.Renamed || Entry.Kind == GitChangeKind.Copied) && !string.IsNullOrEmpty(Entry.OriginalPath);
                return Name + (renamed ? "  ← " + Entry.OriginalPath : string.Empty);
            }
        }

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
                var mark = Entry.IsUnchanged ? "  " : Entry.IsUntracked ? "? " : Entry.IsConflicted ? "! " : string.Format("{0}{1}", Entry.IndexState, Entry.WorkTreeState);
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

    /// <summary><c>git worktree list --porcelain</c>의 항목 하나.</summary>
    public sealed class GitWorktreeInfo
    {
        public string Path { get; set; }
        public string Head { get; set; }

        /// <summary>체크아웃한 브랜치 이름(refs/heads/ 제외). detached·bare면 null.</summary>
        public string Branch { get; set; }
        public bool IsDetached { get; set; }
        public bool IsBare { get; set; }
        public bool IsLocked { get; set; }

        /// <summary>폴더가 없어져 prune 대상인지.</summary>
        public bool IsPrunable { get; set; }

        /// <summary>목록의 첫 항목 = 주 작업 트리(제거 불가).</summary>
        public bool IsMain { get; set; }

        public string DisplayText
        {
            get
            {
                var head = IsBare ? "(bare)" : IsDetached ? "(detached @" + (Head ?? string.Empty).Substring(0, System.Math.Min(7, (Head ?? string.Empty).Length)) + ")" : Branch;
                var flags = (IsMain ? "  [주 작업 트리]" : string.Empty) + (IsLocked ? "  [잠김]" : string.Empty) + (IsPrunable ? "  [폴더 없음 — prune 대상]" : string.Empty);
                return head + "    " + Path + flags;
            }
        }
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
