using System;
using System.Collections.Generic;

namespace Folderss.Services
{
    /// <summary>git config의 적용 범위. 글로벌(사용자 전체)과 로컬(저장소 하나)만 다룬다 — system은 관리자 권한이 필요해 제외.</summary>
    public enum GitConfigScope
    {
        /// <summary><c>--global</c>: <c>~/.gitconfig</c>. 모든 저장소에 적용.</summary>
        Global,
        /// <summary><c>--local</c>: <c>&lt;저장소&gt;/.git/config</c>. 이 저장소에서 글로벌 값을 덮어쓴다.</summary>
        Local
    }

    /// <summary><c>git config --list -z</c>의 한 항목. 같은 키가 여러 번 나올 수 있다(multi-valued).</summary>
    public sealed class GitConfigEntry
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    /// <summary>저장소 설정 대화상자에서 편집하는 항목 하나.</summary>
    public sealed class GitConfigField
    {
        public string Key { get; }
        public string Label { get; }
        public string Hint { get; }

        public GitConfigField(string key, string label, string hint)
        {
            Key = key;
            Label = label;
            Hint = hint;
        }
    }

    /// <summary>대화상자에서 바꾼 값 하나. <see cref="Value"/>가 null이면 키를 지운다.</summary>
    public sealed class GitConfigChange
    {
        public GitConfigScope Scope { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
    }

    /// <summary>
    /// 저장소별 인증·작성자 설정(git config) 조회·저장 명령 인수와 출력 파서. UI와 테스트가 같은 인수를 쓴다.
    /// 값 쓰기는 단일 값 키에만 쓴다 — 값이 여러 개인 키(<c>credential.helper</c>를 여러 번 등록한 경우 등)는
    /// <c>git config key value</c>·<c>--unset</c>이 거부(종료 코드 5)하므로 대화상자가 편집을 막는다.
    /// </summary>
    public static class GitConfigCommands
    {
        /// <summary>대화상자가 보여 주고 고칠 수 있는 키. 인증(자격 증명·SSH)과 커밋 작성자.</summary>
        public static readonly IReadOnlyList<GitConfigField> AuthFields = new[]
        {
            new GitConfigField("user.name", "작성자 이름", "커밋에 기록되는 이름"),
            new GitConfigField("user.email", "작성자 이메일", "커밋에 기록되는 이메일. 회사·개인 계정을 저장소별로 나눌 때 로컬에 둔다"),
            new GitConfigField("credential.helper", "자격 증명 도우미", "예: manager (Git Credential Manager), store, wincred. 비우면 git 기본"),
            new GitConfigField("credential.username", "원격 사용자 이름", "HTTPS 원격에 로그인할 때 쓰는 계정 이름(비밀번호·토큰은 저장하지 않음)"),
            new GitConfigField("credential.useHttpPath", "경로별 자격 증명", "true면 같은 호스트라도 저장소 경로마다 자격 증명을 따로 저장(계정이 여럿일 때)"),
            new GitConfigField("core.sshCommand", "SSH 명령", "예: ssh -i C:/Users/me/.ssh/work_ed25519 — 저장소별로 다른 키를 쓸 때")
        };

        private static string Flag(GitConfigScope scope) => scope == GitConfigScope.Global ? "--global" : "--local";

        /// <summary>그 범위의 모든 설정을 NUL 구분(<c>-z</c>)으로. 글로벌 파일이 아직 없으면 git이 종료 코드 128로 실패한다(= 설정 없음).</summary>
        public static List<string> List(GitConfigScope scope)
        {
            return new List<string> { "config", Flag(scope), "--list", "-z" };
        }

        public static List<string> Set(GitConfigScope scope, string key, string value)
        {
            return new List<string> { "config", Flag(scope), "--", key, value };
        }

        public static List<string> Unset(GitConfigScope scope, string key)
        {
            return new List<string> { "config", Flag(scope), "--unset", "--", key };
        }

        /// <summary>바꾼 값 하나를 적용하는 인수. 값이 null이면 지운다.</summary>
        public static List<string> Apply(GitConfigChange change)
        {
            return change.Value == null ? Unset(change.Scope, change.Key) : Set(change.Scope, change.Key, change.Value);
        }

        /// <summary>
        /// <c>--list -z</c> 출력 파싱: 항목은 NUL로 끝나고 키와 값은 첫 줄바꿈으로 나뉜다(값 안의 줄바꿈은 그대로).
        /// 값 없는 키(<c>[a] b</c>)는 빈 값으로 둔다. 파일 순서를 지킨다 — git은 같은 키가 여럿이면 마지막 값을 쓴다.
        /// </summary>
        public static List<GitConfigEntry> ParseList(string stdout)
        {
            var entries = new List<GitConfigEntry>();
            if (string.IsNullOrEmpty(stdout))
                return entries;

            foreach (var record in stdout.Split('\0'))
            {
                if (record.Length == 0)
                    continue;
                var newline = record.IndexOf('\n');
                entries.Add(newline < 0
                    ? new GitConfigEntry { Key = record, Value = string.Empty }
                    : new GitConfigEntry { Key = record.Substring(0, newline), Value = record.Substring(newline + 1) });
            }
            return entries;
        }

        /// <summary>키의 값 목록(파일 순서). git은 섹션·변수 이름을 소문자로 출력하므로 대/소문자 없이 비교한다.</summary>
        public static List<string> ValuesOf(IEnumerable<GitConfigEntry> entries, string key)
        {
            var values = new List<string>();
            foreach (var entry in entries)
            {
                if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
                    values.Add(entry.Value);
            }
            return values;
        }

        /// <summary>
        /// 편집 결과를 변경 목록으로. 원래 값이 하나였고 글자가 바뀐 키만 들어간다.
        /// 빈 입력은 "지움"(원래 없었으면 아무것도 안 함 — <c>--unset</c>은 없는 키에 실패한다). 값이 여럿인 키는 건드리지 않는다.
        /// </summary>
        public static List<GitConfigChange> Diff(GitConfigScope scope, IReadOnlyList<GitConfigEntry> original, IReadOnlyDictionary<string, string> edited)
        {
            var changes = new List<GitConfigChange>();
            foreach (var pair in edited)
            {
                var before = ValuesOf(original, pair.Key);
                if (before.Count > 1)
                    continue;
                var oldValue = before.Count == 1 ? before[0] : null;
                var newValue = string.IsNullOrEmpty(pair.Value) ? null : pair.Value;
                if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
                    continue;
                if (newValue == null && oldValue == null)
                    continue;
                changes.Add(new GitConfigChange { Scope = scope, Key = pair.Key, Value = newValue });
            }
            return changes;
        }
    }
}
