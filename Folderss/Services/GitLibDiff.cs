using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using LibGit2Sharp;

namespace Folderss.Services
{
    /// <summary>
    /// Git 창·두 파일 비교의 diff를 git 프로세스 없이 libgit2의 xdiff(git.exe와 같은 diff 엔진)로 만든다.
    /// 회사 PC처럼 프로세스 실행이 1초 넘게 걸리는 환경에서 파일·커밋을 고를 때마다 기다리던 문제용(docs/items/git-diff-libgit2.md).
    /// - 결과는 같은 요청을 <see cref="GitDiffCommands"/> 인수로 돌린 git CLI 출력과 같은 unified 텍스트다(GitLibDiffTests가 비교).
    /// - LibGit2Sharp의 diff API는 내용을 UTF-8로만 풀어(잘못된 바이트는 대체 문자) CP949 대체 해석(<see cref="GitTextDecoder"/>)을 못 하므로,
    ///   LibGit2Sharp가 배포하는 같은 네이티브 DLL의 diff 함수를 직접 불러 바이트로 받는다. 리비전 해석·사전 검사는 LibGit2Sharp로 한다.
    /// - 같게 만들 수 없는 경우는 <see cref="NotSupportedException"/>. 호출자는 git CLI로 다시 읽는다:
    ///   충돌 파일(CLI는 combined diff), <see cref="GitLibReader"/> status와 같은 조건(서브모듈·sparse·인덱스 v3+·filter=), diff= 속성(textconv),
    ///   diff.algorithm=histogram, 네이티브 옵션 구조가 예상과 다를 때.
    /// - 두 파일 비교는 git과 달리 core.autocrlf로 줄 끝을 바꾸지 않는다. 줄 끝만 다른 파일도 차이로 보인다(공백 무시면 같게 본다).
    /// </summary>
    public static class GitLibDiff
    {
        /// <summary>LibGit2Sharp가 배포하는 네이티브 DLL 이름("git2-" + libgit2 커밋). LibGit2Sharp를 올리면 바뀐다(테스트가 확인).</summary>
        public const string NativeLibraryName = "git2-5853918";

        // libgit2 diff 플래그(git2/diff.h)
        private const uint DiffIncludeUntracked = 1u << 3;
        private const uint DiffRecurseUntrackedDirs = 1u << 4;
        private const uint DiffDisablePathspecMatch = 1u << 12;
        private const uint DiffIndentHeuristic = 1u << 18;
        private const uint DiffIgnoreWhitespace = 1u << 22;
        private const uint DiffShowUntrackedContent = 1u << 25;
        private const uint DiffPatience = 1u << 28;
        private const uint DiffMinimal = 1u << 29;
        private const uint FindRenames = 1u << 0;
        private const int FormatPatch = 1;

