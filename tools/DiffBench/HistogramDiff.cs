namespace DiffBench;

/// <summary>
/// 직접 구현 ②: Histogram 차이(JGit·git --histogram 방식을 단순화).
/// 옛 구간에서 적게 나오는 줄(최대 64회)을 기준점으로 삼아 가장 드문 공통 구간을 찾고, 그 앞뒤를 다시 푼다.
/// 기준점이 없으면(공통 줄이 모두 흔하거나 없음) Myers로 넘긴다. 코드처럼 `{`·빈 줄이 많은 글에서 사람이 읽기 좋은 결과를 낸다.
/// </summary>
public static class HistogramDiff
{
    private const int MaxChainLength = 64;

    public static List<Block> Diff(int[] a, int[] b)
    {
        var output = new List<Block>();
        var stack = new Stack<(int, int, int, int)>();
        stack.Push((0, a.Length, 0, b.Length));
        var positions = new Dictionary<int, List<int>>();
        while (stack.Count > 0)
        {
            var (aLo, aHi, bLo, bHi) = stack.Pop();
            var trimmed = LineDiff.Trim(a, aLo, aHi, b, bLo, bHi, output);
            if (trimmed == null)
                continue;
            (aLo, aHi, bLo, bHi) = trimmed.Value;

            positions.Clear();
            for (var i = aLo; i < aHi; i++)
            {
                if (!positions.TryGetValue(a[i], out var list))
                    positions[a[i]] = list = new List<int>();
                list.Add(i);
            }

            int bestCount = int.MaxValue, bestLength = 0, bestA = -1, bestB = -1;
            var j = bLo;
            while (j < bHi)
            {
                var next = j + 1;
                if (positions.TryGetValue(b[j], out var list) && list.Count <= MaxChainLength)
                {
                    foreach (var i in list)
                    {
                        int s = i, t = j;
                        while (s > aLo && t > bLo && a[s - 1] == b[t - 1]) { s--; t--; }
                        int e = i + 1, f = j + 1;
                        while (e < aHi && f < bHi && a[e] == b[f]) { e++; f++; }
                        var min = int.MaxValue;
                        for (var k = s; k < e && min > 1; k++)
                            min = Math.Min(min, positions[a[k]].Count);
                        var length = e - s;
                        if (min < bestCount || (min == bestCount && length > bestLength))
                        {
                            bestCount = min;
                            bestLength = length;
                            bestA = s;
                            bestB = t;
                        }
                        next = Math.Max(next, f);
                    }
                }
                j = next;
            }

            if (bestA < 0)
            {
                MyersDiff.Run(a, aLo, aHi, b, bLo, bHi, output);
                continue;
            }
            stack.Push((bestA + bestLength, aHi, bestB + bestLength, bHi));
            stack.Push((aLo, bestA, bLo, bestB));
        }
        return output;
    }
}
