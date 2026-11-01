using Folderss.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Folderss.Services
{
    /// <summary>
    /// <c>.md</c> 뷰어 탭의 블록 편집 ↔ 미리보기(Markdown 뷰어) 전환에서 보던 위치. 제목 앵커(<see cref="HeadingId"/>)가 1차,
    /// 원문 줄 번호(<see cref="Line"/>, 1부터, 0이면 모름)는 제목이 없거나 원문 편집 화면일 때 쓴다(#81).
    /// </summary>
    public sealed class MarkdownViewPosition
    {
        /// <summary>보던 위치 바로 위(또는 그 자리)의 제목 앵커 id(GitHub 규칙, 중복은 <c>-1</c>·<c>-2</c>). 없으면 null.</summary>
        public string HeadingId { get; set; }

        /// <summary>보던 위치의 원문 줄 번호(1부터). 모르면 0.</summary>
        public int Line { get; set; }

        /// <summary>줄 번호를 제목보다 먼저 쓴다(Markdown 뷰어 원문 편집 화면에서 잡은 위치).</summary>
        public bool PreferLine { get; set; }
    }

    /// <summary>
    /// 블록 편집 ↔ 미리보기 전환의 순수 로직(WPF 없음, 테스트 프로젝트에 소스 링크): 열 뷰어 고르기, 위치 대응(블록 ↔ 제목·줄 번호).
    /// </summary>
    public static class MarkdownViewSwitch
    {
        /// <summary>블록 편집기로 보기(<c>builtin:blocks</c>).</summary>
        public const string ViewBlocks = "blocks";

        /// <summary>미리보기 = Markdown 뷰어(<c>builtin:markdown</c>).</summary>
        public const string ViewPreview = "preview";

        /// <summary>설정 > 뷰어 "처음 열 때 기본 보기"의 선택지: 확장자 매핑을 따른다(<c>.md</c> 매핑 = Markdown 뷰어).</summary>
        public const string DefaultFollowMapping = "mapping";

        /// <summary>
        /// 처음 열 때 기본 보기를 저장한 적이 없을 때의 값(#81 3단계): 블록 편집. <c>markdown-view.json</c>에 <c>defaultView</c>가 저장돼 있으면 그 값을 쓴다
        /// (예전 기본값 <c>mapping</c>을 저장한 사용자는 그대로 Markdown 뷰어).
        /// </summary>
        public const string DefaultViewWhenUnset = ViewBlocks;

        /// <summary>전환 버튼을 보이는 확장자(<c>.md</c>·<c>.markdown</c>).</summary>
        public static bool IsMarkdownExtension(string extension)
        {
            return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>두 보기 중 하나인 뷰어 키(Markdown 뷰어·블록 편집기). 다른 뷰어(Monaco 등)로 매핑한 확장자는 전환·기억을 쓰지 않는다.</summary>
        public static bool IsSwitchableKey(string viewerKey)
        {
            return string.Equals(viewerKey, ViewerConfigService.BuiltInMarkdownKey, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(viewerKey, ViewerConfigService.BuiltInBlocksKey, StringComparison.OrdinalIgnoreCase);
        }

        public static string ToViewerKey(string view)
        {
            if (string.Equals(view, ViewBlocks, StringComparison.OrdinalIgnoreCase))
                return ViewerConfigService.BuiltInBlocksKey;
            if (string.Equals(view, ViewPreview, StringComparison.OrdinalIgnoreCase))
                return ViewerConfigService.BuiltInMarkdownKey;
            return null;
        }

        public static string ToView(string viewerKey)
        {
            if (string.Equals(viewerKey, ViewerConfigService.BuiltInBlocksKey, StringComparison.OrdinalIgnoreCase))
                return ViewBlocks;
            if (string.Equals(viewerKey, ViewerConfigService.BuiltInMarkdownKey, StringComparison.OrdinalIgnoreCase))
                return ViewPreview;
            return null;
        }

        /// <summary>
        /// 파일을 열 뷰어 키. <c>.md</c>·<c>.markdown</c>이고 매핑이 두 보기 중 하나일 때만 바꾼다:
        /// 파일별로 기억한 보기 → 설정의 처음 열 때 기본 보기 → 매핑 순서. 그 밖에는 매핑 그대로.
        /// </summary>
        public static string ChooseViewerKey(string extension, string mappingKey, string rememberedView, string defaultView)
        {
            return ChooseViewerKey(extension, mappingKey, rememberedView, defaultView, false);
        }

        /// <param name="blocksUnavailable">블록 편집기로 열 수 없는 파일(너무 큼 — <c>BlockEditorViewer.MaxFileBytes</c>)이면 true: 블록 편집으로 고른 경우 미리보기로 연다.</param>
        public static string ChooseViewerKey(string extension, string mappingKey, string rememberedView, string defaultView, bool blocksUnavailable)
        {
            if (!IsMarkdownExtension(extension) || !IsSwitchableKey(mappingKey))
                return mappingKey;
            var key = ToViewerKey(rememberedView) ?? ToViewerKey(defaultView) ?? mappingKey;
            if (blocksUnavailable && string.Equals(key, ViewerConfigService.BuiltInBlocksKey, StringComparison.OrdinalIgnoreCase))
                return ViewerConfigService.BuiltInMarkdownKey;
            return key;
        }

        // ── 위치 대응 ──────────────────────────────────────────────────────

        /// <summary>
        /// <paramref name="index"/> 블록 위(그 블록 포함)에서 가장 가까운 제목의 앵커 id. Markdown 뷰어(<c>markdown-app.js</c> <c>uniqueSlug</c>)·
        /// <see cref="BlockFind.FindHeading"/>와 같은 규칙(빈 제목은 <c>section</c>, 중복은 <c>-1</c>·<c>-2</c>). 위에 제목이 없으면 null.
        /// </summary>
        public static string HeadingIdAbove(IReadOnlyList<EditorBlock> blocks, int index)
        {
            if (blocks == null || index < 0)
                return null;
            var used = new HashSet<string>(StringComparer.Ordinal);
            string last = null;
            for (var i = 0; i < blocks.Count && i <= index; i++)
            {
                if (blocks[i].GetHeadingLevel() == 0)
                    continue;
                var slug = BlockFind.Slugify(blocks[i].Text);
                var id = slug.Length > 0 ? slug : "section";
                for (var n = 1; used.Contains(id); n++)
                    id = (slug.Length > 0 ? slug : "section") + "-" + n;
                used.Add(id);
                last = id;
            }
            return last;
        }

        /// <summary>
        /// <paramref name="index"/> 블록이 Markdown으로 쓴 글(<see cref="BlockMarkdown.Serialize"/>)에서 시작하는 줄 번호(1부터).
        /// 디스크 원문이 정리된 형식과 다르면(빈 줄 개수 등) 몇 줄 어긋날 수 있다 — 위치 대응은 근사값이면 충분하다.
        /// </summary>
        public static int LineOfBlock(IList<EditorBlock> blocks, int index)
        {
            if (blocks == null || blocks.Count == 0 || index <= 0)
                return 1;
            index = Math.Min(index, blocks.Count - 1);
            var before = BlockMarkdown.Serialize(blocks.Take(index).ToList());
            var through = BlockMarkdown.Serialize(blocks.Take(index + 1).ToList());
            // 앞 블록들만 쓴 글과 이 블록까지 쓴 글이 갈라지는 곳 다음의 첫 글자가 이 블록의 시작이다.
            var common = 0;
            var limit = Math.Min(before.Length, through.Length);
            while (common < limit && before[common] == through[common])
                common++;
            var position = common;
            while (position < through.Length && (through[position] == '\n' || through[position] == '\r'))
                position++;
            var line = 1;
            for (var i = 0; i < position && i < through.Length; i++)
            {
                if (through[i] == '\n')
                    line++;
            }
            return line;
        }

        /// <summary><paramref name="line"/>(1부터)을 담은 블록 — 시작 줄이 그 줄 이하인 마지막 블록. 블록이 없으면 -1.</summary>
        public static int BlockAtLine(IList<EditorBlock> blocks, int line)
        {
            if (blocks == null || blocks.Count == 0)
                return -1;
            if (line <= 1)
                return 0;
            // 시작 줄은 블록 순서대로 늘어나므로 이분 탐색(큰 문서도 Serialize 몇십 번).
            int low = 0, high = blocks.Count - 1;
            while (low < high)
            {
                var mid = (low + high + 1) / 2;
                if (LineOfBlock(blocks, mid) <= line)
                    low = mid;
                else
                    high = mid - 1;
            }
            return low;
        }

        /// <summary>
        /// 블록별 시작 줄 표(<see cref="MarkdownSaveResult.BlockLines"/> — 실제로 저장되는 글 기준)에서 <paramref name="line"/>을 담은 블록(이분 탐색). 표가 비었으면 -1.
        /// </summary>
        public static int BlockAtLine(IReadOnlyList<int> blockLines, int line)
        {
            if (blockLines == null || blockLines.Count == 0)
                return -1;
            if (line <= 1)
                return 0;
            int low = 0, high = blockLines.Count - 1;
            while (low < high)
            {
                var mid = (low + high + 1) / 2;
                if (blockLines[mid] <= line)
                    low = mid;
                else
                    high = mid - 1;
            }
            return low;
        }

        /// <summary>블록 편집기에서 잡은 위치: 화면 맨 위 블록의 제목 앵커와 줄 번호.</summary>
        /// <param name="blockLines">블록별 시작 줄(실제 저장 글 기준). null이면 정규형(<see cref="LineOfBlock"/>)으로 센다.</param>
        public static MarkdownViewPosition FromBlock(IList<EditorBlock> blocks, int index, IReadOnlyList<int> blockLines = null)
        {
            if (blocks == null || index < 0 || index >= blocks.Count)
                return null;
            return new MarkdownViewPosition
            {
                HeadingId = HeadingIdAbove(blocks as IReadOnlyList<EditorBlock> ?? blocks.ToList(), index),
                Line = blockLines != null && blockLines.Count == blocks.Count ? blockLines[index] : LineOfBlock(blocks, index),
            };
        }

        /// <summary>
        /// 블록 편집기에서 돌아갈 블록. 원문 편집 화면에서 잡은 위치(<see cref="MarkdownViewPosition.PreferLine"/>)는 줄 번호, 아니면 제목 → 줄 번호 순서. 없으면 -1.
        /// </summary>
        /// <param name="blockLines">블록별 시작 줄(실제 저장 글 기준). null이면 정규형으로 센다.</param>
        public static int ToBlock(IList<EditorBlock> blocks, MarkdownViewPosition position, IReadOnlyList<int> blockLines = null)
        {
            if (blocks == null || blocks.Count == 0 || position == null)
                return -1;
            if (!position.PreferLine && !string.IsNullOrEmpty(position.HeadingId))
            {
                var heading = BlockFind.FindHeading(blocks as IReadOnlyList<EditorBlock> ?? blocks.ToList(), position.HeadingId);
                if (heading >= 0)
                    return heading;
            }
            if (position.Line > 0)
                return blockLines != null && blockLines.Count == blocks.Count ? BlockAtLine(blockLines, position.Line) : BlockAtLine(blocks, position.Line);
            if (!string.IsNullOrEmpty(position.HeadingId))
                return BlockFind.FindHeading(blocks as IReadOnlyList<EditorBlock> ?? blocks.ToList(), position.HeadingId);
            return -1;
        }
    }

    /// <summary>
    /// <c>.md</c> 탭의 보기 설정: 처음 열 때 기본 보기(설정 > 뷰어)와 파일별 마지막 보기(전환 버튼으로 바꿀 때 기록, 최근 200개 —
    /// Markdown 뷰어의 파일별 모드 기억과 같은 방식). <c>%LOCALAPPDATA%\Folderss\markdown-view.json</c>.
    /// 쓰기는 <see cref="SettingsFile"/>(임시 파일 후 교체)이고 실패는 예외로 알린다.
    /// </summary>
    public sealed class MarkdownViewSettings
    {
        public const int MaxRememberedFiles = 200;

        private static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Folderss", "markdown-view.json");

        private static MarkdownViewSettings _shared;

        private readonly string _path;
        // 오래된 것부터 최근 순서(끝이 가장 최근).
        private readonly List<KeyValuePair<string, string>> _files = new List<KeyValuePair<string, string>>();
        private string _defaultView = MarkdownViewSwitch.DefaultViewWhenUnset;

        /// <summary>앱 전체가 함께 쓰는 설정(처음 쓸 때 파일에서 읽음).</summary>
        public static MarkdownViewSettings Shared => _shared ?? (_shared = new MarkdownViewSettings(null));

        /// <param name="path">설정 파일 경로. null이면 기본 경로. 테스트에서 임시 경로를 준다.</param>
        public MarkdownViewSettings(string path)
        {
            _path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
            try
            {
                if (File.Exists(_path))
                    Parse(File.ReadAllText(_path, Encoding.UTF8));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 읽지 못하면 기본값(매핑을 따름·기억 없음)으로 동작한다. 다음 저장이 새로 쓴다.
            }
        }

        /// <summary>처음 열 때 기본 보기: <see cref="MarkdownViewSwitch.DefaultFollowMapping"/>·<see cref="MarkdownViewSwitch.ViewBlocks"/>·<see cref="MarkdownViewSwitch.ViewPreview"/>.</summary>
        public string DefaultView => _defaultView;

        public int RememberedCount => _files.Count;

        /// <summary>이 파일에서 마지막으로 고른 보기(<c>blocks</c>/<c>preview</c>). 없으면 null.</summary>
        public string GetRemembered(string filePath)
        {
            var key = NormalizeKey(filePath);
            if (key == null)
                return null;
            for (var i = _files.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_files[i].Key, key, StringComparison.OrdinalIgnoreCase))
                    return _files[i].Value;
            }
            return null;
        }

        /// <summary>이 파일의 마지막 보기를 메모리에 기록한다(가장 최근으로 옮기고 200개를 넘으면 오래된 것부터 버림). 바뀌었으면 true.</summary>
        public bool Remember(string filePath, string view)
        {
            var key = NormalizeKey(filePath);
            if (key == null || MarkdownViewSwitch.ToViewerKey(view) == null)
                return false;
            view = view.ToLowerInvariant();
            var last = _files.Count > 0 ? _files[_files.Count - 1] : default(KeyValuePair<string, string>);
            if (last.Key != null && string.Equals(last.Key, key, StringComparison.OrdinalIgnoreCase) && last.Value == view)
                return false;
            _files.RemoveAll(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
            _files.Add(new KeyValuePair<string, string>(key, view));
            if (_files.Count > MaxRememberedFiles)
                _files.RemoveRange(0, _files.Count - MaxRememberedFiles);
            return true;
        }

        /// <summary>파일별 보기를 기록하고 저장한다. 실패는 예외로 알린다.</summary>
        public void RememberAndSave(string filePath, string view)
        {
            if (Remember(filePath, view))
                Save();
        }

        /// <summary>처음 열 때 기본 보기를 바꾸고 저장한다(설정 창 저장). 알 수 없는 값은 "매핑을 따름". 실패는 예외로 알린다.</summary>
        public void SaveDefaultView(string view)
        {
            _defaultView = NormalizeDefault(view);
            Save();
        }

        public void Save()
        {
            SettingsFile.WriteAllText(_path, ToJson(), new UTF8Encoding(false));
        }

        public string ToJson()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("version", 2);
                    writer.WriteString("defaultView", _defaultView);
                    writer.WriteStartArray("files");
                    foreach (var kv in _files)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("path", kv.Key);
                        writer.WriteString("view", kv.Value);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>저장한 JSON을 읽는다. 깨진 내용·모르는 값은 건너뛴다(예외 없음).</summary>
        public void Parse(string json)
        {
            _files.Clear();
            // 저장한 값이 없으면(파일 없음·항목 없음·깨진 JSON) 새 기본값, 있으면 그 값(알 수 없는 값은 "매핑을 따름").
            _defaultView = MarkdownViewSwitch.DefaultViewWhenUnset;
            if (string.IsNullOrWhiteSpace(json))
                return;
            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        return;
                    JsonElement value;
                    // version 1(#81 1단계)은 사용자가 고르지 않아도 기본값 "mapping"을 함께 저장했다.
                    // 그 "mapping"은 고른 값으로 보지 않고 새 기본값(블록 편집)을 쓴다(사용자 결정, 2026-11-01). version 2부터는 저장된 값을 그대로 존중.
                    JsonElement version;
                    var legacy = !root.TryGetProperty("version", out version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() < 2;
                    if (root.TryGetProperty("defaultView", out value) && value.ValueKind == JsonValueKind.String)
                    {
                        var saved = NormalizeDefault(value.GetString());
                        if (!(legacy && saved == MarkdownViewSwitch.DefaultFollowMapping))
                            _defaultView = saved;
                    }
                    if (!root.TryGetProperty("files", out value) || value.ValueKind != JsonValueKind.Array)
                        return;
                    foreach (var item in value.EnumerateArray())
                    {
                        JsonElement path, view;
                        if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty("path", out path) && path.ValueKind == JsonValueKind.String &&
                            item.TryGetProperty("view", out view) && view.ValueKind == JsonValueKind.String)
                            Remember(path.GetString(), view.GetString());
                    }
                }
            }
            catch (JsonException)
            {
                // 손으로 고치다 깨진 파일은 기본값으로 동작한다.
            }
        }

        private static string NormalizeDefault(string view)
        {
            return MarkdownViewSwitch.ToViewerKey(view) != null ? view.ToLowerInvariant() : MarkdownViewSwitch.DefaultFollowMapping;
        }

        private static string NormalizeKey(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;
            try
            {
                return Path.GetFullPath(filePath);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return filePath;
            }
        }
    }
}