        /// <summary>
        /// <paramref name="request"/>와 같은 diff 텍스트. 두 파일 비교(<see cref="GitDiffCommands.Files"/>)는 <paramref name="root"/>를 쓰지 않는다.
        /// </summary>
        public static string Read(string root, GitDiffRequest request, GitDiffViewMode mode, Encoding fallbackEncoding)
        {
            if (request.NoIndex && request.OldSide == GitDiffCommands.WorkTreeSide)
                return ReadFiles(request, mode, fallbackEncoding);

            using (var repo = new Repository(root))
            {
                EnsureSupported(repo, request);
                var settings = DiffSettings.From(repo.Config, mode);
                var flags = settings.Flags | (request.IgnoreWhitespace ? DiffIgnoreWhitespace : 0);
                if (request.Paths != null)
                    flags |= DiffDisablePathspecMatch;

                // 변경 전/후 출처: 인덱스(":"), 작업 트리, 리비전, null(빈 쪽). 리비전은 트리 SHA로 바꿔 둔다.
                var oldSide = request.OldSide;
                var newSide = request.NewSide;
                var untracked = request.NoIndex && oldSide == null && newSide == GitDiffCommands.WorkTreeSide;
                var indexToWorkTree = untracked || (oldSide == GitDiffCommands.IndexSide && newSide == GitDiffCommands.WorkTreeSide);
                if (untracked)
                    flags |= DiffIncludeUntracked | DiffRecurseUntrackedDirs | DiffShowUntrackedContent;   // /dev/null ↔ 파일 전체
                string oldTree = null, newTree = null;
                if (!indexToWorkTree)
                {
                    oldTree = request.OldSideMergeBaseOf != null ? MergeBaseTreeSha(repo, request.OldSideMergeBaseOf) : TreeSha(repo, oldSide);
                    if (newSide != GitDiffCommands.IndexSide && newSide != GitDiffCommands.WorkTreeSide)
                        newTree = TreeSha(repo, newSide);
                }

                using (var native = NativeRepository.Open(root))
                using (var options = new NativeDiffOptions(flags, settings.Context, settings.InterHunk, request.Paths))
                {
                    IntPtr oldTreeObject = native.Tree(oldTree), newTreeObject = native.Tree(newTree);
                    IntPtr diff;
                    if (indexToWorkTree)
                        Check(Native.git_diff_index_to_workdir(out diff, native.Handle, IntPtr.Zero, options.Handle));
                    else if (newSide == GitDiffCommands.IndexSide)
                        Check(Native.git_diff_tree_to_index(out diff, native.Handle, oldTreeObject, IntPtr.Zero, options.Handle));
                    else if (newSide == GitDiffCommands.WorkTreeSide)
                        Check(Native.git_diff_tree_to_workdir_with_index(out diff, native.Handle, oldTreeObject, options.Handle));
                    else
                        Check(Native.git_diff_tree_to_tree(out diff, native.Handle, oldTreeObject, newTreeObject, options.Handle));
                    try
                    {
                        FindRenamesIn(diff);
                        var buf = new GitBuf();
                        try
                        {
                            Check(Native.git_diff_to_buf(ref buf, diff, FormatPatch));
                            return GitTextDecoder.Decode(NormalizeHeaders(buf.ToArray()), fallbackEncoding);
                        }
                        finally
                        {
                            Native.git_buf_dispose(ref buf);
                        }
                    }
                    finally
                    {
                        Native.git_diff_free(diff);
                    }
                }
            }
        }

        /// <summary>폴더 패널에서 고른 임의의 두 파일(<c>git diff --no-index</c>). 저장소가 필요 없다.</summary>
        private static string ReadFiles(GitDiffRequest request, GitDiffViewMode mode, Encoding fallbackEncoding)
        {
            if (request.Paths == null || request.Paths.Count != 2)
                throw new NotSupportedException("두 파일 경로 없음");
            var settings = DiffSettings.From(Configuration.BuildFrom(null), mode);
            var flags = settings.Flags | (request.IgnoreWhitespace ? DiffIgnoreWhitespace : 0);
            var oldBytes = File.ReadAllBytes(request.Paths[0]);
            var newBytes = File.ReadAllBytes(request.Paths[1]);
            using (var options = new NativeDiffOptions(flags, settings.Context, settings.InterHunk, null))
            {
                // 빈 배열은 null 포인터로 넘어갈 수 있고, libgit2는 null을 "파일 없음"(추가·삭제)으로 본다. 빈 파일도 파일로 보이게 한다.
                Check(Native.git_patch_from_buffers(out var patch,
                    oldBytes.Length == 0 ? new byte[1] : oldBytes, (UIntPtr)oldBytes.Length, request.Paths[0],
                    newBytes.Length == 0 ? new byte[1] : newBytes, (UIntPtr)newBytes.Length, request.Paths[1], options.Handle));
                var buf = new GitBuf();
                try
                {
                    Check(Native.git_patch_to_buf(ref buf, patch));
                    return GitTextDecoder.Decode(NormalizeHeaders(buf.ToArray()), fallbackEncoding);
                }
                finally
                {
                    Native.git_buf_dispose(ref buf);
                    Native.git_patch_free(patch);
                }
            }
        }

        private static void EnsureSupported(Repository repo, GitDiffRequest request)
        {
            GitLibReader.EnsureStatusSupported(repo);
            var workDir = repo.Info.WorkingDirectory;
            foreach (var attributes in new[] { Path.Combine(workDir, ".gitattributes"), Path.Combine(repo.Info.Path, "info", "attributes") })
            {
                // diff=<드라이버>의 textconv·funcname은 사용자 설정에 따라 git과 결과가 달라질 수 있다.
                if (File.Exists(attributes) && File.ReadAllText(attributes).Contains("diff="))
                    throw new NotSupportedException("diff 속성(diff=)");
            }
            var touchesIndex = request.OldSide == GitDiffCommands.IndexSide || request.NewSide == GitDiffCommands.IndexSide
                               || request.NewSide == GitDiffCommands.WorkTreeSide;
            if (touchesIndex && !repo.Index.IsFullyMerged)
                throw new NotSupportedException("충돌 중");   // git은 충돌 파일을 combined diff로 보인다
        }

