using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Folderss.Models
{
    /// <summary>블록 편집기(<c>Controls/BlockEditor</c>)의 블록 종류. Markdown 저장 형식은 <c>Services/BlockMarkdown</c> 참고.</summary>
    public enum BlockKind
    {
        Paragraph,
        Heading1,
        Heading2,
        Heading3,
        Bulleted,
        Numbered,
        Todo,
        Toggle,
        Quote,
        Callout,
        Code,
        Divider,
        /// <summary>편집기가 해석하지 않는 Markdown 원문(front matter·구분 행 없는 표·HTML 블록 등). 저장할 때 그대로 쓴다.</summary>
        Raw,
        /// <summary>
        /// 표의 한 행(GFM 표). 셀은 <see cref="EditorBlock.Text"/>에 탭으로 구분해 담는다. 같은 들여쓰기로 이어진 표 행들이 한 표이고
        /// 그 첫 행이 머리글이다(정렬은 머리글의 <see cref="EditorBlock.TableAlign"/>). 행마다 블록이라 큰 표도 화면에 보이는 행만 만든다.
        /// </summary>
        TableRow,
        /// <summary>이미지(<c>![대체 텍스트](경로)</c>만 있는 줄). 대체 텍스트는 <see cref="EditorBlock.Text"/>, 경로는 <see cref="EditorBlock.Source"/>.</summary>
        Image,
        /// <summary>문서 내 제목 블록들을 모아 보여주는 자동 목차(<c>[TOC]</c>).</summary>
        Toc,
        /// <summary>수식 블록(<c>$$ ... $$</c>). 원문 LaTeX 수식은 <see cref="EditorBlock.Text"/>에 담는다.</summary>
        Math,
        /// <summary>파일 첨부 카드(<c>[📎 이름](경로)</c>). 표시 이름은 <see cref="EditorBlock.Text"/>, 경로는 <see cref="EditorBlock.Source"/>.</summary>
        File,
        /// <summary>웹 북마크 카드(<c>[🔖 제목](URL)</c>). 표시 제목은 <see cref="EditorBlock.Text"/>, URL은 <see cref="EditorBlock.Source"/>.</summary>
        Bookmark,
        /// <summary>다른 문서(페이지 = <c>.md</c> 파일)로 가는 링크 카드(<c>[📄 제목](경로.md)</c>). 제목은 <see cref="EditorBlock.Text"/>, 경로는 <see cref="EditorBlock.Source"/>.</summary>
        Page,
        /// <summary>
        /// 문서 맨 앞 YAML front matter(<c>---</c> … <c>---</c>). <see cref="EditorBlock.Text"/>에 구분 줄까지 원문을 담고 저장할 때 그대로 쓴다(<see cref="Raw"/>와 같음).
        /// 화면에는 속성 표(키 | 값)로 보이고, 편집은 <c>Services/FrontMatter</c>로 그 속성 줄만 다시 쓴다. 원문 편집으로 바꾸면 잠시 <see cref="Raw"/>가 된다.
        /// </summary>
        FrontMatter,
    }

    /// <summary>
    /// 블록 하나. 문서는 블록의 평면 목록이고, 중첩은 <see cref="Indent"/>로만 표현한다
    /// (바로 뒤에 있는 더 깊은 블록들이 자식). WPF 형식을 쓰지 않아 테스트 프로젝트에서 소스 링크로 쓴다.
    /// </summary>
    public sealed class EditorBlock
    {
        private static long _lastId;

        /// <summary>
        /// 블록 식별자. <see cref="Clone"/>은 같은 값을 유지해 실행 취소 스냅샷에서 되돌린 블록이 원래 화면 행을 다시 쓰게 하고,
        /// 복제(<see cref="Duplicate"/>)·새 블록은 새 값을 받는다.
        /// </summary>
        public long Id { get; private set; } = Interlocked.Increment(ref _lastId);

        private BlockKind _kind;
        private string _text = string.Empty;

        /// <summary>블록 종류. 줄 안 범위 속성(<see cref="TextMarks"/>)을 받지 않는 종류(코드·원문·표 행 등)로 바뀌면 그 속성을 지운다.</summary>
        public BlockKind Kind
        {
            get => _kind;
            set
            {
                _kind = value;
                if (Attributes != null && !TextMarks.IsTarget(value))
                    SetAttribute(TextMarks.Attribute, null);
            }
        }

        /// <summary>
        /// 본문. 굵게·링크 등 줄 안 서식은 Markdown 원문(<c>**굵게**</c>) 그대로 담는다.
        /// 줄 안 범위 속성(밑줄·글자 색·형광펜, <see cref="TextMarks"/>)이 있으면 바뀐 글자에 맞춰 범위를 옮긴다(찾아 바꾸기·<c>/</c> 명령·날짜 넣기 등 모든 경로).
        /// </summary>
        public string Text
        {
            get => _text;
            set
            {
                if (Attributes != null && !string.Equals(_text, value, StringComparison.Ordinal) && Attributes.ContainsKey(TextMarks.Attribute))
                    TextMarks.OnTextChanging(this, _text, value);
                _text = value;
            }
        }

        /// <summary>범위 속성을 옮기지 않고 본문만 바꾼다(<see cref="TextMarks.SetText"/> — 범위를 따로 정하는 경로).</summary>
        internal void SetTextOnly(string text)
        {
            _text = text;
        }

        public int Indent { get; set; }

        /// <summary>할 일 블록의 체크 상태.</summary>
        public bool Checked { get; set; }

        /// <summary>토글 블록이 접혀 있는지. Markdown에는 <c>&lt;details&gt;</c>의 <c>open</c> 속성으로 저장한다.</summary>
        public bool Collapsed { get; set; }

        /// <summary>코드 블록 언어(펜스 정보 문자열).</summary>
        public string Language { get; set; } = string.Empty;

        /// <summary>콜아웃 종류(GitHub 알림 <c>[!NOTE]</c>의 NOTE·TIP·IMPORTANT·WARNING·CAUTION).</summary>
        public string CalloutType { get; set; } = "NOTE";

        /// <summary>이미지 블록의 경로·URL(Markdown 원문 그대로, 상대 경로는 문서 폴더 기준).</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>표 머리글 행의 열 정렬(열마다 <c>l</c>·<c>c</c>·<c>r</c>·빈 값을 쉼표로 구분). 머리글이 아닌 행에서는 쓰지 않는다.</summary>
        public string TableAlign { get; set; } = string.Empty;

        /// <summary>표 행의 셀 구분자.</summary>
        public const char CellSeparator = '\t';

        public const string AttrWidth = "width";
        public const string AttrAlign = "align";
        public const string AttrHeading = "heading";
        public const string AttrIcon = "icon";
        public const string AttrTitle = "title";
        public const string AttrDescription = "description";
        public const string AttrSize = "size";

        /// <summary>
        /// 여러 열 배치: <c>묶음id/열번호</c>(예: <c>k3f9a2/1</c>). 같은 묶음id로 이어진 블록들이 나란히 놓인 열 묶음이고, Markdown에는 1열 → 2열 순서대로 쓴다.
        /// 정리 규칙은 <c>BlockOperations.ColumnLayout</c>.
        /// </summary>
        public const string AttrColumn = "column";

        /// <summary>열 묶음 첫 블록에만 두는 열 너비 비율(예: <c>1:2</c>). 없으면 같은 너비.</summary>
        public const string AttrColumnRatio = "colratio";

        /// <summary>
        /// 표 열 너비(px, 열마다 쉼표 구분, 빈 값은 자동 — 예: <c>120,,200</c>). 표의 첫 행(머리글, <see cref="TableAlign"/>과 같은 블록)에만 둔다.
        /// 규칙은 <c>BlockTable</c>.
        /// </summary>
        public const string AttrColWidths = "colwidths";

        /// <summary>줄 안 밑줄·글자 색·형광펜 범위(#76 9번). 형식·규칙은 <see cref="TextMarks"/>.</summary>
        public const string AttrMarks = TextMarks.Attribute;

        /// <summary>표 머리글 행 표시 끄기(<c>true</c>). Markdown 표는 머리글이 필수라 첫 행은 그대로 머리글로 저장하고 화면에서만 일반 행처럼 그린다. 표의 첫 행에만 둔다.</summary>
        public const string AttrNoHeader = "noheader";

        /// <summary>토글 제목 단계(1~3). 일반 토글이나 다른 블록이면 0.</summary>
        public int GetToggleHeadingLevel()
        {
            if (Kind != BlockKind.Toggle)
                return 0;
            var h = GetAttribute(AttrHeading);
            if (h == "1") return 1;
            if (h == "2") return 2;
            if (h == "3") return 3;
            return 0;
        }

        /// <summary>제목 단계(1~3). <see cref="BlockKind.Heading1"/>~<see cref="BlockKind.Heading3"/> 또는 토글 제목이면 1~3, 아니면 0.</summary>
        public int GetHeadingLevel()
        {
            switch (Kind)
            {
                case BlockKind.Heading1: return 1;
                case BlockKind.Heading2: return 2;
                case BlockKind.Heading3: return 3;
                case BlockKind.Toggle: return GetToggleHeadingLevel();
                default: return 0;
            }
        }

        /// <summary>
        /// Markdown으로 나타낼 수 없는 속성(블록 글자색 <c>color</c>·배경색 <c>background</c> 등). Markdown은 순수하게 두고
        /// 문서 옆 사이드카(<c>문서.md.meta.json</c>, <c>BlockSidecar</c>)에 저장한다. 속성이 없으면 null.
        /// </summary>
        public SortedDictionary<string, string> Attributes { get; private set; }

        public string GetAttribute(string key)
        {
            string value;
            return Attributes != null && Attributes.TryGetValue(key, out value) ? value : null;
        }

        /// <summary>속성을 넣는다. 값이 비면 지운다.</summary>
        public void SetAttribute(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (Attributes != null && Attributes.Remove(key) && Attributes.Count == 0)
                    Attributes = null;
                return;
            }
            if (Attributes == null)
                Attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            Attributes[key] = value;
        }

        public EditorBlock() { }

        public EditorBlock(BlockKind kind, string text = "", int indent = 0)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            Indent = indent;
        }

        /// <summary>같은 <see cref="Id"/>의 복사본(실행 취소 스냅샷용).</summary>
        public EditorBlock Clone()
        {
            var copy = Duplicate();
            copy.Id = Id;
            return copy;
        }

        /// <summary>내용은 같고 <see cref="Id"/>는 새로운 블록(블록 복제용).</summary>
        public EditorBlock Duplicate()
        {
            return new EditorBlock
            {
                Kind = Kind,
                Text = Text,
                Indent = Indent,
                Checked = Checked,
                Collapsed = Collapsed,
                Language = Language,
                CalloutType = CalloutType,
                Source = Source,
                TableAlign = TableAlign,
                Attributes = Attributes == null ? null : new SortedDictionary<string, string>(Attributes, StringComparer.Ordinal),
            };
        }

        /// <summary>목록·토글처럼 Tab으로 자식을 둘 수 있는 블록인지.</summary>
        public static bool IsContainer(BlockKind kind)
        {
            return kind == BlockKind.Bulleted || kind == BlockKind.Numbered || kind == BlockKind.Todo || kind == BlockKind.Toggle;
        }

        /// <summary>Markdown 목록 항목(줄 사이 빈 줄 없이 이어 쓰는 블록)인지.</summary>
        public static bool IsListItem(BlockKind kind)
        {
            return kind == BlockKind.Bulleted || kind == BlockKind.Numbered || kind == BlockKind.Todo;
        }

        /// <summary>여러 줄 원문을 그대로 다루는 블록(Enter가 줄바꿈)인지.</summary>
        public static bool IsVerbatim(BlockKind kind)
        {
            return kind == BlockKind.Code || kind == BlockKind.Raw || kind == BlockKind.Math;
        }

        /// <summary>
        /// 본문이 줄 안 Markdown 서식(굵게·기울임·코드·링크 등, #76)을 갖는 블록인지 — 표시·편집 때 표식 없이 서식으로 보인다.
        /// 원문 블록(코드·원문·수식)과 캡션·카드 제목, 구분선·목차는 아니다. 표 행은 셀마다 서식(셀 편집 입력란은 원문).
        /// </summary>
        public static bool HasInlineFormatting(BlockKind kind)
        {
            return !IsVerbatim(kind) && kind != BlockKind.FrontMatter && kind != BlockKind.Image && !IsLinkCard(kind) && kind != BlockKind.Divider && kind != BlockKind.Toc;
        }

        /// <summary>경로(<see cref="Source"/>)와 제목을 카드로 보여 주는 블록(파일 첨부·북마크·페이지 링크)인지.</summary>
        public static bool IsLinkCard(BlockKind kind)
        {
            return kind == BlockKind.File || kind == BlockKind.Bookmark || kind == BlockKind.Page;
        }

        /// <summary>내용(식별자 제외)이 같은지. 실행 취소 기록이 바뀐 블록만 저장하는 데 쓴다.</summary>
        public bool SameContent(EditorBlock other)
        {
            return other != null && Kind == other.Kind && Indent == other.Indent && Checked == other.Checked &&
                   Collapsed == other.Collapsed && string.Equals(Text, other.Text) && string.Equals(Language, other.Language) &&
                   string.Equals(CalloutType, other.CalloutType) && string.Equals(Source, other.Source) &&
                   string.Equals(TableAlign, other.TableAlign) && SameAttributes(other);
        }

        private bool SameAttributes(EditorBlock other)
        {
            var a = Attributes;
            var b = other.Attributes;
            if (a == null || b == null)
                return a == null && b == null;
            if (a.Count != b.Count)
                return false;
            foreach (var pair in a)
            {
                string value;
                if (!b.TryGetValue(pair.Key, out value) || value != pair.Value)
                    return false;
            }
            return true;
        }

        public override string ToString()
        {
            return string.Format("{0}[{1}] {2}", Kind, Indent, Text);
        }
    }
}
