using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Folderss.Models;

namespace Folderss.Services
{
    /// <summary>
    /// git 명령 출력 파서. 모두 기계용 출력 형식(<c>-z</c>, 필드 구분 0x1F)만 받는다 — 사람용 문구는 파싱하지 않는다.
    /// </summary>
    public static class GitOutputParser
    {
        public const char FieldSeparator = '\x1f';

        /// <summary><c>git log</c>에 넘길 형식. <see cref="ParseLog"/>와 짝이다.</summary>
        public const string LogFormat = "--format=%H%x1f%P%x1f%an%x1f%at%x1f%D%x1f%s";

        /// <summary><c>git for-each-ref</c>에 넘길 형식. <see cref="ParseBranches"/>와 짝이다.</summary>
        public const string BranchFormat = "--format=%(HEAD)%1f%(refname)%1f%(upstream:short)%1f%(contents:subject)";

        /// <summary>
        /// 상태 조회 인수. 추적 안 되는 폴더를 "dir/" 한 줄로 접지 않고 안의 파일을 하나씩 내도록 <c>--untracked-files=all</c>
        /// (그래야 새 폴더 안에서 add할 파일을 골라 볼 수 있다). .gitignore에 걸린 파일은 나오지 않는다.
        /// </summary>
        public static readonly string[] StatusArguments = { "status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all" };

        /// <summary><c>git ls-files -z</c>: 인덱스에 있는(추적 중인) 파일 목록.</summary>
        public static readonly string[] TrackedFilesArguments = { "ls-files", "-z", "--cached", "--full-name" };

        /// <summary>NUL 구분 경로 목록(<c>ls-files -z</c> 등)을 해석한다.</summary>
        public static List<string> ParseNulList(string output)
        {
            var list = new List<string>();
            foreach (var item in (output ?? string.Empty).Split('\0'))
            {
                if (item.Length > 0)
                    list.Add(item);
            }
            return list;
        }

        /// <summary>추적 중인 파일 중 상태 목록(변경·스테이지·충돌·이름 변경 전후)에 없는 파일 = 변경 없음.</summary>
        public static List<GitStatusEntry> UnchangedEntries(IEnumerable<string> trackedPaths, GitStatusSnapshot snapshot)
        {
            var changed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in snapshot?.Entries ?? new List<GitStatusEntry>())
            {
                changed.Add(entry.Path);
                if (!string.IsNullOrEmpty(entry.OriginalPath))
                    changed.Add(entry.OriginalPath);
            }

            var result = new List<GitStatusEntry>();
            foreach (var path in trackedPaths)
            {
                if (!changed.Contains(path))
                    result.Add(new GitStatusEntry { Path = path, IsUnchanged = true });
            }
            // ls-files는 충돌 파일을 단계(stage)마다 되풀이하므로 중복을 없앤다.
            return result.GroupBy(e => e.Path, StringComparer.Ordinal).Select(g => g.First()).ToList();
        }

