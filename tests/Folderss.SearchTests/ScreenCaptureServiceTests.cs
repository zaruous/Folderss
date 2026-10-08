using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Folderss.SearchTests
{
    /// <summary>
    /// 화면 캡쳐의 순수 계산: 오버레이 좌표 → 캡쳐 픽셀 사각형, 빠른 저장 파일 이름, 확장자 → 저장 형식.
    /// 오버레이·GDI 캡쳐 자체는 Windows 화면이 있어야 하므로 여기서 다루지 않는다.
    /// </summary>
    public sealed class ScreenCaptureServiceTests
    {
        [Fact]
        public void PixelRect_ScalesBySystemDpi()
        {
            // 150% 배율: 오버레이 1280×720(DIP) 위에 1920×1080 픽셀 이미지.
            var rect = ScreenCaptureService.ToPixelRect(100, 50, 300, 250, 1280, 720, 1920, 1080);
            Assert.Equal((150, 75, 300, 300), rect);
        }

        [Fact]
        public void PixelRect_AnyDragDirectionGivesSameRect()
        {
            var forward = ScreenCaptureService.ToPixelRect(10, 20, 110, 220, 1000, 1000, 1000, 1000);
            var backward = ScreenCaptureService.ToPixelRect(110, 220, 10, 20, 1000, 1000, 1000, 1000);
            var mixed = ScreenCaptureService.ToPixelRect(110, 20, 10, 220, 1000, 1000, 1000, 1000);
            Assert.Equal((10, 20, 100, 200), forward);
            Assert.Equal(forward, backward);
            Assert.Equal(forward, mixed);
        }

        [Fact]
        public void PixelRect_ClampsToImageWhenDraggedOutside()
        {
            // 마우스 캡처 중에는 창 밖(음수, 크기 초과) 좌표가 들어온다.
            var rect = ScreenCaptureService.ToPixelRect(-50, -10, 1200, 900, 1000, 800, 1000, 800);
            Assert.Equal((0, 0, 1000, 800), rect);
        }

        [Fact]
        public void PixelRect_NoFloatingPointOffByOne()
        {
            // 125%: 0.1 단위 오차가 올림되어 1픽셀 커지면 안 된다.
            var rect = ScreenCaptureService.ToPixelRect(0.1 * 3, 0, 100.3, 80, 1536, 864, 1920, 1080);
            Assert.Equal((0, 0, 125, 100), rect);
        }

        [Fact]
        public void PixelRect_EmptyViewIsZero()
        {
            Assert.Equal((0, 0, 0, 0), ScreenCaptureService.ToPixelRect(0, 0, 10, 10, 0, 0, 100, 100));
        }

        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(2, 100, false)]
        [InlineData(100, 2, false)]
        [InlineData(3, 3, true)]
        public void IsSelectable_RejectsClicks(int width, int height, bool expected)
        {
            Assert.Equal(expected, ScreenCaptureService.IsSelectable(width, height));
        }

        [Fact]
        public void NextCapturePath_UsesTimestamp()
        {
            var path = ScreenCaptureService.NextCapturePath("folder", new DateTime(2026, 10, 8, 9, 5, 7), _ => false);
            Assert.Equal(Path.Combine("folder", "캡쳐_20261008_090507.png"), path);
        }

        [Fact]
        public void NextCapturePath_AddsSuffixWhenTaken()
        {
            var taken = new HashSet<string>
            {
                Path.Combine("folder", "캡쳐_20261008_090507.png"),
                Path.Combine("folder", "캡쳐_20261008_090507_2.png")
            };
            var path = ScreenCaptureService.NextCapturePath("folder", new DateTime(2026, 10, 8, 9, 5, 7), taken.Contains);
            Assert.Equal(Path.Combine("folder", "캡쳐_20261008_090507_3.png"), path);
        }

        [Theory]
        [InlineData("a.png", "png")]
        [InlineData("a.PNG", "png")]
        [InlineData("a.jpg", "jpeg")]
        [InlineData("a.JPEG", "jpeg")]
        [InlineData("a.bmp", "bmp")]
        [InlineData("a.gif", null)]
        [InlineData("a", null)]
        public void FormatFromPath_ByExtension(string path, string expected)
        {
            Assert.Equal(expected, ScreenCaptureService.FormatFromPath(path));
        }
    }
}
