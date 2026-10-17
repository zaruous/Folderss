using System.Text;

namespace DiffBench;

public sealed record DiffCase(int No, string Name, string Description, string Old, string New);

/// <summary>
/// 비교 테스트 20가지. 고정 씨앗(seed)으로 만들어 매번 같은 내용이다. `--write-cases &lt;폴더&gt;`로 파일로도 내보낸다.
/// </summary>
public static class Cases
{
    private static readonly string[] Words =
        { "alpha", "beta", "gamma", "delta", "value", "count", "index", "result", "buffer", "stream", "item", "name", "path", "state" };

    private static readonly string[] Korean =
        { "폴더", "비교", "파일", "결과", "설정", "작업", "이력", "되돌리기", "휴지통", "차이", "동기화", "경로", "선택", "패널" };

    public static List<DiffCase> All()
    {
        var list = new List<DiffCase>();
        void Add(string name, string description, List<string> oldLines, List<string> newLines)
            => list.Add(new DiffCase(list.Count + 1, name, description, Join(oldLines), Join(newLines)));

        var code1k = CodeLines(1_000, 1);
        var code10k = CodeLines(10_000, 2);
        var code100k = CodeLines(100_000, 3);

        Add("identical-100", "같은 파일 100줄", CodeLines(100, 4), CodeLines(100, 4));
        Add("identical-100k", "같은 파일 10만 줄", code100k, new List<string>(code100k));
        Add("one-line-1k", "1천 줄 중 가운데 1줄 수정", code1k, Edit(code1k, l => l[500] = l[500] + " // 수정"));
        Add("one-line-100k", "10만 줄 중 가운데 1줄 수정", code100k, Edit(code100k, l => l[50_000] = l[50_000] + " // 수정"));
        Add("append-10k", "1만 줄 끝에 100줄 추가", code10k, Edit(code10k, l => l.AddRange(CodeLines(100, 5))));
        Add("prepend-10k", "1만 줄 앞에 100줄 추가", code10k, Edit(code10k, l => l.InsertRange(0, CodeLines(100, 6))));
        Add("delete-block-10k", "1만 줄 가운데 500줄 삭제", code10k, Edit(code10k, l => l.RemoveRange(5_000, 500)));
        Add("insert-block-10k", "1만 줄 가운데 500줄 삽입", code10k, Edit(code10k, l => l.InsertRange(5_000, CodeLines(500, 7))));
        Add("scattered-1pct-10k", "1만 줄 곳곳 1% 수정", code10k, Scatter(code10k, 0.01, 8));
        Add("scattered-10pct-10k", "1만 줄 곳곳 10% 수정", code10k, Scatter(code10k, 0.10, 9));
        Add("scattered-50pct-10k", "1만 줄 곳곳 50% 수정", code10k, Scatter(code10k, 0.50, 10));
        Add("all-different-1k", "1천 줄 전부 다름(최악)", code1k, CodeLines(1_000, 11));
        Add("all-different-10k", "1만 줄 전부 다름(최악)", code10k, CodeLines(10_000, 12));
        Add("moved-block-10k", "1만 줄 중 500줄 덩어리를 뒤로 옮김", code10k, Edit(code10k, l =>
        {
            var block = l.GetRange(2_000, 500);
            l.RemoveRange(2_000, 500);
            l.InsertRange(7_500, block);
        }));
        Add("shuffled-2k", "2천 줄 순서를 뒤섞음", CodeLines(2_000, 13), Shuffle(CodeLines(2_000, 13), 14));
        var dup = DuplicateHeavy(10_000, 15);
        Add("dup-heavy-10k", "중괄호·빈 줄이 많은 코드 1만 줄, 2% 수정", dup, Scatter(dup, 0.02, 16));
        var longLines = LongLines(1_000, 2_000, 17);
        Add("long-lines-1k", "한 줄 2천 자 × 1천 줄, 1% 수정", longLines, Scatter(longLines, 0.01, 18));
        var lf = CodeLines(5_000, 19);
        Add("crlf-vs-lf-5k", "줄 끝만 LF → CRLF", lf, lf.Select(line => line + "\r").ToList());
        Add("indent-only-5k", "5천 줄 중 10% 들여쓰기만 바꿈", lf, Edit(lf, l =>
        {
            var random = new Random(20);
            for (var i = 0; i < l.Count; i++)
                if (random.NextDouble() < 0.10)
                    l[i] = "\t" + l[i].TrimStart();
        }));
        var korean = KoreanLines(5_000, 21);
        Add("korean-5k", "한글 5천 줄, 5% 수정", korean, Scatter(korean, 0.05, 22, KoreanLine));
        return list;
    }

    public static void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var utf8 = new UTF8Encoding(false);
        foreach (var c in All())
        {
            File.WriteAllText(Path.Combine(directory, $"{c.No:00}-{c.Name}.old.txt"), c.Old, utf8);
            File.WriteAllText(Path.Combine(directory, $"{c.No:00}-{c.Name}.new.txt"), c.New, utf8);
        }
    }

    private static string Join(List<string> lines) => string.Join("\n", lines) + "\n";

    private static List<string> Edit(List<string> source, Action<List<string>> change)
    {
        var copy = new List<string>(source);
        change(copy);
        return copy;
    }

    private static string CodeLine(Random random)
    {
        var w1 = Words[random.Next(Words.Length)];
        var w2 = Words[random.Next(Words.Length)];
        return $"        var {w1}{random.Next(100_000)} = Compute({w2}, {random.Next(1_000_000)});";
    }

    private static string KoreanLine(Random random)
    {
        var sb = new StringBuilder("    // ");
        for (var i = 0; i < 6; i++)
            sb.Append(Korean[random.Next(Korean.Length)]).Append(' ');
        return sb.Append(random.Next(1_000_000)).ToString();
    }

    private static List<string> CodeLines(int count, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(_ => CodeLine(random)).ToList();
    }

    private static List<string> KoreanLines(int count, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(_ => KoreanLine(random)).ToList();
    }

    private static List<string> Scatter(List<string> source, double rate, int seed, Func<Random, string> make = null)
    {
        var random = new Random(seed);
        make ??= CodeLine;
        return source.Select(line => random.NextDouble() < rate ? make(random) : line).ToList();
    }

    private static List<string> Shuffle(List<string> source, int seed)
    {
        var random = new Random(seed);
        var copy = new List<string>(source);
        for (var i = copy.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }
        return copy;
    }

    /// <summary>메서드 블록(`{`, `}`, 빈 줄, `return;`)이 반복되는 코드 — 흔한 줄이 많아 짝짓기가 어긋나기 쉽다.</summary>
    private static List<string> DuplicateHeavy(int count, int seed)
    {
        var random = new Random(seed);
        var lines = new List<string>();
        while (lines.Count < count)
        {
            lines.Add($"    public void Method{lines.Count}()");
            lines.Add("    {");
            for (var i = random.Next(1, 4); i > 0; i--)
                lines.Add(CodeLine(random));
            lines.Add("        return;");
            lines.Add("    }");
            lines.Add("");
        }
        return lines.GetRange(0, count);
    }

    private static List<string> LongLines(int count, int width, int seed)
    {
        var random = new Random(seed);
        return Enumerable.Range(0, count).Select(_ =>
        {
            var sb = new StringBuilder(width);
            while (sb.Length < width)
                sb.Append(Words[random.Next(Words.Length)]).Append(',');
            return sb.ToString(0, width);
        }).ToList();
    }
}
