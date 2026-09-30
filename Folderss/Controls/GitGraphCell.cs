using System.Windows;
using System.Windows.Media;
using Folderss.Models;
using Folderss.Services;

namespace Folderss.Controls
{
    /// <summary>
    /// 로그 목록 한 행의 브랜치 그래프를 그린다(<see cref="GitGraphLayout"/>이 계산한 선분). 행 높이 전체를 쓰므로
    /// 행 사이 여백이 없어야 선이 이어진다 — 로그 목록의 ItemContainerStyle이 Padding·Border를 0으로 둔다.
    /// 레인 색은 테마와 무관한 중간 채도 색이라 어두운·밝은 테마 모두에서 보인다.
    /// </summary>
    public sealed class GitGraphCell : FrameworkElement
    {
        public const double LaneWidth = 12;
        public const int MaxLanes = 24;

        private static readonly Brush[] Palette =
        {
            Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF))),
            Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75))),
            Freeze(new SolidColorBrush(Color.FromRgb(0x98, 0xC3, 0x79))),
            Freeze(new SolidColorBrush(Color.FromRgb(0xD1, 0x9A, 0x66))),
            Freeze(new SolidColorBrush(Color.FromRgb(0xC6, 0x78, 0xDD))),
            Freeze(new SolidColorBrush(Color.FromRgb(0x56, 0xB6, 0xC2))),
            Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0xC0, 0x7B))),
            Freeze(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)))
        };

        private static readonly Pen[] Pens = CreatePens();

        public static readonly DependencyProperty RowProperty = DependencyProperty.Register(
            nameof(Row), typeof(GitGraphRow), typeof(GitGraphCell),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public GitGraphRow Row
        {
            get => (GitGraphRow)GetValue(RowProperty);
            set => SetValue(RowProperty, value);
        }

        /// <summary>레인 수에 맞는 열 너비.</summary>
        public static double WidthFor(int laneCount)
        {
            // 머리글 "그래프"가 보일 최소 폭은 둔다.
            return System.Math.Max(40, System.Math.Min(System.Math.Max(laneCount, 1), MaxLanes) * LaneWidth + 4);
        }

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        private static Pen[] CreatePens()
        {
            var pens = new Pen[Palette.Length];
            for (var i = 0; i < pens.Length; i++)
            {
                pens[i] = new Pen(Palette[i], 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                pens[i].Freeze();
            }
            return pens;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var row = Row;
            if (row == null)
                return;

            var height = ActualHeight;
            // 빈 영역도 적중 테스트가 되도록(행 선택 클릭) 투명하게 칠한다.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, height));

            foreach (var segment in row.Segments)
            {
                if (segment.FromLane >= MaxLanes || segment.ToLane >= MaxLanes)
                    continue;
                // 점에서 나가는 선은 도착 레인 색, 나머지는 출발 레인 색.
                var colorLane = segment.FromY == GitGraphLayout.Center ? segment.ToLane : segment.FromLane;
                dc.DrawLine(Pens[colorLane % Pens.Length],
                    new Point(X(segment.FromLane), Y(segment.FromY, height)),
                    new Point(X(segment.ToLane), Y(segment.ToY, height)));
            }

            if (row.NodeLane < MaxLanes)
            {
                var center = new Point(X(row.NodeLane), height / 2);
                var brush = Palette[row.NodeLane % Palette.Length];
                if (row.IsMerge)
                    dc.DrawEllipse(null, Pens[row.NodeLane % Pens.Length], center, 3.5, 3.5);   // 병합 커밋은 속이 빈 점
                else
                    dc.DrawEllipse(brush, null, center, 3.5, 3.5);
            }
        }

        private static double X(int lane) => lane * LaneWidth + LaneWidth / 2 + 2;

        private static double Y(int position, double height)
        {
            switch (position)
            {
                case GitGraphLayout.Top: return 0;
                case GitGraphLayout.Center: return height / 2;
                default: return height;
            }
        }
    }
}
