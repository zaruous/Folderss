using System.Collections.Generic;
using System.Linq;
using Folderss.Models;

namespace Folderss.Services
{
    public enum GitRestoreMode
    {
        /// <summary>작업 트리의 변경만 버리고 스테이지된 내용으로 되돌린다(<c>restore --worktree</c>). 스테이지는 그대로.</summary>
        WorkTree,
        /// <summary>스테이지와 작업 트리를 모두 HEAD로 되돌린다(<c>restore --source=HEAD --staged --worktree</c>).</summary>
        WorkTreeAndIndex
    }

    /// <summary>
    /// 파일 되돌리기(<c>git restore</c>). 버린 변경은 git에 기록이 남지 않아 되돌릴 수 없다.
    /// git은 경로 하나라도 맞지 않으면 명령 전체를 거부하고, 일부 경우엔 파일을 지우거나 비우므로 대상을 먼저 거른다.
    /// </summary>
    public static class GitRestoreCommands
    {
        /// <summary>경로는 표준 입력으로 받는다(<see cref="PathspecInput"/>).</summary>
        public static List<string> Arguments(GitRestoreMode mode)
        {
            return mode == GitRestoreMode.WorkTree
                ? new List<string> { "restore", "--worktree", "--pathspec-from-file=-", "--pathspec-file-nul" }
                : new List<string> { "restore", "--source=HEAD", "--staged", "--worktree", "--pathspec-from-file=-", "--pathspec-file-nul" };
        }

        /// <summary>
        /// 되돌릴 수 없는(또는 되돌리면 안 되는) 항목이면 그 이유, 되돌릴 수 있으면 null.
        /// - 새 파일(추적 안 함): git이 모르는 경로라 명령 전체가 실패한다.
        /// - <c>git add -N</c> 항목: 인덱스 내용이 비어 있어 작업 트리 모드는 파일을 빈 파일로 만든다.
        /// - 충돌: 작업 트리 모드는 git이 거부한다(HEAD 모드는 HEAD 쪽으로 해결).
        /// - HEAD 모드에서 HEAD에 없는 경로(새로 추가·이름 변경·복사): 디스크에서 파일이 지워진다.
        /// </summary>
        public static string ExclusionReason(GitStatusEntry entry, GitRestoreMode mode, bool hasHead)
        {
            if (entry.IsUntracked)
                return "새 파일";
            if (entry.IsConflicted)
            {
                if (mode == GitRestoreMode.WorkTree)
                    return "충돌";
                if (!hasHead)
                    return "HEAD 없음";
                // UA·AA: 우리 쪽(HEAD)에 없는 경로라 HEAD로 되돌리면 파일이 지워진다.
                return entry.WorkTreeState == 'A' ? "HEAD에 없는 경로" : null;
            }
            if (entry.WorkTreeState == 'A')
                return "git add -N 항목";
            if (mode == GitRestoreMode.WorkTree)
                return null;
            if (!hasHead)
                return "HEAD 없음";
            if (entry.IndexState == 'A' || entry.IndexState == 'R' || entry.IndexState == 'C')
                return "HEAD에 없는 경로";
            return null;
        }

        public static List<GitStatusEntry> Restorable(IEnumerable<GitStatusEntry> entries, GitRestoreMode mode, bool hasHead)
        {
            return entries.Where(entry => ExclusionReason(entry, mode, hasHead) == null).ToList();
        }

        /// <summary>NUL 구분 경로 목록(명령줄 길이 제한·특수 문자 회피).</summary>
        public static string PathspecInput(IEnumerable<GitStatusEntry> entries)
        {
            return string.Join("\0", entries.Select(entry => entry.Path).Distinct()) + "\0";
        }
    }
}
