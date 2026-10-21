using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Folderss.Services
{
    /// <summary>
    /// 실행 폴더 <c>Themes\</c>에서 읽는 테마 XAML 검사. XAML은 임의 객체(예: ObjectDataProvider로 프로그램 실행)를 만들 수 있으므로
    /// 테마에 필요한 요소(색·브러시·글꼴·문자열·정수)와 <c>{StaticResource}</c>, <c>{x:Static SystemColors.…}</c>만 허용한다.
    /// </summary>
    public static class ThemeFileValidator
    {
        private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

        private static readonly string[] PresentationElements = { "ResourceDictionary", "Color", "SolidColorBrush", "FontFamily" };
        private static readonly string[] SystemElements = { "String", "Int32" };
        private static readonly string[] SystemNamespaces =
        {
            "clr-namespace:System;assembly=mscorlib",
            "clr-namespace:System;assembly=System.Runtime",
            "clr-namespace:System;assembly=System.Private.CoreLib",
        };

        private static readonly Regex AllowedMarkupExtension =
            new Regex(@"^\{(StaticResource\s+\w+|x:Static\s+SystemColors\.\w+)\}$");

        /// <summary>허용되지 않는 내용이 있으면 이유, 없으면 null.</summary>
        public static string Validate(string xaml)
        {
            XDocument document;
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var reader = XmlReader.Create(new StringReader(xaml), settings))
                    document = XDocument.Load(reader);
            }
            catch (XmlException ex)
            {
                return "XML 형식 오류: " + ex.Message;
            }

            var root = document.Root;
            if (root.Name != XName.Get("ResourceDictionary", PresentationNs))
                return "최상위 요소가 ResourceDictionary가 아닙니다.";

            foreach (var element in root.DescendantsAndSelf())
            {
                if (!IsAllowedElement(element))
                    return "허용하지 않는 요소: " + element.Name.LocalName;

                if (element != root && element.Parent != root)
                    return "중첩 요소는 허용하지 않습니다: " + element.Name.LocalName;

                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                        continue;
                    if (attribute.Name.Namespace == XamlNs && attribute.Name.LocalName != "Key")
                        return "허용하지 않는 속성: x:" + attribute.Name.LocalName;
                    if (attribute.Name.Namespace != XamlNs && attribute.Name.Namespace != XNamespace.None)
                        return "허용하지 않는 속성: " + attribute.Name;

                    var value = attribute.Value.Trim();
                    if (value.StartsWith("{", StringComparison.Ordinal) && !AllowedMarkupExtension.IsMatch(value))
                        return "허용하지 않는 태그 확장: " + value;
                }
            }

            return null;
        }

        private static bool IsAllowedElement(XElement element)
        {
            var ns = element.Name.NamespaceName;
            var name = element.Name.LocalName;
            if (ns == PresentationNs)
                return PresentationElements.Contains(name);
            return SystemNamespaces.Contains(ns) && SystemElements.Contains(name);
        }
    }
}
