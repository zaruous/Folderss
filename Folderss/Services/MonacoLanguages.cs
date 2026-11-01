using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Folderss.Services
{
    /// <summary>Monaco에 등록된 언어 하나(<c>monaco.languages.getLanguages()</c> 항목과 같은 모양).</summary>
    public sealed class MonacoLanguageInfo
    {
        public MonacoLanguageInfo(string id, IReadOnlyList<string> aliases, IReadOnlyList<string> extensions,
            IReadOnlyList<string> filenames, IReadOnlyList<string> filenamePatterns)
        {
            Id = id;
            Aliases = aliases ?? Array.Empty<string>();
            Extensions = extensions ?? Array.Empty<string>();
            Filenames = filenames ?? Array.Empty<string>();
            FilenamePatterns = filenamePatterns ?? Array.Empty<string>();
        }

        public string Id { get; }
        public IReadOnlyList<string> Aliases { get; }
        public IReadOnlyList<string> Extensions { get; }
        public IReadOnlyList<string> Filenames { get; }
        public IReadOnlyList<string> FilenamePatterns { get; }

        /// <summary>목록 표시 이름: 첫 별칭(없으면 id).</summary>
        public string DisplayName => Aliases.Count > 0 && !string.IsNullOrWhiteSpace(Aliases[0]) ? Aliases[0] : Id;
    }

    /// <summary>
    /// Monaco 뷰어의 언어 결정(#82).
    /// <para>
    /// 실제 결정은 편집기 안(<c>monaco-app.html</c>의 <c>detectLanguage</c>)에서 <c>monaco.languages.getLanguages()</c>의
    /// <c>filenames</c> → <c>filenamePatterns</c> → <c>extensions</c>(가장 긴 확장자, 같으면 먼저 등록된 언어)로 한다 — 대소문자 무시.
    /// C#은 "강제 언어"(파일별 선택 → 사용자 지정 → 예외 사전, <see cref="ResolveForced"/>)만 넘기고, 없으면 null을 넘겨 편집기가 고르게 한다.
    /// </para>
    /// <para>
    /// <see cref="Languages"/>는 번들 Monaco의 등록 정보를 옮겨 둔 정적 표다. 설정 창 언어 목록(편집기가 없는 곳)과
    /// 단위 테스트(<see cref="Resolve"/>로 JS와 같은 순서를 흉내)에 쓴다. 번들 <c>basic-languages/monaco.contribution.js</c>와
    /// 어긋나면 <c>MonacoLanguageTests</c>가 실패하므로, Monaco를 올리면 표를 다시 만든다(<c>docs/items/monaco-language.md</c>).
    /// </para>
    /// </summary>
    public static class MonacoLanguages
    {
        public const string PlainText = "plaintext";
        public const string JsonLines = "jsonl";

        /// <summary>
        /// Monaco 등록 정보와 다르게 열 확장자(예외 덮어쓰기). 등록 정보로 이미 맞게 열리는 확장자(.xaml→xml, .jsonl→jsonl 등)는 두지 않는다.
        /// </summary>
        private static readonly Dictionary<string, string> ExceptionOverrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Monaco는 .h를 C로 등록하지만 Windows 코드베이스의 .h는 대부분 C++ 헤더다(이전 동작 유지).
                { ".h", "cpp" },
            };

        private static readonly IReadOnlyList<MonacoLanguageInfo> LanguageTable = new[]
        {
            // 편집기 코어(editor.main.js)
            L("plaintext", "Plain Text|text", ".txt"),
            // language/json (monaco.contribution-*.js) — 런타임 getLanguages()에서 plaintext 바로 다음(브라우저에서 확인, 총 92개)
            L("json", "JSON|json", ".json|.bowerrc|.jshintrc|.jscsrc|.eslintrc|.babelrc|.har"),
            // basic-languages/monaco.contribution.js — 등록 순서 그대로(getLanguages() 순서)
            L("abap", "abap|ABAP", ".abap"),
            L("apex", "Apex|apex", ".cls"),
            L("azcli", "Azure CLI|azcli", ".azcli"),
            L("bat", "Batch|bat", ".bat|.cmd"),
            L("bicep", "Bicep", ".bicep"),
            L("cameligo", "Cameligo", ".mligo"),
            L("clojure", "clojure|Clojure", ".clj|.cljs|.cljc|.edn"),
            L("coffeescript", "CoffeeScript|coffeescript|coffee", ".coffee"),
            L("c", "C|c", ".c|.h"),
            L("cpp", "C++|Cpp|cpp", ".cpp|.cc|.cxx|.hpp|.hh|.hxx"),
            L("csharp", "C#|csharp", ".cs|.csx|.cake"),
            L("csp", "CSP|csp", ".csp"),
            L("css", "CSS|css", ".css"),
            L("cypher", "Cypher|OpenCypher", ".cypher|.cyp"),
            L("dart", "Dart|dart", ".dart"),
            L("dockerfile", "Dockerfile", ".dockerfile", "Dockerfile"),
            L("ecl", "ECL|Ecl|ecl", ".ecl"),
            L("elixir", "Elixir|elixir|ex", ".ex|.exs"),
            L("flow9", "Flow9|Flow|flow9|flow", ".flow"),
            L("fsharp", "F#|FSharp|fsharp", ".fs|.fsi|.ml|.mli|.fsx|.fsscript"),
            L("freemarker2", "FreeMarker2|Apache FreeMarker2", ".ftl|.ftlh|.ftlx"),
            L("freemarker2.tag-angle.interpolation-dollar", "FreeMarker2 (Angle/Dollar)|Apache FreeMarker2 (Angle/Dollar)", ""),
            L("freemarker2.tag-bracket.interpolation-dollar", "FreeMarker2 (Bracket/Dollar)|Apache FreeMarker2 (Bracket/Dollar)", ""),
            L("freemarker2.tag-angle.interpolation-bracket", "FreeMarker2 (Angle/Bracket)|Apache FreeMarker2 (Angle/Bracket)", ""),
            L("freemarker2.tag-bracket.interpolation-bracket", "FreeMarker2 (Bracket/Bracket)|Apache FreeMarker2 (Bracket/Bracket)", ""),
            L("freemarker2.tag-auto.interpolation-dollar", "FreeMarker2 (Auto/Dollar)|Apache FreeMarker2 (Auto/Dollar)", ""),
            L("freemarker2.tag-auto.interpolation-bracket", "FreeMarker2 (Auto/Bracket)|Apache FreeMarker2 (Auto/Bracket)", ""),
            L("go", "Go", ".go"),
            L("graphql", "GraphQL|graphql|gql", ".graphql|.gql"),
            L("handlebars", "Handlebars|handlebars|hbs", ".handlebars|.hbs"),
            L("hcl", "Terraform|tf|HCL|hcl", ".tf|.tfvars|.hcl"),
            L("html", "HTML|htm|html|xhtml", ".html|.htm|.shtml|.xhtml|.mdoc|.jsp|.asp|.aspx|.jshtm"),
            L("ini", "Ini|ini", ".ini|.properties|.gitconfig", "config|.gitattributes|.gitconfig|.editorconfig"),
            L("java", "Java|java", ".java|.jav"),
            L("javascript", "JavaScript|javascript|js", ".js|.es6|.jsx|.mjs|.cjs", "jakefile"),
            L("julia", "julia|Julia", ".jl"),
            L("kotlin", "Kotlin|kotlin", ".kt|.kts"),
            L("less", "Less|less", ".less"),
            L("lexon", "Lexon", ".lex"),
            L("lua", "Lua|lua", ".lua"),
            L("liquid", "Liquid|liquid", ".liquid|.html.liquid"),
            L("m3", "Modula-3|Modula3|modula3|m3", ".m3|.i3|.mg|.ig"),
            L("markdown", "Markdown|markdown", ".md|.markdown|.mdown|.mkdn|.mkd|.mdwn|.mdtxt|.mdtext"),
            L("mdx", "MDX|mdx", ".mdx"),
            L("mips", "MIPS|MIPS-V", ".s"),
            L("msdax", "DAX|MSDAX", ".dax|.msdax"),
            L("mysql", "MySQL|mysql", ""),
            L("objective-c", "Objective-C", ".m"),
            L("pascal", "Pascal|pas", ".pas|.p|.pp"),
            L("pascaligo", "Pascaligo|ligo", ".ligo"),
            L("perl", "Perl|pl", ".pl|.pm"),
            L("pgsql", "PostgreSQL|postgres|pg|postgre", ""),
            L("php", "PHP|php", ".php|.php4|.php5|.phtml|.ctp"),
            L("pla", "", ".pla"),
            L("postiats", "ATS|ATS/Postiats", ".dats|.sats|.hats"),
            L("powerquery", "PQ|M|Power Query|Power Query M", ".pq|.pqm"),
            L("powershell", "PowerShell|powershell|ps|ps1", ".ps1|.psm1|.psd1"),
            L("proto", "protobuf|Protocol Buffers", ".proto"),
            L("pug", "Pug|Jade|jade", ".jade|.pug"),
            L("python", "Python|py", ".py|.rpy|.pyw|.cpy|.gyp|.gypi"),
            L("qsharp", "Q#|qsharp", ".qs"),
            L("r", "R|r", ".r|.rhistory|.rmd|.rprofile|.rt"),
            L("razor", "Razor|razor", ".cshtml"),
            L("redis", "redis", ".redis"),
            L("redshift", "Redshift|redshift", ""),
            L("restructuredtext", "reStructuredText|restructuredtext", ".rst"),
            L("ruby", "Ruby|rb", ".rb|.rbx|.rjs|.gemspec|.pp", "rakefile|Gemfile"),
            L("rust", "Rust|rust", ".rs|.rlib"),
            L("sb", "Small Basic|sb", ".sb"),
            L("scala", "Scala|scala|SBT|Sbt|sbt|Dotty|dotty", ".scala|.sc|.sbt"),
            L("scheme", "scheme|Scheme", ".scm|.ss|.sch|.rkt"),
            L("scss", "Sass|sass|scss", ".scss"),
            L("shell", "Shell|sh", ".sh|.bash"),
            L("sol", "sol|solidity|Solidity", ".sol"),
            L("aes", "aes|sophia|Sophia", ".aes"),
            L("sparql", "sparql|SPARQL", ".rq"),
            L("sql", "SQL", ".sql"),
            L("st", "StructuredText|scl|stl", ".st|.iecst|.iecplc|.lc3lib|.TcPOU|.TcDUT|.TcGVL|.TcIO"),
            L("swift", "Swift|swift", ".swift"),
            L("systemverilog", "SV|sv|SystemVerilog|systemverilog", ".sv|.svh"),
            L("verilog", "V|v|Verilog|verilog", ".v|.vh"),
            L("tcl", "tcl|Tcl|tcltk|TclTk|tcl/tk|Tcl/Tk", ".tcl"),
            L("twig", "Twig|twig", ".twig"),
            L("typescript", "TypeScript|ts|typescript", ".ts|.tsx|.cts|.mts"),
            L("typespec", "TypeSpec", ".tsp"),
            L("vb", "Visual Basic|vb", ".vb"),
            L("wgsl", "WebGPU Shading Language|WGSL|wgsl", ".wgsl"),
            L("xml", "XML|xml", ".xml|.xsd|.dtd|.ascx|.csproj|.config|.props|.targets|.wxi|.wxl|.wxs|.xaml|.svg|.svgz|.opf|.xslt|.xsl"),
            L("yaml", "YAML|yaml|YML|yml", ".yaml|.yml"),
            // monaco-jsonl.js (Folderss가 등록)
            L("jsonl", "JSON Lines|jsonl", ".jsonl|.ndjson"),
        };

        /// <summary>번들 Monaco에 등록된 언어(정적 표, getLanguages() 순서).</summary>
        public static IReadOnlyList<MonacoLanguageInfo> Languages => LanguageTable;

        /// <summary>예외 덮어쓰기 사전(읽기 전용 사본).</summary>
        public static IReadOnlyDictionary<string, string> Exceptions => ExceptionOverrides;

        public static MonacoLanguageInfo Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            return LanguageTable.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 편집기에 넘길 강제 언어. 우선순위: 파일별 선택 → 사용자 지정(<paramref name="userMappings"/> 순서대로 첫 일치) → 예외 사전.
        /// 모두 없으면 null — 편집기가 Monaco 등록 정보로 고르고, 거기도 없으면 plaintext.
        /// </summary>
        public static string ResolveForced(string fileName, string perFileLanguage,
            IEnumerable<KeyValuePair<string, string>> userMappings)
        {
            if (IsValidLanguageId(perFileLanguage))
                return perFileLanguage;

            var name = GetFileName(fileName);
            if (string.IsNullOrEmpty(name))
                return null;

            if (userMappings != null)
            {
                foreach (var mapping in userMappings)
                {
                    if (IsValidLanguageId(mapping.Value) && MatchesPattern(name, mapping.Key))
                        return mapping.Value;
                }
            }

            var extension = Path.GetExtension(name);
            string exception;
            if (!string.IsNullOrEmpty(extension) && ExceptionOverrides.TryGetValue(extension, out exception))
                return exception;
            return null;
        }

        /// <summary>
        /// 최종 언어(테스트·표시용). <see cref="ResolveForced"/> → <see cref="MatchRegistry"/> → plaintext.
        /// 편집기 안에서는 <c>detectLanguage</c>가 런타임 등록 정보로 같은 순서를 따른다.
        /// </summary>
        public static string Resolve(string fileName, string perFileLanguage,
            IEnumerable<KeyValuePair<string, string>> userMappings)
        {
            return ResolveForced(fileName, perFileLanguage, userMappings)
                ?? MatchRegistry(fileName, LanguageTable)
                ?? PlainText;
        }

        /// <summary>
        /// <c>monaco-app.html</c>의 <c>detectLanguage</c>와 같은 규칙으로 등록 정보에서 언어를 고른다(대소문자 무시):
        /// 파일 이름 일치(<c>filenames</c>) → 파일 이름 패턴(<c>filenamePatterns</c>, glob) → 확장자(<c>extensions</c>, 이름 끝이 같은 것 중 가장 긴 것,
        /// 같은 길이면 먼저 등록된 언어). 없으면 null.
        /// </summary>
        public static string MatchRegistry(string fileName, IEnumerable<MonacoLanguageInfo> languages)
        {
            var name = GetFileName(fileName);
            if (string.IsNullOrEmpty(name) || languages == null)
                return null;
            var list = languages as IReadOnlyList<MonacoLanguageInfo> ?? languages.ToList();

            foreach (var language in list)
            {
                if (language.Filenames.Any(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase)))
                    return language.Id;
            }
            foreach (var language in list)
            {
                if (language.FilenamePatterns.Any(p => GlobMatches(name, p)))
                    return language.Id;
            }

            string best = null;
            var bestLength = 0;
            foreach (var language in list)
            {
                foreach (var extension in language.Extensions)
                {
                    if (string.IsNullOrEmpty(extension) || extension.Length <= bestLength)
                        continue;
                    if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        best = language.Id;
                        bestLength = extension.Length;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// 사용자 지정 패턴이 파일 이름에 맞는지. <c>.inc</c>처럼 점으로 시작하고 와일드카드가 없으면 확장자(이름 끝),
        /// <c>*</c>·<c>?</c>가 있으면 파일 이름 glob(<c>Dockerfile*</c>, <c>*.conf</c>), 그 밖에는 파일 이름 전체(<c>Makefile</c>). 대소문자 무시.
        /// </summary>
        public static bool MatchesPattern(string fileName, string pattern)
        {
            var name = GetFileName(fileName);
            pattern = NormalizePattern(pattern);
            if (string.IsNullOrEmpty(name) || pattern == null)
                return false;
            if (pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0)
                return GlobMatches(name, pattern);
            if (pattern.StartsWith(".", StringComparison.Ordinal))
                return name.EndsWith(pattern, StringComparison.OrdinalIgnoreCase);
            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>설정 창 입력을 다듬는다: 앞뒤 공백 제거, 경로 구분자·빈 값은 거부(null). 와일드카드 없는 영숫자 단어는 그대로(파일 이름).</summary>
        public static string NormalizePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return null;
            pattern = pattern.Trim();
            if (pattern.IndexOf('/') >= 0 || pattern.IndexOf('\\') >= 0 || pattern == "." || pattern == "*")
                return null;
            return pattern;
        }

        /// <summary>Monaco 언어 id 모양(영숫자·<c>.</c>·<c>-</c>·<c>_</c>·<c>+</c>)인지. 등록 여부는 편집기가 확인한다(없는 id면 자동으로 돌아감).</summary>
        public static bool IsValidLanguageId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
                return false;
            foreach (var c in id)
            {
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == '+'))
                    return false;
            }
            return true;
        }

        private static bool GlobMatches(string name, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string GetFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var index = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return index >= 0 ? path.Substring(index + 1) : path;
        }

        private static MonacoLanguageInfo L(string id, string aliases, string extensions,
            string filenames = "", string filenamePatterns = "")
        {
            return new MonacoLanguageInfo(id, Split(aliases), Split(extensions), Split(filenames), Split(filenamePatterns));
        }

        private static string[] Split(string value)
        {
            return string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split('|');
        }
    }

    /// <summary>
    /// Monaco 뷰어 언어 설정(#82): 설정 > 뷰어의 사용자 지정 연결(패턴 → 언어, 순서대로 첫 일치)과
    /// 탭 툴바에서 고른 파일별 언어(최근 200개 — <see cref="MarkdownViewSettings"/>와 같은 방식).
    /// <c>%LOCALAPPDATA%\Folderss\monaco-language.json</c>. 쓰기는 <see cref="SettingsFile"/>(임시 파일 후 교체)이고 실패는 예외로 알린다.
    /// </summary>
    public sealed class MonacoLanguageSettings
    {
        public const int MaxRememberedFiles = 200;

        private static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Folderss", "monaco-language.json");

        private static MonacoLanguageSettings _shared;

        private readonly string _path;
        private readonly object _gate = new object();
        // 오래된 것부터 최근 순서(끝이 가장 최근).
        private readonly List<KeyValuePair<string, string>> _files = new List<KeyValuePair<string, string>>();
        private readonly List<KeyValuePair<string, string>> _mappings = new List<KeyValuePair<string, string>>();

        /// <summary>앱 전체가 함께 쓰는 설정(처음 쓸 때 파일에서 읽음).</summary>
        public static MonacoLanguageSettings Shared => _shared ?? (_shared = new MonacoLanguageSettings(null));

        /// <param name="path">설정 파일 경로. null이면 기본 경로. 테스트에서 임시 경로를 준다.</param>
        public MonacoLanguageSettings(string path)
        {
            _path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
            try
            {
                if (File.Exists(_path))
                    Parse(File.ReadAllText(_path, Encoding.UTF8));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 읽지 못하면 기본값(사용자 지정·기억 없음)으로 동작한다. 다음 저장이 새로 쓴다.
            }
        }

        public int RememberedCount { get { lock (_gate) return _files.Count; } }

        /// <summary>사용자 지정 연결(패턴 → 언어 id), 설정 창에 보이는 순서.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Mappings
        {
            get { lock (_gate) return _mappings.ToList(); }
        }

        /// <summary>이 파일에 쓸 강제 언어(파일별 선택 → 사용자 지정 → 예외 사전). 없으면 null(편집기가 자동으로 고름).</summary>
        public string ResolveForced(string filePath)
        {
            lock (_gate)
                return MonacoLanguages.ResolveForced(filePath, GetRememberedCore(filePath), _mappings);
        }

        /// <summary>탭 툴바에서 이 파일에 고른 언어. 없으면 null.</summary>
        public string GetRemembered(string filePath)
        {
            lock (_gate)
                return GetRememberedCore(filePath);
        }

        /// <summary>
        /// 이 파일의 언어를 메모리에 기록한다(가장 최근으로 옮기고 200개를 넘으면 오래된 것부터 버림).
        /// <paramref name="language"/>가 null·빈 값("자동")이면 기억을 지운다. 바뀌었으면 true.
        /// </summary>
        public bool Remember(string filePath, string language)
        {
            var key = NormalizeKey(filePath);
            if (key == null)
                return false;
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(language))
                    return _files.RemoveAll(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) > 0;
                if (!MonacoLanguages.IsValidLanguageId(language))
                    return false;
                var last = _files.Count > 0 ? _files[_files.Count - 1] : default(KeyValuePair<string, string>);
                if (last.Key != null && string.Equals(last.Key, key, StringComparison.OrdinalIgnoreCase) && last.Value == language)
                    return false;
                _files.RemoveAll(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
                _files.Add(new KeyValuePair<string, string>(key, language));
                if (_files.Count > MaxRememberedFiles)
                    _files.RemoveRange(0, _files.Count - MaxRememberedFiles);
                return true;
            }
        }

        /// <summary>파일별 언어를 기록(또는 "자동"이면 삭제)하고 저장한다. 실패는 예외로 알린다.</summary>
        public void RememberAndSave(string filePath, string language)
        {
            if (Remember(filePath, language))
                Save();
        }

        /// <summary>
        /// 사용자 지정 연결을 바꾼다(설정 창). 패턴이 비었거나 언어 id가 잘못된 행은 버리고, 같은 패턴은 뒤의 것을 버린다(먼저 온 것이 이김).
        /// </summary>
        public void SetMappings(IEnumerable<KeyValuePair<string, string>> mappings)
        {
            lock (_gate)
            {
                _mappings.Clear();
                if (mappings == null)
                    return;
                foreach (var kv in mappings)
                {
                    var pattern = MonacoLanguages.NormalizePattern(kv.Key);
                    var language = kv.Value == null ? null : kv.Value.Trim();
                    if (pattern == null || !MonacoLanguages.IsValidLanguageId(language))
                        continue;
                    if (_mappings.Any(m => string.Equals(m.Key, pattern, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    _mappings.Add(new KeyValuePair<string, string>(pattern, language));
                }
            }
        }

        /// <summary>사용자 지정 연결을 바꾸고 저장한다(설정 창 <c>TrySave</c>). 실패는 예외로 알린다.</summary>
        public void SaveMappings(IEnumerable<KeyValuePair<string, string>> mappings)
        {
            SetMappings(mappings);
            Save();
        }

        public void Save()
        {
            SettingsFile.WriteAllText(_path, ToJson(), new UTF8Encoding(false));
        }

        public string ToJson()
        {
            lock (_gate)
            {
                using (var stream = new MemoryStream())
                {
                    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                    {
                        writer.WriteStartObject();
                        writer.WriteNumber("version", 1);
                        writer.WriteStartArray("mappings");
                        foreach (var kv in _mappings)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("pattern", kv.Key);
                            writer.WriteString("language", kv.Value);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        writer.WriteStartArray("files");
                        foreach (var kv in _files)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("path", kv.Key);
                            writer.WriteString("language", kv.Value);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                    }
                    return Encoding.UTF8.GetString(stream.ToArray());
                }
            }
        }

        /// <summary>저장한 JSON을 읽는다. 깨진 내용·모르는 값은 건너뛴다(예외 없음).</summary>
        public void Parse(string json)
        {
            lock (_gate)
            {
                _files.Clear();
                _mappings.Clear();
            }
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
                    if (root.TryGetProperty("mappings", out value) && value.ValueKind == JsonValueKind.Array)
                    {
                        var mappings = new List<KeyValuePair<string, string>>();
                        foreach (var item in value.EnumerateArray())
                        {
                            string pattern, language;
                            if (TryReadPair(item, "pattern", out pattern, out language))
                                mappings.Add(new KeyValuePair<string, string>(pattern, language));
                        }
                        SetMappings(mappings);
                    }
                    if (root.TryGetProperty("files", out value) && value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in value.EnumerateArray())
                        {
                            string path, language;
                            if (TryReadPair(item, "path", out path, out language))
                                Remember(path, language);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // 손으로 고치다 깨진 파일은 기본값으로 동작한다.
            }
        }

        private static bool TryReadPair(JsonElement item, string keyName, out string key, out string language)
        {
            key = null;
            language = null;
            JsonElement k, l;
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty(keyName, out k) || k.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("language", out l) || l.ValueKind != JsonValueKind.String)
                return false;
            key = k.GetString();
            language = l.GetString();
            return true;
        }

        private string GetRememberedCore(string filePath)
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
