using System.Collections.Generic;
using Folderss.Models;

namespace Folderss.Services
{
    /// <summary>
    /// 커밋 목록(<c>--topo-order</c>, 자식이 부모보다 먼저)과 부모 해시로 브랜치 그래프 레인을 계산한다.
    /// <c>git log --graph</c>의 ASCII를 파싱하지 않고 직접 계산한다(순수 로직 — WPF 없이 테스트).
    ///
    /// 레인은 "다음에 나올 것으로 기다리는 커밋 해시"다. 커밋이 오면
    /// 1) 자신을 기다리는 레인(없으면 빈 레인/새 레인)에 점을 찍고,
    /// 2) 같은 커밋을 기다리던 다른 레인들은 이 점으로 합쳐지며 끝나고,
    /// 3) 첫 부모는 같은 레인을 이어받고, 나머지 부모(병합)는 이미 기다리는 레인이 있으면 그리로, 없으면 빈 레인을 새로 연다.
    /// 목록이 개수 제한으로 잘리면 목록 밖 부모를 기다리는 레인은 아래로 계속 이어진 채 끝난다.
    /// </summary>
    public static class GitGraphLayout
    {
        public const int Top = 0;
        public const int Center = 1;
        public const int Bottom = 2;

        public static List<GitGraphRow> Compute(IList<GitCommitInfo> commits)
        {
            var rows = new List<GitGraphRow>(commits.Count);
            var lanes = new List<string>();

            foreach (var commit in commits)
            {
                var row = new GitGraphRow { IsMerge = commit.Parents.Length > 1 };

                var node = lanes.IndexOf(commit.Hash);
                var isTip = node < 0;
                if (isTip)
                {
                    // 아무도 기다리지 않던 커밋 = 브랜치 끝(tip). 빈 레인을 쓰되 위에서 내려오는 선은 없다.
                    node = FirstFree(lanes, -1);
                    lanes[node] = commit.Hash;
                }
                row.NodeLane = node;

                var before = lanes.ToArray();

                // 위 절반: 이 커밋을 기다리던 레인은 점으로 모이고, 나머지는 그대로 지나간다.
                for (var i = 0; i < before.Length; i++)
                {
                    if (before[i] == null)
                        continue;
                    if (before[i] == commit.Hash)
                    {
                        if (i != node || !isTip)
                            row.Segments.Add(new GitGraphSegment(i, Top, node, Center));
                        if (i != node)
                            lanes[i] = null;
                    }
                }

                // 부모 배치
                var parentLanes = new List<int>();
                if (commit.Parents.Length == 0)
                {
                    lanes[node] = null;
                }
                else
                {
                    lanes[node] = commit.Parents[0];
                    parentLanes.Add(node);
                    for (var p = 1; p < commit.Parents.Length; p++)
                    {
                        var parent = commit.Parents[p];
                        var lane = lanes.IndexOf(parent);
                        if (lane < 0)
                        {
                            lane = FirstFree(lanes, node);
                            lanes[lane] = parent;
                        }
                        parentLanes.Add(lane);
                    }
                }

                // 지나가는 레인(위아래 같은 커밋을 계속 기다림)
                for (var i = 0; i < before.Length; i++)
                {
                    if (before[i] != null && before[i] != commit.Hash && i < lanes.Count && lanes[i] == before[i])
                        row.Segments.Add(new GitGraphSegment(i, Top, i, Bottom));
                }

                // 아래 절반: 점에서 각 부모 레인으로
                foreach (var lane in parentLanes)
                    row.Segments.Add(new GitGraphSegment(node, Center, lane, Bottom));

                TrimTrailingFree(lanes);
                row.LaneCount = System.Math.Max(before.Length, System.Math.Max(lanes.Count, node + 1));
                rows.Add(row);
            }
            return rows;
        }

        private static int FirstFree(List<string> lanes, int skip)
        {
            for (var i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] == null && i != skip)
                    return i;
            }
            lanes.Add(null);
            return lanes.Count - 1;
        }

        private static void TrimTrailingFree(List<string> lanes)
        {
            while (lanes.Count > 0 && lanes[lanes.Count - 1] == null)
                lanes.RemoveAt(lanes.Count - 1);
        }
    }
}