        /// <summary>리비전 → 트리 SHA. null이면 빈 쪽, 아직 커밋이 없는 HEAD도 빈 쪽(git diff --cached와 같음).</summary>
        private static string TreeSha(Repository repo, string revision)
        {
            if (revision == null)
                return null;
            if (revision == "HEAD" && repo.Info.IsHeadUnborn)
                return null;
            var target = repo.Lookup(revision) ?? throw new NotSupportedException("리비전 없음: " + revision);
            return target.Peel<Tree>().Sha;
        }

        private static string MergeBaseTreeSha(Repository repo, string[] revisions)
        {
            var one = repo.Lookup(revisions[0])?.Peel<Commit>() ?? throw new NotSupportedException("리비전 없음: " + revisions[0]);
            var two = repo.Lookup(revisions[1])?.Peel<Commit>() ?? throw new NotSupportedException("리비전 없음: " + revisions[1]);
            var mergeBase = repo.ObjectDatabase.FindMergeBase(one, two) ?? throw new NotSupportedException("공통 조상 없음");
            return mergeBase.Tree.Sha;
        }

        /// <summary><c>-M</c>(이름 변경 감지, 유사도 50%).</summary>
        private static void FindRenamesIn(IntPtr diff)
        {
            var options = Marshal.AllocHGlobal(128);
            try
            {
                Zero(options, 128);
                Check(Native.git_diff_find_options_init(options, 1));
                if (Marshal.ReadInt32(options, 0) != 1)
                    throw new NotSupportedException("libgit2 이름 변경 옵션 구조가 예상과 다름");
                Marshal.WriteInt32(options, 4, unchecked((int)FindRenames));
                Check(Native.git_diff_find_similar(diff, options));
            }
            finally
            {
                Marshal.FreeHGlobal(options);
            }
        }

        private static readonly byte[][] PathHeaderPrefixes = new[] { "rename from ", "rename to ", "copy from ", "copy to ", "Binary files " }
            .Select(Encoding.ASCII.GetBytes).ToArray();

        /// <summary>
        /// 헤더 줄의 경로 표기를 git CLI(<c>core.quotepath=false</c>, <see cref="GitCommandRunner"/>가 늘 붙임)와 같게 바꾼다.
        /// libgit2는 비ASCII 경로를 큰따옴표 + 8진수(\353…)로 감싸고, git은 한글 등을 그대로 두며 이름에 공백이 있으면 ---/+++ 줄 끝에 탭을 붙인다.
        /// hunk 안의 줄은 건드리지 않는다(첫 @@부터 다음 diff 줄 전까지).
        /// </summary>
        internal static byte[] NormalizeHeaders(byte[] patch)
        {
            var output = new MemoryStream(patch.Length + 64);
            var inHeader = false;
            var start = 0;
            while (start < patch.Length)
            {
                var end = Array.IndexOf(patch, (byte)'\n', start);
                var length = (end < 0 ? patch.Length : end) - start;
                var line = new ArraySegment<byte>(patch, start, length);
                if (StartsWith(line, "diff --git "))
                {
                    inHeader = true;
                    WriteRequoted(output, line);
                }
                else if (inHeader && StartsWith(line, "@@"))
                {
                    inHeader = false;
                    output.Write(line.Array, line.Offset, line.Count);
                }
                else if (inHeader && (StartsWith(line, "--- ") || StartsWith(line, "+++ ")))
                {
                    output.Write(line.Array, line.Offset, 4);
                    var name = line.Slice(4);
                    WriteRequoted(output, name);
                    if (Array.IndexOf(Unquote(name), (byte)' ') >= 0)
                        output.WriteByte((byte)'\t');
                }
                else if (inHeader && PathHeaderPrefixes.Any(prefix => StartsWith(line, prefix)))
                {
                    WriteRequoted(output, line);
                }
                else
                {
                    output.Write(line.Array, line.Offset, line.Count);
                }
                if (end >= 0)
                    output.WriteByte((byte)'\n');
                start = end < 0 ? patch.Length : end + 1;
            }
            return output.ToArray();
        }

