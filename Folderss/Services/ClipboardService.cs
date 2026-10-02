using System.Runtime.InteropServices;
using System.Windows;

namespace Folderss.Services
{
    /// <summary>
    /// 클립보드 쓰기. 다른 프로그램(클립보드 관리자, 원격 데스크톱, 보안 프로그램 등)이 클립보드를 잡고 있으면
    /// WPF가 내부에서 1초가량 재시도한 뒤 CLIPBRD_E_CANT_OPEN(COMException)을 던진다(#30).
    /// 키 입력 처리기에서 올라가면 앱이 종료되므로 여기서 잡아 알리고 false를 돌려준다.
    /// 조용히 넘기면 사용자가 예전 클립보드 내용을 붙여넣을 수 있어 실패를 알린다.
    /// </summary>
    public static class ClipboardService
    {
        public static bool TrySetDataObject(object data)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (ExternalException)
            {
                MessageBox.Show(
                    "다른 프로그램이 클립보드를 사용 중이라 복사하지 못했습니다.\n잠시 후 다시 시도하세요.",
                    "클립보드", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }
    }
}
