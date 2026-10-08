using System;
using System.Globalization;
using System.IO;

namespace Folderss.Services
{
    /// <summary>
    /// 화면 캡쳐의 순수 계산(WPF·GDI 없음, 테스트 프로젝트에서 소스 링크로 검증).
    /// 화면을 찍고 오버레이·결과 창을 띄우는 쪽은 <c>CaptureOverlayWindow</c>, <c>CaptureResultWindow</c>.
    /// </summary>
    public static class ScreenCaptureService
    {
        /// <summary>이보다 작은 선택(가로·세로 픽셀)은 클릭으로 보고 캡쳐하지 않는다.</summary>
        public const int MinSelectionPixels = 3;

        /// <summary>
        /// 오버레이에서 드래그한 두 점(오버레이 좌표)을 캡쳐 이미지의 픽셀 사각형으로 바꾼다.
        /// 오버레이는 찍어 둔 이미지를 창 크기에 맞춰 늘려 그리므로 비율로만 변환한다 —
        /// 모니터 배율(DPI)을 따로 계산하지 않아도 사용자가 본 그림 그대로 잘린다.
        /// 어느 방향으로 끌어도 되고, 이미지 밖으로 나간 부분은 잘라낸다.
        /// </summary>
        public static (int X, int Y, int Width, int Height) ToPixelRect(
            double x1, double y1, double x2, double y2,
            double viewWidth, double viewHeight, int pixelWidth, int pixelHeight)
        {
            if (viewWidth <= 0 || viewHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
                return (0, 0, 0, 0);

            var scaleX = pixelWidth / viewWidth;
            var scaleY = pixelHeight / viewHeight;
            var left = Clamp(Math.Round(Math.Min(x1, x2) * scaleX), pixelWidth);
            var top = Clamp(Math.Round(Math.Min(y1, y2) * scaleY), pixelHeight);
            var right = Clamp(Math.Round(Math.Max(x1, x2) * scaleX), pixelWidth);
            var bottom = Clamp(Math.Round(Math.Max(y1, y2) * scaleY), pixelHeight);
            return (left, top, right - left, bottom - top);
        }

        public static bool IsSelectable(int width, int height)
        {
            return width >= MinSelectionPixels && height >= MinSelectionPixels;
        }

        /// <summary>
        /// 빠른 저장 경로: <c>&lt;폴더&gt;\캡쳐_yyyyMMdd_HHmmss.png</c>, 같은 이름이 있으면 <c>_2</c>, <c>_3</c>…을 붙인다.
        /// </summary>
        public static string NextCapturePath(string folder, DateTime now, Func<string, bool> exists)
        {
            var stem = "캡쳐_" + now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(folder, stem + ".png");
            for (var i = 2; exists(path); i++)
                path = Path.Combine(folder, stem + "_" + i.ToString(CultureInfo.InvariantCulture) + ".png");
            return path;
        }

        /// <summary>저장 형식은 확장자로 정한다: "png", "jpeg", "bmp". 그 밖의 확장자는 null(내용과 이름이 어긋난 파일을 만들지 않는다).</summary>
        public static string FormatFromPath(string path)
        {
            switch ((Path.GetExtension(path) ?? string.Empty).ToLowerInvariant())
            {
                case ".png": return "png";
                case ".jpg":
                case ".jpeg": return "jpeg";
                case ".bmp": return "bmp";
                default: return null;
            }
        }

        private static int Clamp(double value, int max)
        {
            return (int)Math.Max(0, Math.Min(max, value));
        }
    }
}
