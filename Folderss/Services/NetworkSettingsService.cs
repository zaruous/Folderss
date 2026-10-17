using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;

namespace Folderss.Services
{
    /// <summary>
    /// 뷰어가 외부 주소에 접속하는 방식(설정 창의 "네트워크" 탭). 지금은 마크다운 뷰어의 원격 이미지에 쓴다.
    /// localhost 같은 이 PC 안의 주소는 이 설정과 관계없이 허용한다(<see cref="RemoteContentPolicy"/>).
    /// </summary>
    public sealed class NetworkSettings
    {
        /// <summary>원격 https 이미지를 표시한다(기본 켜짐).</summary>
        public bool ShowRemoteImages { get; set; } = true;

        /// <summary>원격 http 이미지도 표시한다(기본 꺼짐). 뷰어 페이지가 https라 Folderss가 대신 받아 전달한다.</summary>
        public bool AllowHttpImages { get; set; }

        /// <summary>인증서 오류(만료·자체 서명·이름 불일치 등)가 있는 사이트도 허용한다(기본 꺼짐).</summary>
        public bool AllowInvalidCertificates { get; set; }

        /// <summary>
        /// Windows 인증서 저장소 외에 추가로 신뢰할 내부 인증서(PEM). Windows에는 설치하지 않고 Folderss에서만 쓴다.
        /// </summary>
        public List<string> TrustedCertificates { get; set; } = new List<string>();

        public NetworkSettings Clone()
        {
            var copy = (NetworkSettings)MemberwiseClone();
            copy.TrustedCertificates = new List<string>(TrustedCertificates ?? new List<string>());
            return copy;
        }
    }

    /// <summary>
    /// 네트워크 설정을 <c>%LOCALAPPDATA%\Folderss\network-settings.xml</c>에 저장한다.
    /// 뷰어는 요청마다 <see cref="Current"/>를 읽으므로, 저장하면 다음 요청부터 바로 적용된다.
    /// </summary>
    public static class NetworkSettingsService
    {
        public static readonly string DefaultConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "network-settings.xml");

        private static NetworkSettings _current;

        /// <summary>현재 적용 중인 설정(처음 쓸 때 파일에서 읽는다).</summary>
        public static NetworkSettings Current
        {
            get { return _current ?? (_current = Load()); }
        }

        public static NetworkSettings Load() => Load(DefaultConfigPath);

        public static NetworkSettings Load(string path)
        {
            var settings = new NetworkSettings();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return settings;

            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var root = doc.DocumentElement;
                if (root == null)
                    return settings;

                if (bool.TryParse(root.GetAttribute("showRemoteImages"), out var showRemote))
                    settings.ShowRemoteImages = showRemote;
                if (bool.TryParse(root.GetAttribute("allowHttpImages"), out var allowHttp))
                    settings.AllowHttpImages = allowHttp;
                if (bool.TryParse(root.GetAttribute("allowInvalidCertificates"), out var allowInvalid))
                    settings.AllowInvalidCertificates = allowInvalid;

                foreach (XmlElement cert in root.SelectNodes("trustedCertificate"))
                {
                    var pem = cert.InnerText.Trim();
                    if (pem.Length > 0)
                        settings.TrustedCertificates.Add(pem);
                }
            }
            catch (Exception ex)
            {
                // 읽기 실패는 기본값으로 동작한다(창을 못 여는 것보다 낫다). 원인은 디버그 출력에 남긴다.
                System.Diagnostics.Debug.WriteLine(Path.GetFileName(path) + " 읽기 실패: " + ex);
            }
            return settings;
        }

        /// <summary>설정 창에서 저장해 <see cref="Current"/>가 바뀌었을 때(뷰어가 기억한 인증서 허용을 지우는 데 쓴다).</summary>
        public static event EventHandler Changed;

        /// <summary>파일에 저장하고 <see cref="Current"/>를 바꾼다. 실패는 예외로 알린다(설정 창이 모아 보여 준다).</summary>
        public static void Save(NetworkSettings settings)
        {
            // 파일 저장이 실패해도 이번 실행 중에는 바뀐 설정을 쓴다(다른 설정과 같은 방식).
            _current = settings.Clone();
            Changed?.Invoke(null, EventArgs.Empty);
            Save(settings, DefaultConfigPath);
        }

        public static void Save(NetworkSettings settings, string path)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var doc = new XmlDocument();
            var root = doc.CreateElement("network");
            root.SetAttribute("showRemoteImages", settings.ShowRemoteImages.ToString());
            root.SetAttribute("allowHttpImages", settings.AllowHttpImages.ToString());
            root.SetAttribute("allowInvalidCertificates", settings.AllowInvalidCertificates.ToString());
            foreach (var pem in settings.TrustedCertificates ?? new List<string>())
            {
                var cert = doc.CreateElement("trustedCertificate");
                cert.InnerText = pem;
                root.AppendChild(cert);
            }
            doc.AppendChild(root);
            SettingsFile.Write(path, doc.Save);
        }

        /// <summary>
        /// 인증서 파일(.cer/.crt DER 또는 PEM, .pem)을 읽어 PEM 문자열로 돌려준다. 인증서가 아니면 사람이 읽을 문장의 예외를 던진다.
        /// </summary>
        public static string ReadCertificateFileAsPem(string path)
        {
            var bytes = File.ReadAllBytes(path);
            X509Certificate2 cert;
            try
            {
                var text = Encoding.ASCII.GetString(bytes);
                cert = text.Contains("-----BEGIN CERTIFICATE-----")
                    ? X509Certificate2.CreateFromPem(text)
                    : new X509Certificate2(bytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("인증서 파일을 읽을 수 없습니다(.cer, .crt, .pem 형식의 인증서만 추가할 수 있습니다).\n" + ex.Message, ex);
            }

            using (cert)
                return cert.ExportCertificatePem();
        }

        /// <summary>목록에 보일 인증서 요약(주체·만료일·지문). 읽을 수 없으면 null.</summary>
        public static CertificateSummary Describe(string pem)
        {
            try
            {
                using (var cert = X509Certificate2.CreateFromPem(pem))
                {
                    return new CertificateSummary
                    {
                        Pem = pem,
                        Subject = cert.GetNameInfo(X509NameType.SimpleName, false),
                        Expires = cert.NotAfter.ToString("yyyy-MM-dd"),
                        Thumbprint = cert.Thumbprint
                    };
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>같은 인증서(지문)가 이미 목록에 있는지.</summary>
        public static bool ContainsCertificate(IEnumerable<string> pems, string pem)
        {
            var thumbprint = Describe(pem)?.Thumbprint;
            return thumbprint != null && pems.Any(p => Describe(p)?.Thumbprint == thumbprint);
        }
    }

    /// <summary>설정 창 인증서 목록 한 줄.</summary>
    public sealed class CertificateSummary
    {
        public string Pem { get; set; }
        public string Subject { get; set; }
        public string Expires { get; set; }
        public string Thumbprint { get; set; }

        /// <summary>목록 표시용 지문 앞 16자리.</summary>
        public string ShortThumbprint => Thumbprint == null ? null : Thumbprint.Substring(0, Math.Min(16, Thumbprint.Length));
    }
}
