using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Folderss.Services;

namespace Folderss.Controls
{
    /// <summary>
    /// 블록 편집기 이미지 블록용 비동기 이미지 로더. 파일 읽기·원격 다운로드·디코딩은 스레드 풀에서 하고(화면 폭에 맞춰 줄여 디코딩),
    /// 결과(Freeze한 BitmapSource)는 UI 스레드 콜백으로 넘긴다. 스크롤로 행이 다시 만들어져도 다시 디코딩하지 않게
    /// 최근 이미지를 <see cref="Capacity"/>개까지 기억하고, 원본 픽셀 크기는 따로 기억해 행을 만들기 전 높이 추정에 쓴다.
    /// </summary>
    internal static class BlockImageLoader
    {
        private const int Capacity = 64;
        private const long MaxRemoteImageBytes = 20L * 1024 * 1024;
        private const int MaxBookmarkHtmlBytes = 128 * 1024;

        private static readonly Lazy<HttpClient> RemoteClient = new Lazy<HttpClient>(() =>
        {
            var handler = new HttpClientHandler
            {
                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                ServerCertificateCustomValidationCallback = (request, certificate, chain, errors) =>
                    errors == SslPolicyErrors.None ||
                    RemoteContentPolicy.ShouldAllowCertificateError(request.RequestUri, certificate,
                        chain == null ? Enumerable.Empty<X509Certificate2>() : chain.ChainElements.Cast<X509ChainElement>().Select(el => el.Certificate),
                        NetworkSettingsService.Current)
            };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Folderss/1.0");
            return client;
        });

        private sealed class Entry
        {
            public BitmapSource Image;
            public int DecodeWidth;
            public DateTime FileTime;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, LinkedListNode<KeyValuePair<string, Entry>>> Cache =
            new Dictionary<string, LinkedListNode<KeyValuePair<string, Entry>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<KeyValuePair<string, Entry>> Recent = new LinkedList<KeyValuePair<string, Entry>>();
        private static readonly Dictionary<string, Size> Sizes = new Dictionary<string, Size>(StringComparer.OrdinalIgnoreCase);

        /// <summary>한 번 읽은 이미지의 원본 픽셀 크기.</summary>
        public static bool TryGetSize(string path, out Size size)
        {
            lock (Gate)
                return Sizes.TryGetValue(path, out size);
        }

        /// <summary>
        /// 이미지를 불러 <paramref name="done"/>(UI 스레드)에 넘긴다. 기억한 이미지가 충분히 크고 파일이 그대로면 바로 넘긴다(동기 호출).
        /// 실패하면 이미지 null과 오류 문장.
        /// </summary>
        public static void Load(string path, int decodeWidth, Action<BitmapSource, string> done)
        {
            DateTime fileTime;
            try
            {
                if (!File.Exists(path))
                {
                    done(null, "파일 없음");
                    return;
                }
                fileTime = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex)
            {
                done(null, ex.Message);
                return;
            }

            lock (Gate)
            {
                LinkedListNode<KeyValuePair<string, Entry>> node;
                if (Cache.TryGetValue(path, out node) && node.Value.Value.FileTime == fileTime &&
                    (node.Value.Value.DecodeWidth >= decodeWidth || node.Value.Value.Image.PixelWidth < node.Value.Value.DecodeWidth))
                {
                    Recent.Remove(node);
                    Recent.AddFirst(node);
                    done(node.Value.Value.Image, null);
                    return;
                }
            }

            var dispatcher = Dispatcher.CurrentDispatcher;
            Task.Run(() => Decode(path, decodeWidth)).ContinueWith(task =>
            {
                var image = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                var error = task.Exception != null ? task.Exception.GetBaseException().Message : null;
                if (image != null)
                {
                    lock (Gate)
                    {
                        LinkedListNode<KeyValuePair<string, Entry>> old;
                        if (Cache.TryGetValue(path, out old))
                            Recent.Remove(old);
                        var node = Recent.AddFirst(new KeyValuePair<string, Entry>(path, new Entry { Image = image, DecodeWidth = decodeWidth, FileTime = fileTime }));
                        Cache[path] = node;
                        while (Recent.Count > Capacity)
                        {
                            Cache.Remove(Recent.Last.Value.Key);
                            Recent.RemoveLast();
                        }
                    }
                }
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => done(image, error)));
            }, TaskScheduler.Default);
        }

