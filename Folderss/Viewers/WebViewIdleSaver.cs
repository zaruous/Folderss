using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Windows;
using System.Windows.Threading;

namespace Folderss.Viewers
{
    /// <summary>
    /// 탭이 가려진 WebView2 뷰어의 메모리를 줄인다. 뷰어마다 렌더러 프로세스가 하나씩(약 90~120MB) 떠 있으므로,
    /// 화면에서 빠지면 메모리 목표를 낮추고(<see cref="CoreWebView2MemoryUsageTargetLevel.Low"/>),
    /// 계속 가려져 있으면 일시 중지(<see cref="CoreWebView2.TrySuspendAsync"/>)한다.
    /// 다시 보이면 재개하고 목표를 되돌린 뒤 <c>onVisible</c>을 불러, 가려진 동안 미뤄 둔 일(테마 적용 등)을 하게 한다.
    /// </summary>
    internal sealed class WebViewIdleSaver : IDisposable
    {
        private static readonly TimeSpan SuspendDelay = TimeSpan.FromSeconds(30);

        private readonly FrameworkElement _owner;
        private readonly WebView2 _webView;
        private readonly Action _onVisible;
        private readonly DispatcherTimer _suspendTimer;
        private bool _disposed;

        public WebViewIdleSaver(FrameworkElement owner, WebView2 webView, Action onVisible)
        {
            _owner = owner;
            _webView = webView;
            _onVisible = onVisible;
            _suspendTimer = new DispatcherTimer { Interval = SuspendDelay };
            _suspendTimer.Tick += OnSuspendTimerTick;
            _owner.IsVisibleChanged += OnOwnerIsVisibleChanged;
            Apply();
        }

        private void OnOwnerIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Apply();
            if (_owner.IsVisible && !_disposed)
                _onVisible?.Invoke();
        }

        private void Apply()
        {
            var core = _disposed ? null : _webView.CoreWebView2;
            if (core == null)
                return;

            // 메모리 절약은 덤이므로, 오래된 WebView2 런타임처럼 지원하지 않는 환경에서는 조용히 포기한다.
            try
            {
                if (_owner.IsVisible)
                {
                    _suspendTimer.Stop();
                    if (core.IsSuspended)
                        core.Resume();
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                }
                else
                {
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                    _suspendTimer.Start();
                }
            }
            catch (Exception)
            {
            }
        }

        private async void OnSuspendTimerTick(object sender, EventArgs e)
        {
            _suspendTimer.Stop();
            var core = _disposed ? null : _webView.CoreWebView2;
            if (core == null || _owner.IsVisible || core.IsSuspended)
                return;

            // 컨트롤이 아직 보이는 상태로 남아 있는 등 일시 중지할 수 없으면 다음에 가려질 때 다시 시도한다.
            try
            {
                await core.TrySuspendAsync();
            }
            catch (Exception)
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _suspendTimer.Stop();
            _suspendTimer.Tick -= OnSuspendTimerTick;
            _owner.IsVisibleChanged -= OnOwnerIsVisibleChanged;
        }
    }
}