        /// <summary>파일 내용을 줄 번호가 붙은 문맥 줄로(변경 없는 파일 미리보기). 한도를 넘으면 안내 줄을 붙인다.</summary>
        public static List<GitDiffLine> ContentLines(string content, int maxLines = int.MaxValue)
        {
            var lines = new List<GitDiffLine>();
            var text = (content ?? string.Empty).Replace("\r\n", "\n");
            if (text.EndsWith("\n", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 1);
            if (text.Length == 0)
                return lines;

            var raw = text.Split('\n');
            for (var i = 0; i < raw.Length; i++)
            {
                if (lines.Count >= maxLines)
                {
                    lines.Add(new GitDiffLine { Kind = GitDiffLineKind.Meta, Text = string.Format("… 너무 길어 이하 {0}줄은 표시하지 않습니다.", raw.Length - i) });
                    break;
                }
                lines.Add(new GitDiffLine { Kind = GitDiffLineKind.Context, Text = raw[i], OldLine = i + 1, NewLine = i + 1 });
            }
            return lines;
        }

        /// <summary><c>git status --porcelain=v2 --branch -z</c> 출력을 해석한다.</summary>
        public static GitStatusSnapshot ParseStatusV2(string output)
        {
            var snapshot = new GitStatusSnapshot();
            var records = (output ?? string.Empty).Split('\0');
            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                if (record.Length < 2)
                    continue;

                switch (record[0])
                {
                    case '#':
                        ParseHeader(snapshot, record);
                        break;
                    case '1':
                    {
                        // 1 XY sub mH mI mW hH hI path
                        var parts = record.Split(' ', 9);
                        if (parts.Length == 9)
                            snapshot.Entries.Add(CreateEntry(parts[1], parts[8]));
                        break;
                    }
                    case '2':
                    {
                        // 2 XY sub mH mI mW hH hI Xscore path \0 origPath
                        var parts = record.Split(' ', 10);
                        if (parts.Length == 10)
                        {
                            var entry = CreateEntry(parts[1], parts[9]);
                            if (i + 1 < records.Length)
                                entry.OriginalPath = records[++i];
                            snapshot.Entries.Add(entry);
                        }
                        break;
                    }
                    case 'u':
                    {
                        // u XY sub m1 m2 m3 mW h1 h2 h3 path
                        var parts = record.Split(' ', 11);
                        if (parts.Length == 11)
                        {
                            var entry = CreateEntry(parts[1], parts[10]);
                            entry.IsConflicted = true;
                            snapshot.Entries.Add(entry);
                        }
                        break;
                    }
                    case '?':
                        snapshot.Entries.Add(new GitStatusEntry { Path = record.Substring(2), IsUntracked = true });
                        break;
                }
            }
            return snapshot;
        }

        private static void ParseHeader(GitStatusSnapshot snapshot, string record)
        {
            var parts = record.Split(' ', 3);
            if (parts.Length < 3)
                return;

            var value = parts[2];
            switch (parts[1])
            {
                case "branch.oid":
                    snapshot.HeadOid = value == "(initial)" ? null : value;
                    break;
                case "branch.head":
                    snapshot.IsDetached = value == "(detached)";
                    snapshot.Branch = snapshot.IsDetached ? null : value;
                    break;
                case "branch.upstream":
                    snapshot.Upstream = value;
                    break;
                case "branch.ab":
                    var ab = value.Split(' ');
                    if (ab.Length == 2)
                    {
                        int.TryParse(ab[0].TrimStart('+'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ahead);
                        int.TryParse(ab[1].TrimStart('-'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var behind);
                        snapshot.Ahead = ahead;
                        snapshot.Behind = behind;
                    }
                    break;
            }
        }

        private static GitStatusEntry CreateEntry(string xy, string path)
        {
            return new GitStatusEntry
            {
                Path = path,
                IndexState = xy.Length > 0 ? xy[0] : '.',
                WorkTreeState = xy.Length > 1 ? xy[1] : '.'
            };
        }

        /// <summary><c>git log -z</c> + <see cref="LogFormat"/> 출력을 해석한다.</summary>
        public static List<GitCommitInfo> ParseLog(string output)
        {
            var commits = new List<GitCommitInfo>();
            foreach (var record in (output ?? string.Empty).Split('\0'))
            {
                var fields = record.Trim('\n', '\r').Split(FieldSeparator);
                if (fields.Length < 6 || fields[0].Length == 0)
                    continue;

                long.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTime);
                commits.Add(new GitCommitInfo
                {
                    Hash = fields[0],
                    Parents = fields[1].Length == 0 ? Array.Empty<string>() : fields[1].Split(' '),
                    Author = fields[2],
                    Time = DateTimeOffset.FromUnixTimeSeconds(unixTime),
                    Refs = fields[4],
                    // 제목에 구분자가 들어갈 일은 없지만, 들어가도 잘리지 않게 나머지를 합친다.
                    Subject = string.Join(FieldSeparator.ToString(), fields, 5, fields.Length - 5)
                });
            }
            return commits;
        }

        private static readonly Regex HunkHeader = new Regex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);