        /// <summary>원격(http/https) 이미지를 내려받아 디코딩한 뒤 <paramref name="done"/>(UI 스레드)에 넘긴다.</summary>
        public static void LoadRemote(Uri uri, int decodeWidth, Action<BitmapSource, string> done)
        {
            if (uri == null)
            {
                done(null, "잘못된 주소");
                return;
            }

            var key = uri.AbsoluteUri;
            lock (Gate)
            {
                LinkedListNode<KeyValuePair<string, Entry>> node;
                if (Cache.TryGetValue(key, out node) &&
                    (node.Value.Value.DecodeWidth >= decodeWidth || node.Value.Value.Image.PixelWidth < node.Value.Value.DecodeWidth))
                {
                    Recent.Remove(node);
                    Recent.AddFirst(node);
                    done(node.Value.Value.Image, null);
                    return;
                }
            }

            var dispatcher = Dispatcher.CurrentDispatcher;
            Task.Run(async () =>
            {
                using (var response = await RemoteClient.Value.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("HTTP " + (int)response.StatusCode);
                    if (response.Content.Headers.ContentLength > MaxRemoteImageBytes)
                        throw new InvalidDataException("이미지가 너무 큽니다(20MB 초과).");

                    using (var buffer = new MemoryStream())
                    {
                        using (var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            var chunk = new byte[81920];
                            long total = 0;
                            int read;
                            while ((read = await body.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
                            {
                                total += read;
                                if (total > MaxRemoteImageBytes)
                                    throw new InvalidDataException("이미지가 너무 큽니다(20MB 초과).");
                                buffer.Write(chunk, 0, read);
                            }
                        }
                        buffer.Position = 0;
                        return DecodeStream(buffer, key, decodeWidth);
                    }
                }
            }).ContinueWith(task =>
            {
                var image = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                var error = task.Exception != null ? task.Exception.GetBaseException().Message : null;
                if (image != null)
                {
                    lock (Gate)
                    {
                        LinkedListNode<KeyValuePair<string, Entry>> old;
                        if (Cache.TryGetValue(key, out old))
                            Recent.Remove(old);
                        var node = Recent.AddFirst(new KeyValuePair<string, Entry>(key, new Entry { Image = image, DecodeWidth = decodeWidth, FileTime = DateTime.MinValue }));
                        Cache[key] = node;
                        while (Recent.Count > Capacity)
                        {
                            Cache.Remove(Recent.Last.Value.Key);
                            Recent.RemoveLast();
                        }
                    }
                }
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => done(image, error)));
            }, TaskScheduler.Default);
        }

        /// <summary>북마크 블록에서 사용자가 '미리보기 불러오기'를 눌렀을 때 페이지 제목·설명을 가져온다.</summary>
        public static async Task<Tuple<string, string>> FetchPageMetaAsync(Uri uri)
        {
            using (var response = await RemoteClient.Value.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (var buffer = new MemoryStream())
                {
                    using (var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    {
                        var chunk = new byte[16384];
                        int read;
                        while (buffer.Length < MaxBookmarkHtmlBytes && (read = await body.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false)) > 0)
                            buffer.Write(chunk, 0, Math.Min(read, MaxBookmarkHtmlBytes - (int)buffer.Length));
                    }
                    var html = Encoding.UTF8.GetString(buffer.ToArray());
                    var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    var title = titleMatch.Success ? WebUtility.HtmlDecode(Regex.Replace(titleMatch.Groups[1].Value, @"\s+", " ").Trim()) : string.Empty;

                    var descMatch = Regex.Match(html, @"<meta\s+(?:[^>]*?\s+)?(?:name|property)\s*=\s*[""'](?:description|og:description)[""'][^>]*?\scontent\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (!descMatch.Success)
                        descMatch = Regex.Match(html, @"<meta\s+(?:[^>]*?\s+)?content\s*=\s*[""']([^""']*)[""'][^>]*?\s(?:name|property)\s*=\s*[""'](?:description|og:description)[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    var desc = descMatch.Success ? WebUtility.HtmlDecode(Regex.Replace(descMatch.Groups[1].Value, @"\s+", " ").Trim()) : string.Empty;
                    return Tuple.Create(title, desc);
                }
            }
        }

        private static BitmapSource Decode(string path, int decodeWidth)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return DecodeStream(stream, path, decodeWidth);
        }

        private static BitmapSource DecodeStream(Stream stream, string sizeKey, int decodeWidth)
        {
            // 머리글만 읽어 원본 크기를 알아 둔다(작은 이미지는 늘려 디코딩하지 않게).
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            var pixelWidth = frame.PixelWidth;
            var pixelHeight = frame.PixelHeight;
            lock (Gate)
                Sizes[sizeKey] = new Size(pixelWidth, pixelHeight);

            stream.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0 && pixelWidth > decodeWidth)
                image.DecodePixelWidth = decodeWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
