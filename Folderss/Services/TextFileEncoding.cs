using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Folderss.Services
{
    /// <summary>
    /// 일반 텍스트 편집기(<c>builtin:text</c>)의 인코딩 감지·선택·저장 검사.
    /// 감지 순서: BOM(UTF-8·UTF-16 LE/BE) → BOM 없는 UTF-8(엄격 검사) → 실패하면 CP949(EUC-KR 확장, 한국어 Windows 기본 ANSI).
    /// </summary>
    public static class TextFileEncoding
    {
        static TextFileEncoding()
        {
            // 정적 초기화식은 정적 생성자 본문보다 먼저 실행되므로, CP949를 쓰는 값은 등록 뒤 여기서 만든다.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Cp949 = Encoding.GetEncoding(949);
            Choices = new List<KeyValuePair<string, Encoding>>
            {
                new KeyValuePair<string, Encoding>("UTF-8", new UTF8Encoding(false)),
                new KeyValuePair<string, Encoding>("UTF-8 BOM", new UTF8Encoding(true)),
                new KeyValuePair<string, Encoding>("UTF-16 LE", new UnicodeEncoding(false, true)),
                new KeyValuePair<string, Encoding>("UTF-16 BE", new UnicodeEncoding(true, true)),
                new KeyValuePair<string, Encoding>("CP949 (EUC-KR)", Cp949),
            };
        }

        /// <summary>CP949(EUC-KR 확장). BOM 없는 파일이 UTF-8로 읽히지 않을 때 쓴다.</summary>
        public static Encoding Cp949 { get; }

        /// <summary>상태 표시줄 인코딩 메뉴에 보이는 선택지(표시 이름, 인코딩). 순서대로 표시한다.</summary>
        public static IReadOnlyList<KeyValuePair<string, Encoding>> Choices { get; }

        /// <summary>상태 표시줄에 쓰는 짧은 이름(예: <c>UTF-8 BOM</c>, <c>CP949</c>).</summary>
        public static string DisplayName(Encoding encoding)
        {
            if (encoding == null)
                return string.Empty;
            var hasBom = encoding.GetPreamble().Length > 0;
            switch (encoding.CodePage)
            {
                case 65001: return hasBom ? "UTF-8 BOM" : "UTF-8";
                case 1200: return "UTF-16 LE";
                case 1201: return "UTF-16 BE";
                case 949: return "CP949";
                default: return encoding.WebName.ToUpperInvariant();
            }
        }

        /// <summary>같은 인코딩인지(코드 페이지와 BOM 유무가 같으면 같다).</summary>
        public static bool SameEncoding(Encoding a, Encoding b)
        {
            return a != null && b != null && a.CodePage == b.CodePage
                && a.GetPreamble().Length > 0 == b.GetPreamble().Length > 0;
        }

        /// <summary>바이트를 감지한 인코딩으로 읽는다. BOM은 글에 넣지 않는다.</summary>
        public static string Decode(byte[] bytes, out Encoding encoding)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                encoding = new UTF8Encoding(true);
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = new UnicodeEncoding(false, true);
                return encoding.GetString(bytes, 2, bytes.Length - 2);
            }
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = new UnicodeEncoding(true, true);
                return encoding.GetString(bytes, 2, bytes.Length - 2);
            }

            // BOM 없는 파일: 엄격한 UTF-8로 한 번에 읽어 보고(검사와 읽기를 한 번에), 잘못된 바이트가 있으면 CP949로 읽는다.
            try
            {
                var text = new UTF8Encoding(false, true).GetString(bytes);
                encoding = new UTF8Encoding(false);
                return text;
            }
            catch (DecoderFallbackException)
            {
                encoding = Cp949;
                return encoding.GetString(bytes);
            }
        }

        /// <summary>지정한 인코딩으로 읽는다(인코딩 바꿔 다시 열기). 파일에 있는 BOM은 글에 넣지 않는다.</summary>
        public static string DecodeAs(byte[] bytes, Encoding encoding)
        {
            var skip = 0;
            if (encoding.CodePage == 65001 && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                skip = 3;
            else if (encoding.CodePage == 1200 && bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                skip = 2;
            else if (encoding.CodePage == 1201 && bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                skip = 2;
            return encoding.GetString(bytes, skip, bytes.Length - skip);
        }

        /// <summary>
        /// 이 인코딩으로 저장하면 사라지는 글자(예: CP949에 없는 이모지)를 찾는다. 없으면 null, 있으면 처음 몇 개를 돌려준다.
        /// CP949는 없는 글자를 말없이 '?'로 바꾸므로 저장 전에 검사해 알린다.
        /// </summary>
        public static string FindUnencodable(string text, Encoding encoding, int maxSamples = 5)
        {
            if (string.IsNullOrEmpty(text) || encoding.CodePage == 65001 || encoding.CodePage == 1200 || encoding.CodePage == 1201)
                return null;   // 유니코드 인코딩은 모든 글자를 저장한다

            var strict = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            try
            {
                strict.GetByteCount(text);
                return null;
            }
            catch (EncoderFallbackException)
            {
            }

            // 어떤 글자인지 보여 주기 위해 한 글자(서로게이트 쌍은 함께)씩 다시 검사한다.
            var samples = new List<string>();
            for (int i = 0; i < text.Length && samples.Count < maxSamples; i++)
            {
                var length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
                var piece = text.Substring(i, length);
                try
                {
                    strict.GetByteCount(piece);
                }
                catch (EncoderFallbackException)
                {
                    if (!samples.Contains(piece))
                        samples.Add(piece);
                }
                i += length - 1;
            }
            return string.Join(" ", samples);
        }

        /// <summary>글에서 많이 쓰인 줄 끝(<c>\r\n</c> 또는 <c>\n</c>). 줄 끝이 없으면 Windows 기본 CRLF.</summary>
        public static string DetectNewLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "\r\n";
            int crlf = 0, lf = 0;
            // 큰 파일도 앞부분(최대 1MB)만 보면 충분하다.
            var limit = Math.Min(text.Length, 1 << 20);
            for (int i = 0; i < limit; i++)
            {
                if (text[i] != '\n')
                    continue;
                if (i > 0 && text[i - 1] == '\r')
                    crlf++;
                else
                    lf++;
            }
            return lf > crlf ? "\n" : "\r\n";
        }

        /// <summary>가장 긴 줄의 글자 수(줄 끝 제외). 긴 줄이 있으면 편집기가 구문 강조를 끄고 앞부분만 그린다.</summary>
        public static int MaxLineLength(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            int max = 0, start = 0;
            while (true)
            {
                var end = text.IndexOfAny(LineBreaks, start);   // 벡터화된 검색이라 수십 MB도 빠르다
                var length = (end < 0 ? text.Length : end) - start;
                if (length > max)
                    max = length;
                if (end < 0)
                    return max;
                start = end + 1;
                if (text[end] == '\r' && start < text.Length && text[start] == '\n')
                    start++;
            }
        }

        private static readonly char[] LineBreaks = { '\r', '\n' };

        /// <summary>모든 줄 끝을 <paramref name="newLine"/>으로 바꾼다(<c>\r\n</c>, 홀로 있는 <c>\r</c>·<c>\n</c> 모두).</summary>
        public static string NormalizeNewLines(string text, string newLine)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            var sb = new StringBuilder(text.Length + text.Length / 50);
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                        i++;
                    sb.Append(newLine);
                }
                else if (c == '\n')
                    sb.Append(newLine);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>파일을 읽기 공유로 연다(다른 프로그램이 쓰는 중인 로그도 읽을 수 있게).</summary>
        public static byte[] ReadAllBytesShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var length = fs.Length;
                if (length > int.MaxValue)
                    throw new IOException("파일이 너무 큽니다.");
                var bytes = new byte[length];
                int read = 0;
                while (read < bytes.Length)
                {
                    var n = fs.Read(bytes, read, bytes.Length - read);
                    if (n == 0)
                        break;
                    read += n;
                }
                if (read < bytes.Length)
                    Array.Resize(ref bytes, read);   // 읽는 사이 줄어든 파일
                return bytes;
            }
        }
    }
}
