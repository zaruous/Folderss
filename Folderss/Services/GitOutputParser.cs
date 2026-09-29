using System;
using System.Collections.Generic;
using System.Globalization;
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
