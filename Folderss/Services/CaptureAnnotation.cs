using System;
using System.Collections.Generic;

namespace Folderss.Services
{
    public enum CaptureAnnotationKind { Rectangle, Ellipse, Arrow, Text, Image }

    public enum CaptureHandle { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Start, End }

    /// <summary>
    /// 캡쳐 편집의 도형·텍스트·캡쳐 이미지 하나(바뀌지 않는 값). 좌표는 페이지 픽셀.
    /// 사각형·타원·텍스트·이미지는 (X1, Y1)–(X2, Y2)가 테두리 상자, 화살표는 시작점–끝점.
    /// 이미지는 캡쳐본이 페이지에 놓인 자리만 값으로 두고 그림(비트맵)은 편집면이 따로 든다(굵기·글자 없음).
    /// 텍스트의 상자는 확정할 때 잰 글자 크기다(이동·자르기·크기 조절 때 함께 옮기고 늘린다).
    /// 이동·핸들 끌기·자르기·크기 조절은 새 값을 돌려주므로 되돌리기 기록이 나중 편집에 오염되지 않는다.
    /// WPF 없음 — 테스트 프로젝트에서 소스 링크로 검증한다.
    /// </summary>
    public sealed class CaptureAnnotation
    {
        public CaptureAnnotation(CaptureAnnotationKind kind, double x1, double y1, double x2, double y2, uint argb, double thickness, double fontSize = 0, string text = null)
        {
            Kind = kind;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            Argb = argb;
            Thickness = thickness;
            FontSize = fontSize;
            Text = text;
        }

        public CaptureAnnotationKind Kind { get; }
        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public uint Argb { get; }
        public double Thickness { get; }
        public double FontSize { get; }
        public string Text { get; }

        public double Left => Math.Min(X1, X2);
        public double Top => Math.Min(Y1, Y2);
        public double Width => Math.Abs(X2 - X1);
        public double Height => Math.Abs(Y2 - Y1);

        public CaptureAnnotation WithPoints(double x1, double y1, double x2, double y2)
        {
            return new CaptureAnnotation(Kind, x1, y1, x2, y2, Argb, Thickness, FontSize, Text);
        }

        public CaptureAnnotation Translate(double dx, double dy)
        {
            return WithPoints(X1 + dx, Y1 + dy, X2 + dx, Y2 + dy);
        }

        /// <summary>
        /// 자르기·크기 조절에 맞춰 옮기고 늘린다: 새 좌표 = (좌표 + offset) × scale.
        /// 선 굵기와 글자 크기는 가로·세로 배율의 기하 평균으로 늘린다(비율을 풀고 늘려도 한쪽으로만 굵어지지 않게).
        /// </summary>
        public CaptureAnnotation Transform(double offsetX, double offsetY, double scaleX, double scaleY)
        {
            var scale = Math.Sqrt(scaleX * scaleY);
            return new CaptureAnnotation(Kind,
                (X1 + offsetX) * scaleX, (Y1 + offsetY) * scaleY,
                (X2 + offsetX) * scaleX, (Y2 + offsetY) * scaleY,
                Argb, Thickness * scale, FontSize * scale, Text);
        }

        /// <summary>상자형(사각형·타원·텍스트)은 (X1, Y1)이 왼쪽 위가 되게 정리한다. 화살표는 방향이 의미라 그대로.</summary>
        public CaptureAnnotation Normalized()
        {
            if (Kind == CaptureAnnotationKind.Arrow)
                return this;
            return WithPoints(Left, Top, Left + Width, Top + Height);
        }

        /// <summary>
        /// 이 점이 도형을 집는가. 상자형은 테두리 상자 안(속이 빈 도형도 안쪽 클릭으로 잡히게), 화살표는 선에서의 거리로 본다.
        /// <paramref name="tolerance"/>만큼 바깥도 허용한다.
        /// </summary>
        public bool HitTest(double x, double y, double tolerance)
        {
            if (Kind == CaptureAnnotationKind.Arrow)
                return DistanceToSegment(x, y, X1, Y1, X2, Y2) <= tolerance + Thickness / 2;
            var margin = tolerance + (Kind == CaptureAnnotationKind.Text ? 0 : Thickness / 2);
            return x >= Left - margin && x <= Left + Width + margin && y >= Top - margin && y <= Top + Height + margin;
        }

