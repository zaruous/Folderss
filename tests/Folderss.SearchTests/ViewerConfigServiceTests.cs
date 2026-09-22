using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// viewer-config.json 로드·저장 규칙 검증. 특히 "저장은 되는데 재시작하면 매핑이 사라지던" 버그의 회귀 방지:
    /// 버전 표기가 없는 재정의 파일에서 LegacyDefaultMappings와 같은 값을 잔여물로 오판해 버리던 문제.
    /// </summary>
    public sealed class ViewerConfigServiceTests : IDisposable
    {
        private readonly string _root;
        private readonly string _configPath;

        public ViewerConfigServiceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "folderss-viewer-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _configPath = Path.Combine(_root, "viewer-config.json");
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private ViewerConfigService LoadFrom(string json)
        {
            File.WriteAllText(_configPath, json, Encoding.UTF8);
            return new ViewerConfigService(_configPath);
        }

        // ── 로드 규칙 ────────────────────────────────────────────────────────────────

        [Fact]
        public void Load_UnversionedOverridesFile_KeepsEntriesEqualToLegacyDefaults()
        {
            // 실제 사용자 PC에서 발견된 형태: 버전 없음, 현재 기본값과 같은 항목 없음,
            // .sql/.java → monaco는 LegacyDefaultMappings와 같지만 사용자가 직접 고른 값이다.
            var service = LoadFrom(
                "{\"mappings\":{\".xml\":\"builtin:text\",\".sql\":\"builtin:monaco\"," +
                "\".java\":\"builtin:monaco\",\".toml\":\"builtin:monaco\"}}");

            Assert.Equal("builtin:monaco", service.GetMappingKey(".sql"));
            Assert.Equal("builtin:monaco", service.GetMappingKey(".java"));
            Assert.Equal("builtin:monaco", service.GetMappingKey(".toml"));
            Assert.Equal("builtin:text", service.GetMappingKey(".xml"));
        }

        [Fact]
        public void Load_PreMonacoFullDumpFile_DropsLegacyResidueButKeepsRealOverrides()
        {
            // Monaco 도입 전 형식: 당시 기본 매핑 전체를 덤프 (.md → markdown이 들어 있어 전체 덤프로 판별됨)
            var service = LoadFrom(
                "{\"mappings\":{\".md\":\"builtin:markdown\",\".markdown\":\"builtin:markdown\"," +
                "\".txt\":\"builtin:text\",\".cs\":\"builtin:text\",\".json\":\"builtin:text\"," +
                "\".xml\":\"builtin:text\",\".log\":\"builtin:text\"}}");

            Assert.Null(service.GetMappingKey(".txt"));                    // legacy 표와 동일 → 잔여물로 버림
            Assert.Null(service.GetMappingKey(".log"));
            Assert.Equal("builtin:text", service.GetMappingKey(".cs"));    // legacy 표(.cs → monaco)와 다름 → 유지
            Assert.Equal("builtin:text", service.GetMappingKey(".json"));  // 현재 기본값(monaco)과 다른 재정의 → 유지
            Assert.Equal("builtin:markdown", service.GetMappingKey(".md")); // 기본값 그대로
        }

        [Fact]
        public void Load_VersionedFile_KeepsEveryEntry()
        {
            var service = LoadFrom(
                "{\"version\":2,\"mappings\":{\".txt\":\"builtin:text\",\".cs\":\"builtin:monaco\"}}");

            Assert.Equal("builtin:text", service.GetMappingKey(".txt"));
            Assert.Equal("builtin:monaco", service.GetMappingKey(".cs"));
        }

        [Fact]
        public void Load_VersionWrittenAsQuotedString_IsStillTreatedAsVersioned()
        {
            // 손으로 고친 파일. "2"를 v1로 오판하면 legacy 정리 규칙이 .txt → text를 버린다.
            var service = LoadFrom("{\"version\":\"2\",\"mappings\":{\".txt\":\"builtin:text\"}}");

            Assert.Equal("builtin:text", service.GetMappingKey(".txt"));
        }

        [Fact]
        public void Load_PreMonacoDumpWithoutMarkdownEntries_KeepsLegacyEntries_KnownLimitation()
        {
            // Monaco 도입 전 파일에서 .md/.markdown 행이 빠진 경우(당시 UI에서 지웠거나 손으로 고친 경우)는
            // 재정의 파일과 구분할 수 없어 legacy 잔여물이 남는다. 사용자 선택을 버리는 쪽보다 남기는 쪽을 택한 결정이며,
            // 이 테스트는 그 결정을 문서화한다. 남은 항목은 설정 창에서 지울 수 있다.
            var service = LoadFrom(
                "{\"mappings\":{\".txt\":\"builtin:text\",\".cs\":\"builtin:text\",\".log\":\"builtin:text\"}}");

            Assert.Equal("builtin:text", service.GetMappingKey(".txt"));
            Assert.Equal("builtin:text", service.GetMappingKey(".log"));
        }

        [Fact]
        public void Load_MissingFile_UsesDefaultsOnly()
        {
            var service = new ViewerConfigService(_configPath);

            Assert.Equal("builtin:markdown", service.GetMappingKey(".md"));
            Assert.Equal("builtin:monaco", service.GetMappingKey(".json"));
            Assert.Null(service.GetMappingKey(".txt"));
            Assert.False(File.Exists(_configPath));
        }

        // ── 저장 규칙 ────────────────────────────────────────────────────────────────

        [Fact]
        public void ReplaceMappings_WritesVersionedOverridesOnce_AndRoundTrips()
        {
            var service = new ViewerConfigService(_configPath);

            service.ReplaceMappings(new[]
            {
                new KeyValuePair<string, string>(".md", "builtin:markdown"), // 기본값과 동일 → 저장하지 않음
                new KeyValuePair<string, string>(".txt", "builtin:text"),    // legacy 표와 동일하지만 사용자 선택
                new KeyValuePair<string, string>("cs", "builtin:monaco"),    // 점 없는 확장자 정규화
            });

            var json = File.ReadAllText(_configPath, Encoding.UTF8);
            Assert.StartsWith("{\"version\":2,", json);
            Assert.DoesNotContain("\".md\"", json);

            var reloaded = new ViewerConfigService(_configPath);
            Assert.Equal("builtin:text", reloaded.GetMappingKey(".txt"));
            Assert.Equal("builtin:monaco", reloaded.GetMappingKey(".cs"));
            Assert.Equal("builtin:markdown", reloaded.GetMappingKey(".md"));
        }

        [Fact]
        public void ReplaceMappings_SystemDefaultRows_RoundTrip()
        {
            // 설정 창의 "삭제"는 기본 매핑이 있는 행을 system:default로 바꾸고(내장 뷰어 끄기),
            // 사용자가 추가한 행은 목록에서 지운다. 기본 매핑이 없는 확장자에 system:default를 직접 고른 행도
            // 기존 동작대로 재정의로 저장되어 목록에 남는다.
            var service = new ViewerConfigService(_configPath);

            service.ReplaceMappings(new[]
            {
                new KeyValuePair<string, string>(".md", ViewerConfigService.SystemDefaultKey),
                new KeyValuePair<string, string>(".txt", ViewerConfigService.SystemDefaultKey),
            });

            var reloaded = new ViewerConfigService(_configPath);
            Assert.Equal(ViewerConfigService.SystemDefaultKey, reloaded.GetMappingKey(".md"));
            Assert.Null(reloaded.Resolve(".md"));
            Assert.Equal(ViewerConfigService.SystemDefaultKey, reloaded.GetMappingKey(".txt"));
            Assert.Contains(reloaded.GetMappingRows(), row => row.Extension == ".txt");
        }

        [Fact]
        public void ReplaceMappings_LeavesNoTemporaryFile()
        {
            var service = new ViewerConfigService(_configPath);

            service.ReplaceMappings(new[] { new KeyValuePair<string, string>(".txt", "builtin:text") });

            Assert.True(File.Exists(_configPath));
            Assert.False(File.Exists(_configPath + ".tmp"));
        }

        [Fact]
        public void ReplaceMappings_ReplacesPreviousOverrides()
        {
            var service = LoadFrom("{\"version\":2,\"mappings\":{\".old\":\"builtin:text\"}}");

            service.ReplaceMappings(new[] { new KeyValuePair<string, string>(".new", "builtin:monaco") });

            var reloaded = new ViewerConfigService(_configPath);
            Assert.Null(reloaded.GetMappingKey(".old"));
            Assert.Equal("builtin:monaco", reloaded.GetMappingKey(".new"));
        }

        [Fact]
        public void ReplaceMappings_WhenWriteFails_ThrowsButKeepsInMemoryState()
        {
            // 설정 파일 자리에 디렉터리를 만들어 쓰기가 실패하게 한다. 설정 창은 이 예외를 모아 사용자에게 보여준다.
            Directory.CreateDirectory(_configPath);
            var service = new ViewerConfigService(_configPath);

            Assert.ThrowsAny<Exception>(() =>
                service.ReplaceMappings(new[] { new KeyValuePair<string, string>(".txt", "builtin:text") }));

            Assert.Equal("builtin:text", service.GetMappingKey(".txt"));
        }
    }
}
