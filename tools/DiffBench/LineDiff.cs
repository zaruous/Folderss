namespace DiffBench;

/// <summary>바뀐 구간 하나: 옛 줄 [AStart, AStart+ACount)를 새 줄 [BStart, BStart+BCount)로 바꾼다.</summary>
public readonly record struct Block(int AStart, int ACount, int BStart, int BCount);

/// <summary>직접 구현 공용: 줄을 정수 id로 바꾸고, 구간 목록을 검증한다.</summary>
public static class LineDiff
{
    /// <summary>'\n'으로 나눈다('\r'은 줄 내용에 남는다 — git과 같은 취급).</summary>
    public static string[] Split(string text) => text.Split('\n');

    public static (int[] A, int[] B, int IdCount) ToIds(string[] a, string[] b)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(string line)
        {
            if (!ids.TryGetValue(line, out var id))
                ids[line] = id = ids.Count;
            return id;
        }
        var ia = new int[a.Length];
        var ib = new int[b.Length];
        for (var i = 0; i < a.Length; i++) ia[i] = Id(a[i]);
        for (var i = 0; i < b.Length; i++) ib[i] = Id(b[i]);
        return (ia, ib, ids.Count);
    }

    /// <summary>구간 목록이 옛 줄을 새 줄로 정확히 바꾸는지 확인한다. 바뀐 줄 수(삭제+추가)를 돌려준다. 틀리면 예외.</summary>
    public static int Verify(string[] a, string[] b, List<Block> blocks)
    {
        blocks.Sort((x, y) => x.AStart != y.AStart ? x.AStart.CompareTo(y.AStart) : x.BStart.CompareTo(y.BStart));
        int ai = 0, bi = 0, changed = 0;
        foreach (var block in blocks)
        {
            var equal = block.AStart - ai;
            if (equal < 0 || block.BStart - bi != equal)
                throw new InvalidOperationException($"구간 어긋남: a {ai}→{block.AStart}, b {bi}→{block.BStart}");
            for (var k = 0; k < equal; k++)
                if (!string.Equals(a[ai + k], b[bi + k], StringComparison.Ordinal))
                    throw new InvalidOperationException($"같다고 한 줄이 다름: a[{ai + k}] b[{bi + k}]");
            ai = block.AStart + block.ACount;
            bi = block.BStart + block.BCount;
            changed += block.ACount + block.BCount;
        }
        if (a.Length - ai != b.Length - bi)
            throw new InvalidOperationException("끝 구간 길이 다름");
        for (var k = 0; k < a.Length - ai; k++)
            if (!string.Equals(a[ai + k], b[bi + k], StringComparison.Ordinal))
                throw new InvalidOperationException($"끝의 같다고 한 줄이 다름: a[{ai + k}]");
        return changed;
    }

    /// <summary>공통 앞·뒤를 잘라 낸 구간. 한쪽이 비면 구간 하나를 더하고 null.</summary>
    public static (int ALo, int AHi, int BLo, int BHi)? Trim(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, List<Block> output)
    {
        while (aLo < aHi && bLo < bHi && a[aLo] == b[bLo]) { aLo++; bLo++; }
        while (aLo < aHi && bLo < bHi && a[aHi - 1] == b[bHi - 1]) { aHi--; bHi--; }
        if (aLo == aHi || bLo == bHi)
        {
            if (aLo < aHi || bLo < bHi)
                output.Add(new Block(aLo, aHi - aLo, bLo, bHi - bLo));
            return null;
        }
        return (aLo, aHi, bLo, bHi);
    }
}