        /// <summary>
        /// unified diff 텍스트를 줄 단위로 분류하고 변경 전/후 줄 번호를 붙인다.
        /// "---"/"+++"는 hunk 밖일 때만 헤더로 본다 — hunk 안에서 "--"로 시작하는 줄이 지워지면 "---…"가 되기 때문.
        /// <paramref name="maxLines"/>를 넘으면 자르고 생략 안내 줄을 붙인다.
        /// </summary>
        public static List<GitDiffLine> ParseDiff(string diff, int maxLines = int.MaxValue)
        {
            var lines = new List<GitDiffLine>();
            var text = (diff ?? string.Empty).Replace("\r\n", "\n");
            if (text.EndsWith("\n", StringComparison.Ordinal))
                text = text.Substring(0, text.Length - 1);
            if (text.Length == 0)
                return lines;

            var raw = text.Split('\n');
            var inHunk = false;
            var combined = false;   // 충돌 파일의 combined diff(@@@): 앞 두 글자가 부모별 표시, 줄 번호 없음
            int oldLine = 0, newLine = 0;
            for (var i = 0; i < raw.Length; i++)
            {
                if (lines.Count >= maxLines)
                {
                    lines.Add(new GitDiffLine
                    {
                        Kind = GitDiffLineKind.Meta,
                        Text = string.Format("… 너무 길어 이하 {0}줄은 표시하지 않습니다.", raw.Length - i)
                    });
                    break;
                }

                var line = raw[i];
                var entry = new GitDiffLine { Text = line };

                if (line.StartsWith("diff ", StringComparison.Ordinal))
                {
                    inHunk = false;
                    entry.Kind = GitDiffLineKind.Header;
                }
                else if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    var match = HunkHeader.Match(line);
                    inHunk = true;
                    combined = !match.Success;
                    entry.Kind = GitDiffLineKind.Hunk;
                    if (match.Success)
                    {
                        oldLine = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                        newLine = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    }
                }
                else if (!inHunk)
                {
                    entry.Kind = GitDiffLineKind.Header;
                }
                else if (line.StartsWith("\\", StringComparison.Ordinal))
                {
                    entry.Kind = GitDiffLineKind.Meta;
                }
                else if (combined)
                {
                    var marks = line.Length >= 2 ? line.Substring(0, 2) : line;
                    entry.Kind = marks.Contains('+') ? GitDiffLineKind.Added
                        : marks.Contains('-') ? GitDiffLineKind.Removed
                        : GitDiffLineKind.Context;
                }
                else if (line.StartsWith("+", StringComparison.Ordinal))
                {
                    entry.Kind = GitDiffLineKind.Added;
                    entry.NewLine = newLine++;
                }
                else if (line.StartsWith("-", StringComparison.Ordinal))
                {
                    entry.Kind = GitDiffLineKind.Removed;
                    entry.OldLine = oldLine++;
                }
                else
                {
                    entry.Kind = GitDiffLineKind.Context;
                    entry.OldLine = oldLine++;
                    entry.NewLine = newLine++;
                }
                lines.Add(entry);
            }
            return lines;
        }

        /// <summary><c>git for-each-ref</c> + <see cref="BranchFormat"/> 출력을 해석한다. 원격 HEAD 별칭은 뺀다.</summary>
        public static List<GitBranchInfo> ParseBranches(string output)
        {
            var branches = new List<GitBranchInfo>();
            foreach (var line in (output ?? string.Empty).Split('\n'))
            {
                var fields = line.TrimEnd('\r').Split(FieldSeparator);
                if (fields.Length < 4)
                    continue;

                var refName = fields[1];
                GitBranchInfo branch;
                if (refName.StartsWith("refs/heads/", StringComparison.Ordinal))
                {
                    branch = new GitBranchInfo { Name = refName.Substring("refs/heads/".Length) };
                }
                else if (refName.StartsWith("refs/remotes/", StringComparison.Ordinal))
                {
                    if (refName.EndsWith("/HEAD", StringComparison.Ordinal))
                        continue;
                    branch = new GitBranchInfo { Name = refName.Substring("refs/remotes/".Length), IsRemote = true };
                }
                else
                {
                    continue;
                }

                branch.IsCurrent = fields[0] == "*";
                branch.Upstream = fields[2];
                branch.Subject = fields[3];
                branches.Add(branch);
            }
            return branches;
        }
    }
}
