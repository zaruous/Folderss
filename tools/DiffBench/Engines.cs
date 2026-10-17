using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LibGit2Sharp;

namespace DiffBench;

/// <summary>한 번 실행 결과. Changed = 삭제 줄 + 추가 줄(작을수록 최소에 가까움). Verified = 결과로 옛 파일을 새 파일로 정확히 바꿀 수 있음을 확인했는가.</summary>
public sealed record EngineResult(int Changed, bool Verified, string Note = null);

/// <summary>diff 방식 하나. 입력은 이미 디스크에 있는 두 파일(앱의 두 파일 비교와 같은 조건) — 파일 읽기도 측정에 들어간다.</summary>
public interface IDiffEngine
{
    string Name { get; }
    string Kind { get; }
    /// <summary>같은 프로세스 안에서 도는가(메모리 할당을 잴 수 있음).</summary>
    bool InProcess { get; }
    EngineResult Run(string oldPath, string newPath);
}

/// <summary>현재 상태: 앱과 같은 인수로 git.exe를 실행한다(`git diff --no-index`, 기본 문맥 3줄).</summary>
public sealed class GitCliEngine : IDiffEngine
{
    public string Name => "git CLI (현재)";
    public string Kind => "외부 프로세스";
    public bool InProcess => false;

    public EngineResult Run(string oldPath, string newPath)
    {
        var start = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false)
        };
        foreach (var arg in new[] { "-c", "core.quotepath=false", "-c", "color.ui=false", "diff", "--no-index",
                     "--no-color", "--no-ext-diff", "-M", "--src-prefix=a/", "--dst-prefix=b/", "--", oldPath, newPath })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode > 1)
            throw new InvalidOperationException("git 실패: " + stderr.Result);
        return new EngineResult(CountUnified(output), false);
    }

    /// <summary>unified diff에서 hunk 안의 +/- 줄 수.</summary>
    public static int CountUnified(string patch)
    {
        var count = 0;
        var inHunk = false;
        foreach (var line in patch.Split('\n'))
        {
            if (line.StartsWith("@@", StringComparison.Ordinal)) { inHunk = true; continue; }
            if (line.StartsWith("diff ", StringComparison.Ordinal)) { inHunk = false; continue; }
            if (inHunk && line.Length > 0 && (line[0] == '+' || line[0] == '-'))
                count++;
        }
        return count;
    }
}

/// <summary>LibGit2Sharp(이미 앱 의존성, 네이티브 libgit2): 임시 bare 저장소에 두 파일을 blob으로 넣고 비교한다.</summary>
public sealed class LibGit2Engine : IDiffEngine, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "diffbench-repo-" + Guid.NewGuid().ToString("N"));
    private readonly Repository _repo;

    public LibGit2Engine()
    {
        Repository.Init(_root, isBare: true);
        _repo = new Repository(_root);
    }

    public string Name => "LibGit2Sharp";
    public string Kind => "라이브러리(네이티브, 기존 의존성)";
    public bool InProcess => true;

    public EngineResult Run(string oldPath, string newPath)
    {
        Blob oldBlob, newBlob;
        using (var s = File.OpenRead(oldPath)) oldBlob = _repo.ObjectDatabase.CreateBlob(s);
        using (var s = File.OpenRead(newPath)) newBlob = _repo.ObjectDatabase.CreateBlob(s);
        var changes = _repo.Diff.Compare(oldBlob, newBlob, new CompareOptions { ContextLines = 3 });
        if (changes.IsBinaryComparison)
            return new EngineResult(0, false, "바이너리로 판정");
        return new EngineResult(changes.LinesAdded + changes.LinesDeleted, false);
    }

    public void Dispose()
    {
        _repo.Dispose();
        try { Directory.Delete(_root, true); } catch { /* 임시 폴더 — 남아도 무해 */ }
    }
}

/// <summary>
/// libgit2 버퍼 비교: LibGit2Sharp가 감싸지 않은 <c>git_patch_from_buffers</c>·<c>git_patch_to_buf</c>를 P/Invoke로 직접 부른다.
/// git.exe와 같은 xdiff 엔진을 프로세스 없이, 저장소·blob 쓰기 없이 메모리에서 돌리고 unified 텍스트를 받는다(앱 화면이 받는 형식).
/// 저장소 설정(core.autocrlf 등) 변환은 하지 않는다.
/// </summary>
public sealed class LibGit2BufferEngine : IDiffEngine
{
    // LibGit2Sharp 0.32(LibGit2Sharp.NativeBinaries 2.0.324)가 배포하는 네이티브 DLL 이름. 판이 바뀌면 이름도 바뀐다.
    private const string Lib = "git2-5853918";

    [StructLayout(LayoutKind.Sequential)]
    private struct GitBuf
    {
        public IntPtr Ptr;
        public UIntPtr Reserved;
        public UIntPtr Size;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int git_libgit2_init();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe int git_patch_from_buffers(out IntPtr patch,
        byte* oldBuffer, UIntPtr oldLen, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldAsPath,
        byte* newBuffer, UIntPtr newLen, [MarshalAs(UnmanagedType.LPUTF8Str)] string newAsPath,
        IntPtr options);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int git_patch_to_buf(ref GitBuf buf, IntPtr patch);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void git_patch_free(IntPtr patch);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void git_buf_dispose(ref GitBuf buf);

    // 참조 횟수 방식이라 LibGit2Sharp가 이미 초기화했어도 한 번 더 불러도 된다(전역 할당자 준비).
    static LibGit2BufferEngine() => git_libgit2_init();

