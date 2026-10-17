namespace DiffBench;

/// <summary>
/// 직접 구현 ①: Myers O(ND) 차이, 선형 공간(가운데 뱀으로 나눠 정복).
/// 정방향·역방향 탐색이 만나는 점에서 둘로 나눠 각각 다시 푼다. 재귀 대신 작업 스택을 쓴다.
/// </summary>
public static class MyersDiff
{
    public static List<Block> Diff(int[] a, int[] b)
    {
        var output = new List<Block>();
        Run(a, 0, a.Length, b, 0, b.Length, output);
        return output;
    }

    internal static void Run(int[] a, int aLo0, int aHi0, int[] b, int bLo0, int bHi0, List<Block> output)
    {
        var stack = new Stack<(int, int, int, int)>();
        stack.Push((aLo0, aHi0, bLo0, bHi0));
        while (stack.Count > 0)
        {
            var (aLo, aHi, bLo, bHi) = stack.Pop();
            var trimmed = LineDiff.Trim(a, aLo, aHi, b, bLo, bHi, output);
            if (trimmed == null)
                continue;
            (aLo, aHi, bLo, bHi) = trimmed.Value;
            var split = MiddleSnake(a, aLo, aHi, b, bLo, bHi);
            if (split == null)
            {
                output.Add(new Block(aLo, aHi - aLo, bLo, bHi - bLo));
                continue;
            }
            var (x, y) = split.Value;
            stack.Push((aLo + x, aHi, bLo + y, bHi));
            stack.Push((aLo, aLo + x, bLo, bLo + y));
        }
    }

    /// <summary>두 탐색이 만나는 (x, y)(구간 안 상대 위치). 공통 줄이 하나도 없으면 null.</summary>
    private static (int, int)? MiddleSnake(int[] a, int aOff, int aEnd, int[] b, int bOff, int bEnd)
    {
        var n = aEnd - aOff;
        var m = bEnd - bOff;
        var maxD = (n + m + 1) / 2;
        var vOffset = maxD;
        var vLength = 2 * maxD + 2;
        var v1 = new int[vLength];
        var v2 = new int[vLength];
        Array.Fill(v1, -1);
        Array.Fill(v2, -1);
        v1[vOffset + 1] = 0;
        v2[vOffset + 1] = 0;
        var delta = n - m;
        var front = (delta & 1) != 0;
        int k1Start = 0, k1End = 0, k2Start = 0, k2End = 0;
        for (var d = 0; d < maxD; d++)
        {
            for (var k1 = -d + k1Start; k1 <= d - k1End; k1 += 2)
            {
                var k1Offset = vOffset + k1;
                var x1 = k1 == -d || (k1 != d && v1[k1Offset - 1] < v1[k1Offset + 1]) ? v1[k1Offset + 1] : v1[k1Offset - 1] + 1;
                var y1 = x1 - k1;
                while (x1 < n && y1 < m && a[aOff + x1] == b[bOff + y1]) { x1++; y1++; }
                v1[k1Offset] = x1;
                if (x1 > n) k1End += 2;
                else if (y1 > m) k1Start += 2;
                else if (front)
                {
                    var k2Offset = vOffset + delta - k1;
                    if (k2Offset >= 0 && k2Offset < vLength && v2[k2Offset] != -1 && x1 >= n - v2[k2Offset])
                        return (x1, y1);
                }
            }
            for (var k2 = -d + k2Start; k2 <= d - k2End; k2 += 2)
            {
                var k2Offset = vOffset + k2;
                var x2 = k2 == -d || (k2 != d && v2[k2Offset - 1] < v2[k2Offset + 1]) ? v2[k2Offset + 1] : v2[k2Offset - 1] + 1;
                var y2 = x2 - k2;
                while (x2 < n && y2 < m && a[aEnd - x2 - 1] == b[bEnd - y2 - 1]) { x2++; y2++; }
                v2[k2Offset] = x2;
                if (x2 > n) k2End += 2;
                else if (y2 > m) k2Start += 2;
                else if (!front)
                {
                    var k1Offset = vOffset + delta - k2;
                    if (k1Offset >= 0 && k1Offset < vLength && v1[k1Offset] != -1)
                    {
                        var x1 = v1[k1Offset];
                        var y1 = vOffset + x1 - k1Offset;
                        if (x1 >= n - x2)
                            return (x1, y1);
                    }
                }
            }
        }
        return null;
    }
}