        private static bool StartsWith(ArraySegment<byte> line, string prefix) => StartsWith(line, Encoding.ASCII.GetBytes(prefix));

        private static bool StartsWith(ArraySegment<byte> line, byte[] prefix)
        {
            if (line.Count < prefix.Length)
                return false;
            for (var i = 0; i < prefix.Length; i++)
            {
                if (line.Array[line.Offset + i] != prefix[i])
                    return false;
            }
            return true;
        }

        /// <summary>줄 안의 큰따옴표 경로마다 풀어서 git 규칙으로 다시 감싼다. 따옴표 밖은 그대로.</summary>
        private static void WriteRequoted(MemoryStream output, ArraySegment<byte> line)
        {
            var i = 0;
            while (i < line.Count)
            {
                var b = line.Array[line.Offset + i];
                if (b != (byte)'"')
                {
                    output.WriteByte(b);
                    i++;
                    continue;
                }
                var close = i + 1;
                while (close < line.Count && line.Array[line.Offset + close] != (byte)'"')
                    close += line.Array[line.Offset + close] == (byte)'\\' ? 2 : 1;
                if (close >= line.Count)
                {
                    output.Write(line.Array, line.Offset + i, line.Count - i);   // 닫는 따옴표가 없으면 손대지 않는다
                    return;
                }
                var quoted = line.Slice(i, close - i + 1);
                var raw = Unquote(quoted);
                var requoted = QuoteLikeGit(raw);
                output.Write(requoted, 0, requoted.Length);
                i = close + 1;
            }
        }

        /// <summary>C 스타일 따옴표(<c>"a/\353\221\220 \\x"</c>)를 원래 바이트로. 따옴표가 아니면 그대로.</summary>
        private static byte[] Unquote(ArraySegment<byte> text)
        {
            if (text.Count < 2 || text.Array[text.Offset] != (byte)'"' || text.Array[text.Offset + text.Count - 1] != (byte)'"')
                return text.ToArray();
            var bytes = new List<byte>(text.Count);
            for (var i = 1; i < text.Count - 1; i++)
            {
                var b = text.Array[text.Offset + i];
                if (b != (byte)'\\' || i + 1 >= text.Count - 1)
                {
                    bytes.Add(b);
                    continue;
                }
                var next = text.Array[text.Offset + ++i];
                if (next >= (byte)'0' && next <= (byte)'7' && i + 2 < text.Count - 1)
                {
                    bytes.Add((byte)(((next - '0') << 6) | ((text.Array[text.Offset + i + 1] - '0') << 3) | (text.Array[text.Offset + i + 2] - '0')));
                    i += 2;
                    continue;
                }
                switch (next)
                {
                    case (byte)'a': bytes.Add(7); break;
                    case (byte)'b': bytes.Add(8); break;
                    case (byte)'t': bytes.Add(9); break;
                    case (byte)'n': bytes.Add(10); break;
                    case (byte)'v': bytes.Add(11); break;
                    case (byte)'f': bytes.Add(12); break;
                    case (byte)'r': bytes.Add(13); break;
                    default: bytes.Add(next); break;   // \\ \"
                }
            }
            return bytes.ToArray();
        }

        /// <summary>git quote_c_style(core.quotepath=false): 제어 문자·DEL·큰따옴표·역슬래시가 있을 때만 감싸고, 0x80 이상(한글 등)은 그대로 둔다.</summary>
        private static byte[] QuoteLikeGit(byte[] raw)
        {
            if (!raw.Any(b => b < 0x20 || b == 0x7F || b == (byte)'"' || b == (byte)'\\'))
                return raw;
            var output = new List<byte>(raw.Length + 8) { (byte)'"' };
            foreach (var b in raw)
            {
                switch (b)
                {
                    case 7: output.AddRange(new[] { (byte)'\\', (byte)'a' }); break;
                    case 8: output.AddRange(new[] { (byte)'\\', (byte)'b' }); break;
                    case 9: output.AddRange(new[] { (byte)'\\', (byte)'t' }); break;
                    case 10: output.AddRange(new[] { (byte)'\\', (byte)'n' }); break;
                    case 11: output.AddRange(new[] { (byte)'\\', (byte)'v' }); break;
                    case 12: output.AddRange(new[] { (byte)'\\', (byte)'f' }); break;
                    case 13: output.AddRange(new[] { (byte)'\\', (byte)'r' }); break;
                    case (byte)'"':
                    case (byte)'\\':
                        output.Add((byte)'\\');
                        output.Add(b);
                        break;
                    default:
                        if (b < 0x20 || b == 0x7F)
                            output.AddRange(Encoding.ASCII.GetBytes("\\" + Convert.ToString(b, 8).PadLeft(3, '0')));
                        else
                            output.Add(b);
                        break;
                }
            }
            output.Add((byte)'"');
            return output.ToArray();
        }

