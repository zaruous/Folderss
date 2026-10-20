using System;
using System.Collections.Generic;
using Folderss.Models;

namespace Folderss.Services
{
    /// <summary>
    /// 브랜치 탭의 요약 그래프. 커밋 전체 대신 브랜치 끝(tip)·HEAD·각 tip이 HEAD와 갈라진 지점(merge-base)만 남기고,
    /// 남긴 커밋의 부모를 가장 가까운 남긴 조상으로 다시 잇는다. <c>git log --simplify-by-decoration --parents</c>와 비슷하지만
    /// 갈라진 지점을 남긴다 — 빼면 갈라진 브랜치가 훨씬 오래된 커밋에서 갈라진 것처럼 보인다.
    /// 같은 계산으로 tip마다 HEAD 대비 앞섬/뒤처짐 커밋 수도 낸다. LibGit2Sharp·git CLI 어느 쪽으로 읽든 이 함수를 거친다(순수 로직).
    /// </summary>
    public static class GitBranchGraph
    {
        public sealed class Result
        {
            /// <summary>남긴 커밋(입력 순서 = topo 순서). Parents는 다시 이은 부모다.</summary>
            public List<GitCommitInfo> Rows { get; } = new List<GitCommitInfo>();

            /// <summary>tip·HEAD가 아니고 갈라진 지점이라서 남긴 커밋.</summary>
            public HashSet<string> ForkPoints { get; } = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>tip 해시 → HEAD 대비 (tip에만 있는 커밋 수, HEAD에만 있는 커밋 수). HEAD가 없으면 비어 있다.</summary>
            public Dictionary<string, (int Ahead, int Behind)> Divergence { get; } = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        }

        /// <summary>
        /// LibGit2Sharp로 못 읽을 때 쓸 <c>git log</c> 인수. 출력은 <see cref="GitOutputParser.ParseLog"/> 후 <see cref="Build"/>로.
        /// 브랜치 끝을 해시로 넘기면 원격 브랜치가 많을 때 명령줄 길이 제한에 걸리므로 <c>--branches</c>/<c>--remotes</c>로 고른다.
        /// </summary>
        public static List<string> CliArguments(bool includeRemotes, bool hasHead)
        {
            var args = new List<string> { "log", "--topo-order", "-z", GitOutputParser.LogFormat, "--branches" };
            if (includeRemotes)
                args.Add("--remotes");
            if (hasHead)
                args.Add("HEAD");
            return args;
        }

        /// <param name="commits">tip들과 HEAD에서 닿는 모든 커밋, <c>--topo-order</c>(자식이 부모보다 먼저). Hash·Parents만 쓴다.</param>
        /// <param name="tips">남길 브랜치 끝 커밋 해시.</param>
        /// <param name="head">HEAD 커밋 해시. 없으면(첫 커밋 전) null — 앞섬/뒤처짐·갈라진 지점을 내지 않는다.</param>
        public static Result Build(IList<GitCommitInfo> commits, IEnumerable<string> tips, string head)
        {
            var n = commits.Count;
            var index = new Dictionary<string, int>(n, StringComparer.Ordinal);
            for (var i = 0; i < n; i++)
                index[commits[i].Hash] = i;

            // 목록 밖 부모(shallow 경계)는 버린다.
            var parents = new int[n][];
            for (var i = 0; i < n; i++)
            {
                var list = new List<int>(commits[i].Parents.Length);
                foreach (var parent in commits[i].Parents)
                {
                    if (index.TryGetValue(parent, out var p))
                        list.Add(p);
                }
                parents[i] = list.ToArray();
            }

            var result = new Result();
            var keep = new bool[n];
            var tipSet = new HashSet<int>();
            var mergeBases = new List<int>();

            bool[] headReach = null;
            var headIndex = -1;
            if (head != null && index.TryGetValue(head, out headIndex))
            {
                keep[headIndex] = true;
                headReach = new bool[n];
                Reach(parents, headIndex, headReach);
            }

            var reach = headReach == null ? null : new bool[n];
            foreach (var tip in tips)
            {
                if (tip == null || !index.TryGetValue(tip, out var t) || !tipSet.Add(t))
                    continue;
                keep[t] = true;
                if (headReach == null)
                    continue;

                Array.Clear(reach, 0, n);
                Reach(parents, t, reach);
                // topo 순서에서 처음 만나는 공통 조상은 다른 공통 조상의 조상일 수 없다 = 가장 가까운 merge-base.
                int ahead = 0, behind = 0, mergeBase = -1;
                for (var i = 0; i < n; i++)
                {
                    if (reach[i] && headReach[i])
                    {
                        if (mergeBase < 0)
                            mergeBase = i;
                    }
                    else if (reach[i])
                    {
                        ahead++;
                    }
                    else if (headReach[i])
                    {
                        behind++;
                    }
                }
                result.Divergence[tip] = (ahead, behind);
                if (mergeBase >= 0)
                {
                    keep[mergeBase] = true;
                    mergeBases.Add(mergeBase);
                }
            }

            foreach (var i in mergeBases)
            {
                if (!tipSet.Contains(i) && i != headIndex)
                    result.ForkPoints.Add(commits[i].Hash);
            }

            // nearest[i]: i 자신(남김) 또는 i의 조상 중 가장 가까운 남긴 커밋들. 부모가 뒤에 있으므로 뒤에서부터 채운다.
            var nearest = new List<int>[n];
            var rewritten = new List<int>[n];
            for (var i = n - 1; i >= 0; i--)
            {
                List<int> up;
                if (parents[i].Length == 1)
                {
                    up = nearest[parents[i][0]];   // 한 줄기는 목록을 복사하지 않고 공유한다
                }
                else
                {
                    up = new List<int>();
                    foreach (var p in parents[i])
                    {
                        foreach (var k in nearest[p])
                        {
                            if (!up.Contains(k))
                                up.Add(k);
                        }
                    }
                }

                if (keep[i])
                {
                    rewritten[i] = up;
                    nearest[i] = new List<int> { i };
                }
                else
                {
                    nearest[i] = up;
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (!keep[i])
                    continue;
                var source = commits[i];
                var newParents = new string[rewritten[i].Count];
                for (var k = 0; k < newParents.Length; k++)
                    newParents[k] = commits[rewritten[i][k]].Hash;
                result.Rows.Add(new GitCommitInfo
                {
                    Hash = source.Hash,
                    Parents = newParents,
                    Author = source.Author,
                    Time = source.Time,
                    Refs = source.Refs,
                    Subject = source.Subject
                });
            }
            return result;
        }

        private static void Reach(int[][] parents, int start, bool[] seen)
        {
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                if (seen[i])
                    continue;
                seen[i] = true;
                foreach (var p in parents[i])
                {
                    if (!seen[p])
                        stack.Push(p);
                }
            }
        }
    }
}
