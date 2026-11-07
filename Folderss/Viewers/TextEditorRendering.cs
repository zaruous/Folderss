using Folderss.Services;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Folderss.Viewers
{
    /// <summary>
    /// AvalonEdit 기본 구문 색(밝은 배경용)을 어두운 테마에서도 읽히게 그리는 colorizer.
    /// 정의 자체는 앱 전체가 함께 쓰므로 고치지 않고, 그릴 때만 밝기를 올린 복사본 색을 쓴다(색마다 한 번 만들어 둠).
    /// </summary>
    internal sealed class ThemedHighlightingColorizer : HighlightingColorizer
    {
        private readonly Dictionary<HighlightingColor, HighlightingColor> _darkColors = new Dictionary<HighlightingColor, HighlightingColor>();
        private bool _dark;

        public ThemedHighlightingColorizer(IHighlightingDefinition definition) : base(definition)
        {
        }

        public bool Dark
        {
            get => _dark;
            set => _dark = value;
        }

        protected override void ApplyColorToElement(VisualLineElement element, HighlightingColor color)
        {
            base.ApplyColorToElement(element, _dark && color != null ? DarkVariant(color) : color);
        }

        private HighlightingColor DarkVariant(HighlightingColor color)
        {
            HighlightingColor adjusted;
            if (_darkColors.TryGetValue(color, out adjusted))
                return adjusted;

            adjusted = color.Clone();
            var fg = ColorOf(color.Foreground);
            if (fg.HasValue)
                adjusted.Foreground = new SimpleHighlightingBrush(Lighten(fg.Value));
            var bg = ColorOf(color.Background);
            if (bg.HasValue)
                adjusted.Background = new SimpleHighlightingBrush(Color.FromArgb(0x40, bg.Value.R, bg.Value.G, bg.Value.B));
            _darkColors[color] = adjusted;
            return adjusted;
        }

        private static Color? ColorOf(HighlightingBrush brush)
        {
            if (brush == null)
                return null;
            try
            {
                return brush.GetColor(null);
            }
            catch (Exception ex) when (ex is NullReferenceException || ex is InvalidOperationException)
            {
                return null;   // 문맥이 필요한 브러시(시스템 색)는 그대로 둔다
            }
        }

        /// <summary>어두운 바탕에서 읽히도록 밝기(HSL의 L)를 최소 0.66으로 올린다. 색상·채도는 유지한다.</summary>
        internal static Color Lighten(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, h = 0, s = 0;
            if (max != min)
            {
                var d = max - min;
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6;
            }
            if (l >= 0.66)
                return c;
            l = 0.66 + l * 0.15;
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            return Color.FromArgb(c.A, ToByte(Hue(p, q, h + 1.0 / 3)), ToByte(Hue(p, q, h)), ToByte(Hue(p, q, h - 1.0 / 3)));
        }

        private static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        private static byte ToByte(double v) => (byte)Math.Round(Math.Max(0, Math.Min(1, v)) * 255);
    }

    /// <summary>
    /// 아주 긴 줄은 앞 <see cref="Limit"/>자만 그리고 나머지는 "… (N자 더)" 표시 하나로 대신한다(VS Code의 stopRenderingLineAfter와 같은 방식).
    /// WPF 글자 배치는 줄 전체를 한 번에 하므로(가로 가상화 없음) 220만 자 한 줄이 열리는 데 19초·메모리 +640MB가 들었다.
    /// 문서 내용은 그대로라 저장·찾기·복사·실행 취소는 줄 전체에 동작한다. 가려진 부분은 화면에서 편집할 수 없다.
    /// </summary>
    internal sealed class LongLineElementGenerator : VisualLineElementGenerator
    {
        public const int Limit = 10000;

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var line = CurrentContext.Document.GetLineByOffset(startOffset);
            if (line.Length <= Limit)
                return -1;
            var cut = CutOffset(line);
            return startOffset <= cut ? cut : -1;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            var line = CurrentContext.Document.GetLineByOffset(offset);
            if (line.Length <= Limit || offset != CutOffset(line))
                return null;
            var hidden = line.EndOffset - offset;
            return new FormattedTextElement(string.Format("  … ({0:N0}자 더 — 긴 줄은 앞 {1:N0}자만 표시)", hidden, Limit), hidden);
        }

        /// <summary>자르는 위치. 서로게이트 쌍(이모지 등)을 가르지 않는다.</summary>
        private int CutOffset(ICSharpCode.AvalonEdit.Document.DocumentLine line)
        {
            var cut = line.Offset + Limit;
            if (char.IsHighSurrogate(CurrentContext.Document.GetCharAt(cut - 1)))
                cut--;
            return cut;
        }
    }

    /// <summary>찾기 결과를 모두 강조한다. 화면에 보이는 줄에 걸친 일치만 이진 탐색으로 골라 그린다(큰 파일에서도 가볍게).</summary>
    internal sealed class SearchResultRenderer : IBackgroundRenderer
    {
        public IReadOnlyList<TextMatch> Matches { get; set; } = Array.Empty<TextMatch>();
        public int CurrentIndex { get; set; } = -1;
        public Brush MatchBrush { get; set; }
        public Brush CurrentBrush { get; set; }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            var matches = Matches;
            if (matches == null || matches.Count == 0 || !textView.VisualLinesValid || textView.VisualLines.Count == 0)
                return;

            var lines = textView.VisualLines;
            var start = lines[0].FirstDocumentLine.Offset;
            var last = lines[lines.Count - 1].LastDocumentLine;
            var end = last.EndOffset;

            // 화면 첫 위치 이상에서 시작하는 첫 일치(하한). 그 앞 일치가 여러 줄로 화면까지 이어질 수 있어 하나 앞에서 시작한다.
            int lo = 0, hi = matches.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (matches[mid].Offset < start) lo = mid + 1; else hi = mid;
            }
            var i = Math.Max(0, lo - 1);
            for (; i < matches.Count && matches[i].Offset <= end; i++)
            {
                var m = matches[i];
                if (m.EndOffset < start)
                    continue;
                var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
                builder.AddSegment(textView, new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = m.Offset, Length = m.Length });
                var geometry = builder.CreateGeometry();
                if (geometry != null)
                    drawingContext.DrawGeometry(i == CurrentIndex ? CurrentBrush : MatchBrush, null, geometry);
            }
        }
    }
}