        private static void Check(int code)
        {
            if (code >= 0)
                return;
            var error = Native.git_error_last();
            var message = error == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error));
            throw new InvalidOperationException("libgit2 diff 실패(" + code + "): " + (message ?? "알 수 없는 오류"));
        }

        private static void Zero(IntPtr memory, int length)
        {
            for (var i = 0; i < length; i += 4)
                Marshal.WriteInt32(memory, i, 0);
        }

        /// <summary>git CLI가 diff에 쓰는 사용자 설정 중 출력에 영향을 주는 것. 보기 모드의 문맥(-U)이 diff.context보다 우선한다.</summary>
        private sealed class DiffSettings
        {
            public uint Flags;
            public int Context;
            public int InterHunk;

            public static DiffSettings From(Configuration config, GitDiffViewMode mode)
            {
                var settings = new DiffSettings
                {
                    Context = mode == GitDiffViewMode.Context10 ? 10
                        : mode == GitDiffViewMode.FullFile ? GitDiffCommands.FullFileContext
                        : config?.Get<int>("diff.context")?.Value ?? 3,
                    InterHunk = config?.Get<int>("diff.interHunkContext")?.Value ?? 0
                };
                // git은 2.14부터 indent heuristic이 기본으로 켜져 있다(libgit2는 꺼짐).
                if (config?.Get<bool>("diff.indentHeuristic")?.Value ?? true)
                    settings.Flags |= DiffIndentHeuristic;
                switch ((config?.Get<string>("diff.algorithm")?.Value ?? "default").Trim().ToLowerInvariant())
                {
                    case "default":
                    case "myers":
                        break;
                    case "minimal":
                        settings.Flags |= DiffMinimal;
                        break;
                    case "patience":
                        settings.Flags |= DiffPatience;
                        break;
                    default:
                        throw new NotSupportedException("diff.algorithm");   // histogram 등
                }
                return settings;
            }
        }

        /// <summary>
        /// <c>git_diff_options</c>. 구조체 전체를 C#으로 옮기지 않고 libgit2가 기본값을 채운 메모리에서 앞쪽 필드만 고친다.
        /// 앞쪽 배치(version, flags, ignore_submodules, pathspec, 콜백 3개, context_lines, interhunk_lines)는 libgit2 1.x에서 같고,
        /// 기본값(version 1, ignore_submodules -1, context_lines 3)으로 자리를 확인해 다르면 쓰지 않는다.
        /// </summary>
        private sealed class NativeDiffOptions : IDisposable
        {
            private const int Size = 512;
            private static readonly int PathspecOffset = IntPtr.Size == 8 ? 16 : 12;
            private static readonly int ContextOffset = PathspecOffset + 5 * IntPtr.Size;
            private readonly List<IntPtr> _strings = new List<IntPtr>();
            private IntPtr _array;

            public IntPtr Handle { get; }

            public NativeDiffOptions(uint flags, int context, int interHunk, IReadOnlyList<string> paths)
            {
                Handle = Marshal.AllocHGlobal(Size);
                try
                {
                    Zero(Handle, Size);
                    Check(Native.git_diff_options_init(Handle, 1));
                    if (Marshal.ReadInt32(Handle, 0) != 1 || Marshal.ReadInt32(Handle, 8) != -1 || Marshal.ReadInt32(Handle, ContextOffset) != 3)
                        throw new NotSupportedException("libgit2 diff 옵션 구조가 예상과 다름");
                    Marshal.WriteInt32(Handle, 4, unchecked((int)flags));
                    Marshal.WriteInt32(Handle, ContextOffset, context);
                    Marshal.WriteInt32(Handle, ContextOffset + 4, interHunk);
                    if (paths != null && paths.Count > 0)
                    {
                        _array = Marshal.AllocHGlobal(IntPtr.Size * paths.Count);
                        for (var i = 0; i < paths.Count; i++)
                        {
                            var text = Marshal.StringToCoTaskMemUTF8(paths[i]);
                            _strings.Add(text);
                            Marshal.WriteIntPtr(_array, i * IntPtr.Size, text);
                        }
                        Marshal.WriteIntPtr(Handle, PathspecOffset, _array);
                        Marshal.WriteIntPtr(Handle, PathspecOffset + IntPtr.Size, (IntPtr)paths.Count);
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                foreach (var text in _strings)
                    Marshal.FreeCoTaskMem(text);
                _strings.Clear();
                if (_array != IntPtr.Zero)
                    Marshal.FreeHGlobal(_array);
                _array = IntPtr.Zero;
                Marshal.FreeHGlobal(Handle);
            }
        }

        /// <summary>네이티브 저장소 핸들과, 이 diff에 쓸 트리 객체들(함께 해제).</summary>
        private sealed class NativeRepository : IDisposable
        {
            private readonly List<IntPtr> _objects = new List<IntPtr>();

            public IntPtr Handle { get; private set; }

            public static NativeRepository Open(string root)
            {
                Check(Native.git_repository_open(out var handle, root));
                return new NativeRepository { Handle = handle };
            }

            /// <summary>트리 SHA → 트리 객체. null이면 빈 트리(IntPtr.Zero).</summary>
            public IntPtr Tree(string sha)
            {
                if (sha == null)
                    return IntPtr.Zero;
                Check(Native.git_revparse_single(out var tree, Handle, sha));
                _objects.Add(tree);
                return tree;
            }

            public void Dispose()
            {
                foreach (var item in _objects)
                    Native.git_object_free(item);
                _objects.Clear();
                if (Handle != IntPtr.Zero)
                    Native.git_repository_free(Handle);
                Handle = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GitBuf
        {
            public IntPtr Ptr;
            public UIntPtr Reserved;
            public UIntPtr Size;

            public byte[] ToArray()
            {
                var bytes = new byte[checked((int)Size)];
                if (bytes.Length > 0)
                    Marshal.Copy(Ptr, bytes, 0, bytes.Length);
                return bytes;
            }
        }

        private static class Native
        {
            // 참조 횟수 방식이라 LibGit2Sharp가 이미 초기화했어도 한 번 더 불러도 된다(전역 할당자 준비).
            static Native() => git_libgit2_init();

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            private static extern int git_libgit2_init();

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr git_error_last();

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_repository_open(out IntPtr repository, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void git_repository_free(IntPtr repository);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_revparse_single(out IntPtr result, IntPtr repository, [MarshalAs(UnmanagedType.LPUTF8Str)] string spec);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void git_object_free(IntPtr item);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_diff_options_init(IntPtr options, uint version);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_diff_find_options_init(IntPtr options, uint version);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_diff_index_to_workdir(out IntPtr diff, IntPtr repository, IntPtr index, IntPtr options);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_diff_tree_to_index(out IntPtr diff, IntPtr repository, IntPtr oldTree, IntPtr index, IntPtr options);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_diff_tree_to_tree(out IntPtr diff, IntPtr repository, IntPtr oldTree, IntPtr newTree, IntPtr options);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_diff_tree_to_workdir_with_index(out IntPtr diff, IntPtr repository, IntPtr oldTree, IntPtr options);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_diff_find_similar(IntPtr diff, IntPtr options);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_diff_to_buf(ref GitBuf buf, IntPtr diff, int format);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void git_diff_free(IntPtr diff);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_patch_from_buffers(out IntPtr patch,
                byte[] oldBuffer, UIntPtr oldLength, [MarshalAs(UnmanagedType.LPUTF8Str)] string oldAsPath,
                byte[] newBuffer, UIntPtr newLength, [MarshalAs(UnmanagedType.LPUTF8Str)] string newAsPath,
                IntPtr options);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern int git_patch_to_buf(ref GitBuf buf, IntPtr patch);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void git_patch_free(IntPtr patch);

            [DllImport(NativeLibraryName, CallingConvention = CallingConvention.Cdecl)]
            public static extern void git_buf_dispose(ref GitBuf buf);
        }
    }
}
