using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DiffBench;

// 사용법 (tools/DiffBench 폴더에서):
//   dotnet run -c Release                       — 20가지 경우 × 6가지 방식 측정, 결과를 results.md로
//   dotnet run -c Release -- --runs 3 --out r.md
//   dotnet run -c Release -- --write-cases cases — 테스트 파일만 내보내기
//   dotnet run -c Release -- --selftest          — 직접 구현의 정확성·최소성 무작위 검사
var runs = 5;
var budget = TimeSpan.FromSeconds(3);
var outPath = "results.md";
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--runs": runs = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "--out": outPath = args[++i]; break;
        case "--selftest":
            SelfTest.Run();
            return;
        case "--write-cases":
            Cases.Write(args[++i]);
            Console.WriteLine("테스트 파일을 썼습니다: " + Path.GetFullPath(args[i]));
            return;
    }
}

var cases = Cases.All();
using var libgit2 = new LibGit2Engine();
var engines = new IDiffEngine[]
{
    new GitCliEngine(),
    libgit2,
    new DiffPlexEngine(),
    new DiffMatchPatchEngine(),
    new OwnEngine("직접 Myers", MyersDiff.Diff),
    new OwnEngine("직접 Histogram", HistogramDiff.Diff)
};

var work = Path.Combine(Path.GetTempPath(), "diffbench-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
var cells = new Dictionary<(int, string), Cell>();
try
{
    foreach (var c in cases)
    {
        var oldPath = Path.Combine(work, $"{c.No:00}.old.txt");
        var newPath = Path.Combine(work, $"{c.No:00}.new.txt");
        File.WriteAllText(oldPath, c.Old, new UTF8Encoding(false));
        File.WriteAllText(newPath, c.New, new UTF8Encoding(false));
        foreach (var engine in engines)
        {
            var cell = Measure(engine, oldPath, newPath, runs, budget);
            cells[(c.No, engine.Name)] = cell;
            Console.WriteLine($"{c.No,2} {c.Name,-22} {engine.Name,-16} {cell.Display,12}  변경 {cell.ChangedText}");
        }
    }
}
finally
{
    try { Directory.Delete(work, true); } catch { /* 임시 폴더 */ }
}

var report = Report(cases, engines, cells, runs);
File.WriteAllText(outPath, report, new UTF8Encoding(false));
Console.WriteLine();
Console.WriteLine("결과: " + Path.GetFullPath(outPath));

static Cell Measure(IDiffEngine engine, string oldPath, string newPath, int runs, TimeSpan budget)
{
    EngineResult result;
    var first = Stopwatch.StartNew();
    try
    {
        result = engine.Run(oldPath, newPath);   // 첫 실행: JIT·검증 포함, 결과 확인용
    }
    catch (Exception ex) when (ex is NotSupportedException || ex is InvalidOperationException || ex is OutOfMemoryException)
    {
        return new Cell { Error = ex.Message };
    }
    first.Stop();

    var times = new List<double>();
    long allocated = -1;
    var total = Stopwatch.StartNew();
    for (var r = 0; r < runs && (r == 0 || total.Elapsed < budget); r++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        engine.Run(oldPath, newPath);
        sw.Stop();
        if (engine.InProcess && allocated < 0)
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        times.Add(sw.Elapsed.TotalMilliseconds);
    }
    times.Sort();
    return new Cell
    {
        MedianMs = times[times.Count / 2],
        Samples = times.Count,
        AllocatedBytes = allocated,
        Changed = result.Changed,
        Verified = result.Verified,
        Note = result.Note
    };
}

static string Report(List<DiffCase> cases, IDiffEngine[] engines, Dictionary<(int, string), Cell> cells, int runs)
{
    var sb = new StringBuilder();
    sb.AppendLine("# diff 방식 비교 결과");
    sb.AppendLine();
    sb.AppendLine($"- 측정 시각: {DateTime.Now:yyyy-MM-dd HH:mm}");
    sb.AppendLine($"- 환경: {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, 논리 CPU {Environment.ProcessorCount}, {RuntimeInformation.FrameworkDescription}, {GitVersion()}");
    sb.AppendLine($"- 방법: 경우마다 두 파일을 디스크에 써 두고, 방식마다 첫 실행(JIT·결과 검증) 뒤 최대 {runs}회(3초 넘으면 중단) 측정한 **중앙값**. 파일 읽기 포함.");
    sb.AppendLine("- 변경 줄 = 삭제 줄 + 추가 줄(작을수록 최소 diff에 가까움). ✓ = 결과로 옛 파일을 새 파일로 정확히 재구성됨을 확인.");
    sb.AppendLine("- 메모리 = 1회 실행의 관리 힙 할당량. git CLI는 별도 프로세스라, LibGit2Sharp는 네이티브 메모리라 대부분 잡히지 않는다.");
    sb.AppendLine();

    sb.AppendLine("## 시간 (ms, 중앙값)");
    sb.AppendLine();
    Header(sb, engines);
    foreach (var c in cases)
        Row(sb, c, engines, e => cells[(c.No, e.Name)].Display);
    sb.AppendLine();

    sb.AppendLine("## 변경 줄 수 (결과 품질)");
    sb.AppendLine();
    Header(sb, engines);
    foreach (var c in cases)
        Row(sb, c, engines, e => cells[(c.No, e.Name)].ChangedText);
    sb.AppendLine();

    sb.AppendLine("## 관리 힙 할당 (MB, 1회)");
    sb.AppendLine();
    Header(sb, engines);
    foreach (var c in cases)
        Row(sb, c, engines, e =>
        {
            var cell = cells[(c.No, e.Name)];
            return cell.Error != null || cell.AllocatedBytes < 0 ? "—" : (cell.AllocatedBytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture);
        });
    sb.AppendLine();

    sb.AppendLine("## 요약");
    sb.AppendLine();
    sb.AppendLine("| 방식 | 종류 | 20건 합계 (ms) | git CLI 대비 (기하평균) | 가장 빠름 횟수 | 실패·지원 안 함 |");
    sb.AppendLine("|---|---|---:|---:|---:|---:|");
    var baseline = engines[0].Name;
    foreach (var e in engines)
    {
        var ok = cases.Where(c => cells[(c.No, e.Name)].Error == null).ToList();
        var sum = ok.Sum(c => cells[(c.No, e.Name)].MedianMs);
        var both = ok.Where(c => cells[(c.No, baseline)].Error == null).ToList();
        var geo = both.Count == 0 ? double.NaN
            : Math.Exp(both.Average(c => Math.Log(cells[(c.No, baseline)].MedianMs / cells[(c.No, e.Name)].MedianMs)));
        var wins = cases.Count(c => engines.Where(x => cells[(c.No, x.Name)].Error == null)
            .OrderBy(x => cells[(c.No, x.Name)].MedianMs).FirstOrDefault()?.Name == e.Name);
        sb.AppendLine($"| {e.Name} | {e.Kind} | {sum:N1} | {geo:0.0}배 | {wins} | {cases.Count - ok.Count} |");
    }
    sb.AppendLine();
    sb.AppendLine("오류·지원 안 함:");
    sb.AppendLine();
    foreach (var c in cases)
        foreach (var e in engines)
            if (cells[(c.No, e.Name)].Error is string error)
                sb.AppendLine($"- {c.No} {c.Name} / {e.Name}: {error}");
    return sb.ToString();
}

static void Header(StringBuilder sb, IDiffEngine[] engines)
{
    sb.AppendLine("| # | 경우 | " + string.Join(" | ", engines.Select(e => e.Name)) + " |");
    sb.AppendLine("|---:|---|" + string.Concat(engines.Select(_ => "---:|")));
}

static void Row(StringBuilder sb, DiffCase c, IDiffEngine[] engines, Func<IDiffEngine, string> value)
    => sb.AppendLine($"| {c.No} | {c.Name}<br>{c.Description} | " + string.Join(" | ", engines.Select(value)) + " |");

static string GitVersion()
{
    try
    {
        var p = Process.Start(new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
        var v = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return v;
    }
    catch (Exception)
    {
        return "git 없음";
    }
}

sealed class Cell
{
    public double MedianMs { get; init; }
    public int Samples { get; init; }
    public long AllocatedBytes { get; init; } = -1;
    public int Changed { get; init; }
    public bool Verified { get; init; }
    public string Note { get; init; }
    public string Error { get; init; }

    public string Display => Error != null ? "지원 안 함" : MedianMs.ToString(MedianMs < 10 ? "0.00" : "0", CultureInfo.InvariantCulture);
    public string ChangedText => Error != null ? "—" : Changed.ToString("N0", CultureInfo.InvariantCulture) + (Verified ? " ✓" : "") + (Note != null ? " (" + Note + ")" : "");
}
