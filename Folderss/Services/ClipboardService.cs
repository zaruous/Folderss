using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;

namespace Folderss.Services
{
    /// <summary>
    /// 클립보드 읽기·쓰기. 다른 프로그램(클립보드 관리자, 원격 데스크톱, 보안 프로그램 등)이 클립보드를 잡고 있으면
    /// WPF가 내부에서 1초가량 재시도한 뒤 CLIPBRD_E_CANT_OPEN(COMException)을 던진다(#30).
    /// 키 입력 처리기에서 올라가면 앱이 종료되므로 여기서 잡아 알리고 false를 돌려준다.
    /// 조용히 넘기면 사용자가 예전 클립보드 내용을 붙여넣을 수 있어 실패를 알린다.
    /// </summary>
    public static class ClipboardService
    {
        public static bool TrySetDataObject(object data)
        {
            var dataObject = data as IDataObject ?? new DataObject(data);
            try
            {
                Clipboard.SetDataObject(dataObject, true);
                return true;
            }
            catch (ExternalException)
            {
                // 등록(OleSetClipboard)은 됐고 Flush만 실패했으면 이 앱이 실행 중인 동안은 붙여넣기가 된다.
                // 앱을 닫으면 내용이 사라질 수 있지만 복사 자체는 성공으로 본다.
                if (IsCurrent(dataObject))
                    return true;

                ShowBusy("복사하지");
                return false;
            }
        }

        /// <summary>읽기에 실패하면 알리고 false. 파일 목록이 없으면 true와 null.</summary>
        public static bool TryGetFileDropList(out StringCollection files)
        {
            files = null;
            try
            {
                if (Clipboard.ContainsFileDropList())
                    files = Clipboard.GetFileDropList();
                return true;
            }
            catch (ExternalException)
            {
                ShowBusy("붙여넣지");
                return false;
            }
        }

        /// <summary>읽기에 실패하면 알리고 false. 텍스트가 없으면 true와 null.</summary>
        public static bool TryGetText(out string text)
        {
            text = null;
            try
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
                return true;
            }
            catch (ExternalException)
            {
                ShowBusy("붙여넣지");
                return false;
            }
        }

        /// <summary>명령 사용 가능 여부 판단용. 자주 불리므로 실패해도 알리지 않고 false.</summary>
        public static bool ContainsFileDropList()
        {
            try
            {
                return Clipboard.ContainsFileDropList();
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        private static bool IsCurrent(IDataObject dataObject)
        {
            try
            {
                return Clipboard.IsCurrent(dataObject);
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        private static void ShowBusy(string action)
        {
            MessageBox.Show(
                "다른 프로그램이 클립보드를 사용 중이라 " + action + " 못했습니다.\n잠시 후 다시 시도하세요.",
                "클립보드", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