        /// <summary>크기 조절 핸들: 사각형·타원·이미지는 모서리·변 가운데 8개, 화살표는 양 끝 2개, 텍스트는 없음(이동만).</summary>
        public IReadOnlyList<(CaptureHandle Handle, double X, double Y)> Handles()
        {
            switch (Kind)
            {
                case CaptureAnnotationKind.Arrow:
                    return new[] { (CaptureHandle.Start, X1, Y1), (CaptureHandle.End, X2, Y2) };
                case CaptureAnnotationKind.Text:
                    return Array.Empty<(CaptureHandle, double, double)>();
                default:
                    var right = Left + Width;
                    var bottom = Top + Height;
                    var centerX = Left + Width / 2;
                    var centerY = Top + Height / 2;
                    return new[]
                    {
                        (CaptureHandle.TopLeft, Left, Top), (CaptureHandle.Top, centerX, Top), (CaptureHandle.TopRight, right, Top),
                        (CaptureHandle.Right, right, centerY), (CaptureHandle.BottomRight, right, bottom), (CaptureHandle.Bottom, centerX, bottom),
                        (CaptureHandle.BottomLeft, Left, bottom), (CaptureHandle.Left, Left, centerY)
                    };
            }
        }

        public CaptureHandle HitHandle(double x, double y, double tolerance)
        {
            foreach (var handle in Handles())
            {
                if (Math.Abs(x - handle.X) <= tolerance && Math.Abs(y - handle.Y) <= tolerance)
                    return handle.Handle;
            }
            return CaptureHandle.None;
        }

        /// <summary>
        /// 핸들을 (x, y)로 끈 결과. 상자형은 정리된(<see cref="Normalized"/>) 값에서 시작해 잡은 변만 옮긴다 —
        /// 반대편을 넘어가면 뒤집힌 채로 두고, 끝낼 때 <see cref="Normalized"/>로 정리한다.
        /// 이미지의 모서리 핸들은 비율을 유지한다(반대편 모서리를 고정하고 가로·세로 중 더 많이 끈 쪽에 맞춤). 변 핸들은 그 방향으로만 늘린다.
        /// </summary>
        public CaptureAnnotation DragHandle(CaptureHandle handle, double x, double y)
        {
            if (Kind == CaptureAnnotationKind.Arrow)
            {
                if (handle == CaptureHandle.Start)
                    return WithPoints(x, y, X2, Y2);
                if (handle == CaptureHandle.End)
                    return WithPoints(X1, Y1, x, y);
                return this;
            }
            if (Kind == CaptureAnnotationKind.Text)
                return this;

            var left = Left;
            var top = Top;
            var right = Left + Width;
            var bottom = Top + Height;
            if (Kind == CaptureAnnotationKind.Image && IsCorner(handle) && Width > 0 && Height > 0)
            {
                var anchorX = handle == CaptureHandle.TopLeft || handle == CaptureHandle.BottomLeft ? right : left;
                var anchorY = handle == CaptureHandle.TopLeft || handle == CaptureHandle.TopRight ? bottom : top;
                var dx = x - anchorX;
                var dy = y - anchorY;
                var scale = Math.Max(Math.Abs(dx) / Width, Math.Abs(dy) / Height);
                var width = Width * scale * (dx < 0 ? -1 : 1);
                var height = Height * scale * (dy < 0 ? -1 : 1);
                return WithPoints(anchorX, anchorY, anchorX + width, anchorY + height);
            }
            switch (handle)
            {
                case CaptureHandle.TopLeft: left = x; top = y; break;
                case CaptureHandle.Top: top = y; break;
                case CaptureHandle.TopRight: right = x; top = y; break;
                case CaptureHandle.Right: right = x; break;
                case CaptureHandle.BottomRight: right = x; bottom = y; break;
                case CaptureHandle.Bottom: bottom = y; break;
                case CaptureHandle.BottomLeft: left = x; bottom = y; break;
                case CaptureHandle.Left: left = x; break;
                default: return this;
            }
            return WithPoints(left, top, right, bottom);
        }

        private static bool IsCorner(CaptureHandle handle)
        {
            return handle == CaptureHandle.TopLeft || handle == CaptureHandle.TopRight ||
                handle == CaptureHandle.BottomRight || handle == CaptureHandle.BottomLeft;
        }

        /// <summary>점을 집는 맨 위(나중에 그린) 도형의 번호, 없으면 -1.</summary>
        public static int TopmostHit(IReadOnlyList<CaptureAnnotation> annotations, double x, double y, double tolerance)
        {
            for (var i = annotations.Count - 1; i >= 0; i--)
            {
                if (annotations[i].HitTest(x, y, tolerance))
                    return i;
            }
            return -1;
        }

        private static double DistanceToSegment(double x, double y, double x1, double y1, double x2, double y2)
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            var lengthSquared = dx * dx + dy * dy;
            var t = lengthSquared <= 0 ? 0 : Math.Max(0, Math.Min(1, ((x - x1) * dx + (y - y1) * dy) / lengthSquared));
            var px = x1 + t * dx - x;
            var py = y1 + t * dy - y;
            return Math.Sqrt(px * px + py * py);
        }
    }
}
