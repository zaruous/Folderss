using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Folderss.Controls
{
    /// <summary>
    /// 입력 이벤트의 <c>OriginalSource</c>에서 조상 요소로 올라가는 탐색.
    /// <c>Run</c>·<c>Span</c> 같은 ContentElement는 Visual이 아니어서 <see cref="VisualTreeHelper.GetParent"/>가 예외를 던지므로
    /// (블록 편집기 수식 클릭 크래시) 논리 부모로 건너간다.
    /// </summary>
    internal static class ElementTree
    {
        public static DependencyObject GetParent(DependencyObject element)
        {
            if (element is Visual || element is Visual3D)
                return VisualTreeHelper.GetParent(element);
            return LogicalTreeHelper.GetParent(element)
                ?? (element is ContentElement content ? ContentOperations.GetParent(content) : null);
        }

        /// <summary><paramref name="element"/>가 <paramref name="ancestor"/> 자신이거나 그 아래에 있는지(<c>Visual.IsAncestorOf</c>는 Run에서 예외).</summary>
        public static bool IsWithin(DependencyObject element, DependencyObject ancestor)
        {
            for (; element != null; element = GetParent(element))
            {
                if (ReferenceEquals(element, ancestor))
                    return true;
            }
            return false;
        }

        public static T FindAncestor<T>(DependencyObject element) where T : DependencyObject
        {
            for (; element != null; element = GetParent(element))
            {
                if (element is T found)
                    return found;
            }
            return null;
        }
    }
}
