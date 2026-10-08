using Folderss.Services;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// 캡쳐 편집 도형 값: 불변(되돌리기 기록 보존), 이동·자르기·크기 조절 변환, 선택 판정, 핸들 끌기.
    /// </summary>
    public sealed class CaptureAnnotationTests
    {
        private const uint Red = 0xFFE53935;

        private static CaptureAnnotation Rect(double x1, double y1, double x2, double y2, double thickness = 4)
        {
            return new CaptureAnnotation(CaptureAnnotationKind.Rectangle, x1, y1, x2, y2, Red, thickness);
        }

        private static CaptureAnnotation Arrow(double x1, double y1, double x2, double y2, double thickness = 4)
        {
            return new CaptureAnnotation(CaptureAnnotationKind.Arrow, x1, y1, x2, y2, Red, thickness);
        }

        private static void AssertPoints(CaptureAnnotation a, double x1, double y1, double x2, double y2)
        {
            Assert.Equal(x1, a.X1, 6);
            Assert.Equal(y1, a.Y1, 6);
            Assert.Equal(x2, a.X2, 6);
            Assert.Equal(y2, a.Y2, 6);
        }

        [Fact]
        public void Translate_ReturnsNewValue_OriginalUnchanged()
        {
            var original = Rect(10, 10, 50, 40);
            var moved = original.Translate(5, -3);
            AssertPoints(moved, 15, 7, 55, 37);
            AssertPoints(original, 10, 10, 50, 40);
            Assert.Equal(original.Argb, moved.Argb);
            Assert.Equal(original.Thickness, moved.Thickness);
        }

        [Fact]
        public void Transform_Crop_ShiftsByCropOrigin()
        {
            var cropped = Rect(110, 220, 150, 260).Transform(-100, -200, 1, 1);
            AssertPoints(cropped, 10, 20, 50, 60);
            Assert.Equal(4, cropped.Thickness, 6);
        }

        [Fact]
        public void Transform_Resize_ScalesPointsThicknessAndFont()
        {
            var text = new CaptureAnnotation(CaptureAnnotationKind.Text, 10, 10, 60, 30, Red, 0, 18, "hi");
            var half = text.Transform(0, 0, 0.5, 0.5);
            AssertPoints(half, 5, 5, 30, 15);
            Assert.Equal(9, half.FontSize, 6);
            Assert.Equal("hi", half.Text);

            // 비율을 풀고 가로만 4배: 굵기는 기하 평균(√4 = 2배).
            var wide = Rect(0, 0, 10, 10, 3).Transform(0, 0, 4, 1);
            AssertPoints(wide, 0, 0, 40, 10);
            Assert.Equal(6, wide.Thickness, 6);
        }

        [Fact]
        public void Normalized_BoxesStartTopLeft_ArrowsKeepDirection()
        {
            AssertPoints(Rect(50, 40, 10, 10).Normalized(), 10, 10, 50, 40);
            AssertPoints(Arrow(50, 40, 10, 10).Normalized(), 50, 40, 10, 10);
        }

        [Fact]
        public void HitTest_HollowRectangle_InsideCounts()
        {
            var rect = Rect(10, 10, 110, 60);
            Assert.True(rect.HitTest(60, 35, 0));          // 속이 비어도 안쪽 클릭으로 잡힌다
            Assert.True(rect.HitTest(10 - 2 - 5, 35, 5));  // 굵기 절반(2) + 여유(5)
            Assert.False(rect.HitTest(10 - 8, 35, 5));
        }

        [Fact]
        public void HitTest_Arrow_UsesDistanceFromLine()
        {
            var arrow = Arrow(0, 0, 100, 100);
            Assert.True(arrow.HitTest(50, 50, 0));
            Assert.True(arrow.HitTest(50, 57, 4));   // 선과의 거리 ≈ 4.95 ≤ 여유 4 + 굵기 절반 2
            Assert.False(arrow.HitTest(90, 10, 5));  // 테두리 상자 안이지만 선에서 멀다
            Assert.False(arrow.HitTest(110, 110, 3)); // 끝점 너머
        }

        [Fact]
        public void TopmostHit_PrefersLaterDrawn()
        {
            var list = new[] { Rect(0, 0, 100, 100), Rect(40, 40, 60, 60) };
            Assert.Equal(1, CaptureAnnotation.TopmostHit(list, 50, 50, 0));
            Assert.Equal(0, CaptureAnnotation.TopmostHit(list, 10, 10, 0));
            Assert.Equal(-1, CaptureAnnotation.TopmostHit(list, 300, 300, 0));
        }

        [Fact]
        public void Handles_ByKind()
        {
            Assert.Equal(8, Rect(0, 0, 10, 10).Handles().Count);
            Assert.Equal(2, Arrow(0, 0, 10, 10).Handles().Count);
            Assert.Empty(new CaptureAnnotation(CaptureAnnotationKind.Text, 0, 0, 10, 10, Red, 0, 18, "a").Handles());
        }

        [Fact]
        public void HitHandle_FindsCornerAndEdgeCenters()
        {
            var rect = Rect(10, 20, 110, 80);
            Assert.Equal(CaptureHandle.TopLeft, rect.HitHandle(12, 21, 4));
            Assert.Equal(CaptureHandle.Bottom, rect.HitHandle(60, 80, 4));
            Assert.Equal(CaptureHandle.Right, rect.HitHandle(110, 50, 4));
            Assert.Equal(CaptureHandle.None, rect.HitHandle(60, 50, 4));
            Assert.Equal(CaptureHandle.End, Arrow(0, 0, 100, 50).HitHandle(99, 51, 4));
        }

        [Fact]
        public void DragHandle_MovesOnlyGrabbedSides()
        {
            var rect = Rect(10, 20, 110, 80);
            AssertPoints(rect.DragHandle(CaptureHandle.TopLeft, 0, 5), 0, 5, 110, 80);
            AssertPoints(rect.DragHandle(CaptureHandle.Right, 150, 999), 10, 20, 150, 80);
            AssertPoints(rect.DragHandle(CaptureHandle.Bottom, 999, 100), 10, 20, 110, 100);
            AssertPoints(rect.DragHandle(CaptureHandle.BottomLeft, 30, 90), 30, 20, 110, 90);
        }

        [Fact]
        public void DragHandle_PastOppositeSide_FlipsThenNormalizes()
        {
            var flipped = Rect(10, 20, 110, 80).DragHandle(CaptureHandle.Left, 200, 0);
            AssertPoints(flipped.Normalized(), 110, 20, 200, 80);
        }

        private static CaptureAnnotation Image(double x1, double y1, double x2, double y2)
        {
            return new CaptureAnnotation(CaptureAnnotationKind.Image, x1, y1, x2, y2, 0, 0);
        }

        [Fact]
        public void Image_SelectsLikeBoxAndHasEightHandles()
        {
            var image = Image(0, 0, 400, 300);
            Assert.True(image.HitTest(200, 150, 0));
            Assert.True(image.HitTest(-5, 150, 6));   // 굵기 없음: 여유만큼만 바깥 허용
            Assert.False(image.HitTest(-7, 150, 6));
            Assert.Equal(8, image.Handles().Count);
            Assert.Equal(CaptureHandle.BottomRight, image.HitHandle(399, 301, 4));
            // 도형 아래에 있어도(먼저 그려짐) 겹치지 않은 곳에서는 이미지가 잡힌다.
            Assert.Equal(0, CaptureAnnotation.TopmostHit(new[] { image, Rect(10, 10, 50, 50) }, 200, 150, 0));
            Assert.Equal(1, CaptureAnnotation.TopmostHit(new[] { image, Rect(10, 10, 50, 50) }, 30, 30, 0));
        }

        [Fact]
        public void DragHandle_ImageCorner_KeepsRatioAnchoredAtOppositeCorner()
        {
            var image = Image(100, 100, 500, 400); // 4:3
            // 오른쪽 아래를 가로(200/400)로 세로(100/300)보다 더 많이 끌면 가로에 맞추고 세로는 비율대로.
            AssertPoints(image.DragHandle(CaptureHandle.BottomRight, 300, 200), 100, 100, 300, 250);
            // 세로(600/300)로 더 많이 끌면 세로에 맞춘다.
            AssertPoints(image.DragHandle(CaptureHandle.BottomRight, 490, 700), 100, 100, 900, 700);
            // 왼쪽 위는 오른쪽 아래를 고정한다.
            AssertPoints(image.DragHandle(CaptureHandle.TopLeft, 300, 350), 500, 400, 300, 250);
            // 반대편을 넘기면 뒤집힌 채로 두고 정리하면 고정점 너머에 놓인다(거울 반전은 없음).
            AssertPoints(image.DragHandle(CaptureHandle.BottomRight, -100, 0).Normalized(), -100, -50, 100, 100);
        }

        [Fact]
        public void DragHandle_ImageSide_StretchesFreely()
        {
            var image = Image(100, 100, 500, 400);
            AssertPoints(image.DragHandle(CaptureHandle.Right, 700, 999), 100, 100, 700, 400);
            AssertPoints(image.DragHandle(CaptureHandle.Top, 999, 50), 100, 50, 500, 400);
        }

        [Fact]
        public void Transform_Image_ScalesPlacementOnly()
        {
            var half = Image(0, 0, 400, 300).Transform(-100, -100, 0.5, 0.5);
            AssertPoints(half, -50, -50, 150, 100);
            Assert.Equal(0, half.Thickness, 6);
        }

        [Fact]
        public void DragHandle_ArrowEndsAndTextIgnores()
        {
            AssertPoints(Arrow(0, 0, 10, 10).DragHandle(CaptureHandle.Start, 5, 6), 5, 6, 10, 10);
            AssertPoints(Arrow(0, 0, 10, 10).DragHandle(CaptureHandle.End, 50, 60), 0, 0, 50, 60);
            var text = new CaptureAnnotation(CaptureAnnotationKind.Text, 0, 0, 10, 10, Red, 0, 18, "a");
            Assert.Same(text, text.DragHandle(CaptureHandle.TopLeft, 5, 5));
        }
    }
}