    public string Name => "libgit2 버퍼";
    public string Kind => "라이브러리(네이티브 xdiff, 기존 의존성 직접 호출)";
    public bool InProcess => true;

    public unsafe EngineResult Run(string oldPath, string newPath)
    {
        var oldBytes = File.ReadAllBytes(oldPath);
        var newBytes = File.ReadAllBytes(newPath);
        IntPtr patch;
        fixed (byte* a = oldBytes)
        fixed (byte* b = newBytes)
        {
            if (git_patch_from_buffers(out patch, a, (UIntPtr)oldBytes.Length, "a", b, (UIntPtr)newBytes.Length, "b", IntPtr.Zero) != 0)
                throw new InvalidOperationException("git_patch_from_buffers 실패");
        }
        var buf = new GitBuf();
        try
        {
            if (git_patch_to_buf(ref buf, patch) != 0)
                throw new InvalidOperationException("git_patch_to_buf 실패");
            var text = buf.Ptr == IntPtr.Zero ? string.Empty : Encoding.UTF8.GetString((byte*)buf.Ptr, checked((int)buf.Size));
            return new EngineResult(GitCliEngine.CountUnified(text), false);
        }
        finally
        {
            git_buf_dispose(ref buf);
            git_patch_free(patch);
        }
    }
}

/// <summary>DiffPlex 1.9 (Myers). 줄 나누기는 '\n'만 — 기본 LineChunker는 CRLF와 LF를 같게 봐서 다른 방식과 결과가 달라진다.</summary>
public sealed class DiffPlexEngine : IDiffEngine
{
    private sealed class NewlineChunker : DiffPlex.IChunker
    {
        public IReadOnlyList<string> Chunk(string text) => LineDiff.Split(text);
    }

    private static readonly DiffPlex.IChunker Chunker = new NewlineChunker();

    public string Name => "DiffPlex";
    public string Kind => "라이브러리(관리 코드)";
    public bool InProcess => true;

    public EngineResult Run(string oldPath, string newPath)
    {
        var oldText = File.ReadAllText(oldPath);
        var newText = File.ReadAllText(newPath);
        var result = new DiffPlex.Differ().CreateDiffs(oldText, newText, false, false, Chunker);
        var blocks = result.DiffBlocks.Select(b => new Block(b.DeleteStartA, b.DeleteCountA, b.InsertStartB, b.InsertCountB)).ToList();
        return new EngineResult(LineDiff.Verify(LineDiff.Split(oldText), LineDiff.Split(newText), blocks), true);
    }
}

/// <summary>
/// Google diff-match-patch 3.0.0(.NET 8 대상 마지막 판). 줄 단위 공개 API가 없어 문서의 "줄 모드" 방법대로 줄마다 문자 하나로 바꿔 비교한다.
/// 문자 하나에 줄 하나를 담으므로 고유 줄이 약 6만 3천 개를 넘으면 쓸 수 없다. 기본 시간 제한(1초)은 그대로 둔다 — 넘으면 최소가 아닌 결과를 낸다.
/// </summary>
public sealed class DiffMatchPatchEngine : IDiffEngine
{
    public string Name => "diff-match-patch";
    public string Kind => "라이브러리(관리 코드)";
    public bool InProcess => true;

    public EngineResult Run(string oldPath, string newPath)
    {
        var a = LineDiff.Split(File.ReadAllText(oldPath));
        var b = LineDiff.Split(File.ReadAllText(newPath));
        var ids = new Dictionary<string, char>(StringComparer.Ordinal);
        string Encode(string[] lines)
        {
            var sb = new StringBuilder(lines.Length);
            foreach (var line in lines)
            {
                if (!ids.TryGetValue(line, out var c))
                {
                    var n = ids.Count + 1;
                    if (n >= 0xD800) n += 0x800;   // 서로게이트 영역은 건너뛴다
                    if (n > 0xFFFF)
                        throw new NotSupportedException("고유 줄 6만 3천 개 초과");
                    ids[line] = c = (char)n;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
        var diffs = new DiffMatchPatch.diff_match_patch().diff_main(Encode(a), Encode(b), false);

        var blocks = new List<Block>();
        int ai = 0, bi = 0;
        foreach (var d in diffs)
        {
            var n = d.text.Length;
            switch (d.operation)
            {
                case DiffMatchPatch.Operation.EQUAL: ai += n; bi += n; break;
                case DiffMatchPatch.Operation.DELETE: blocks.Add(new Block(ai, n, bi, 0)); ai += n; break;
                default: blocks.Add(new Block(ai, 0, bi, n)); bi += n; break;
            }
        }
        return new EngineResult(LineDiff.Verify(a, b, blocks), true);
    }
}

/// <summary>직접 구현: <see cref="MyersDiff"/> 또는 <see cref="HistogramDiff"/>.</summary>
public sealed class OwnEngine : IDiffEngine
{
    private readonly Func<int[], int[], List<Block>> _diff;

    public OwnEngine(string name, Func<int[], int[], List<Block>> diff)
    {
        Name = name;
        _diff = diff;
    }

    public string Name { get; }
    public string Kind => "직접 구현";
    public bool InProcess => true;

    public EngineResult Run(string oldPath, string newPath)
    {
        var a = LineDiff.Split(File.ReadAllText(oldPath));
        var b = LineDiff.Split(File.ReadAllText(newPath));
        var (ia, ib, _) = LineDiff.ToIds(a, b);
        return new EngineResult(LineDiff.Verify(a, b, _diff(ia, ib)), true);
    }
}
