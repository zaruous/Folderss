using System;
using System.Text;

namespace Folderss.Services
{
    /// <summary>
    /// zip 항목 이름 해석용 인코딩. .NET <c>ZipArchive</c>는 UTF-8 플래그(일반 목적 플래그 bit 11)가 켜진 항목은 UTF-8로 읽고,
    /// 꺼진 항목만 생성자에 넘긴 인코딩으로 읽는다. 플래그가 꺼진 이름은 만든 도구에 따라 CP949(한국어 Windows 탐색기·알집),
    /// UTF-8(macOS·리눅스), CP437(규격의 원래 문자 집합)일 수 있어 항목마다 바이트를 보고 판정한다.
    /// <list type="number">
    /// <item>모두 ASCII → 그대로</item>
    /// <item>엄격한 UTF-8로 읽히고 C1 제어 문자(U+0080–U+009F)가 없으면 UTF-8</item>
    /// <item>엄격한 CP949로 읽히면 CP949</item>
    /// <item>그 밖은 CP437(모든 바이트가 글자로 바뀌어 이름이 사라지지 않음)</item>
    /// </list>
    /// 쓰기(새 zip)에는 쓰지 않는다 — 새 zip은 .NET 기본(UTF-8 + 플래그)으로 만든다.
    /// </summary>
    public sealed class ZipEntryNameEncoding : Encoding
    {
        public static readonly ZipEntryNameEncoding Instance = new ZipEntryNameEncoding();

        /// <summary>플래그 없는 이름의 대체 코드 페이지(한국어 Windows OEM 코드 페이지).</summary>
        public const int LegacyCodePage = 949;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Encoding StrictLegacy;
        private static readonly Encoding Cp437;

        static ZipEntryNameEncoding()
        {
            // .NET(Core)은 CP949·CP437 같은 코드 페이지를 공급자를 등록해야 쓸 수 있다(GitTextDecoder와 같음). 여러 번 등록해도 된다.
            RegisterProvider(CodePagesEncodingProvider.Instance);
            StrictLegacy = GetEncoding(LegacyCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            Cp437 = GetEncoding(437);
        }

        private ZipEntryNameEncoding()
        {
        }

        /// <summary>플래그 없는 zip 이름 바이트를 위 순서로 판정해 읽는다.</summary>
        public static string Decode(byte[] bytes, int index, int count)
        {
            if (bytes == null || count <= 0)
                return string.Empty;

            var ascii = true;
            for (var i = index; i < index + count; i++)
            {
                if (bytes[i] >= 0x80)
                {
                    ascii = false;
                    break;
                }
            }
            if (ascii)
                return ASCII.GetString(bytes, index, count);

            var utf8 = TryDecode(StrictUtf8, bytes, index, count);
            if (utf8 != null && !ContainsC1Control(utf8))
                return utf8;

            var legacy = TryDecode(StrictLegacy, bytes, index, count);
            if (legacy != null)
                return legacy;

            return Cp437.GetString(bytes, index, count);
        }

        private static string TryDecode(Encoding encoding, byte[] bytes, int index, int count)
        {
            try
            {
                return encoding.GetString(bytes, index, count);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        // CP949 두 번째 바이트(0x81–0x9F)가 UTF-8 C2 xx로 읽히면 C1 제어 문자가 된다 — 이름에 쓸 일이 없는 글자라 UTF-8로 보지 않는다.
        private static bool ContainsC1Control(string text)
        {
            foreach (var ch in text)
            {
                if (ch >= '\u0080' && ch <= '\u009F')
                    return true;
            }
            return false;
        }

        public override string GetString(byte[] bytes, int index, int count)
        {
            return Decode(bytes, index, count);
        }

        public override int GetCharCount(byte[] bytes, int index, int count)
        {
            return Decode(bytes, index, count).Length;
        }

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            var text = Decode(bytes, byteIndex, byteCount);
            text.CopyTo(0, chars, charIndex, text.Length);
            return text.Length;
        }

        // 아래는 읽기 전용 인코딩이라 쓸 일이 없지만 Encoding 계약상 구현한다(UTF-8과 같게).
        public override int GetByteCount(char[] chars, int index, int count)
        {
            return UTF8.GetByteCount(chars, index, count);
        }

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
        {
            return UTF8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
        }

        public override int GetMaxByteCount(int charCount)
        {
            return UTF8.GetMaxByteCount(charCount);
        }

        public override int GetMaxCharCount(int byteCount)
        {
            // UTF-8·CP949·CP437 모두 바이트 하나가 글자 하나 이하로 바뀐다.
            return byteCount + 1;
        }
    }
}
