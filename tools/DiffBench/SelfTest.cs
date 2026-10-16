namespace DiffBench;

/// <summary>직접 구현 검사: 무작위 짧은 입력에서 결과가 정확한지(재구성), Myers가 최소인지(LCS 동적계획법과 비교).</summary>
public static class SelfTest
{
    public static void Run()
    {
        var random = new Random(42);
        int trials = 20_000, myersNotMinimal = 0;
        for (var t = 0; t < trials; t++)
        {
            var alphabet = random.Next(1, 6);
            var a = Enumerable.Range(0, random.Next(0, 40)).Select(_ => ((char)('a' + random.Next(alphabet))).ToString()).ToArray();
            var b = Enumerable.Range(0, random.Next(0, 40)).Select(_ => ((char)('a' + random.Next(alphabet))).ToString()).ToArray();
            var (ia, ib, _) = LineDiff.ToIds(a, b);
            var minimal = a.Length + b.Length - 2 * Lcs(ia, ib);
            var myers = LineDiff.Verify(a, b, MyersDiff.Diff(ia, ib));
            LineDiff.Verify(a, b, HistogramDiff.Diff(ia, ib));
            if (myers != minimal)
                myersNotMinimal++;
        }
        Console.WriteLine($"무작위 {trials:N0}건: 두 직접 구현 모두 재구성 정확. Myers가 최소가 아닌 경우 {myersNotMinimal}건.");
        if (myersNotMinimal > 0)
            Environment.ExitCode = 1;
    }

    private static int Lcs(int[] a, int[] b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
        return dp[a.Length, b.Length];
    }
}
