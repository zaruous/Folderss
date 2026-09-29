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
