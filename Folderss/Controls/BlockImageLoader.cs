using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Folderss.Controls
{
    /// <summary>
    /// 블록 편집기 이미지 블록용 비동기 이미지 로더. 파일 읽기·디코딩은 스레드 풀에서 하고(화면 폭에 맞춰 줄여 디코딩),
    /// 결과(Freeze한 BitmapSource)는 UI 스레드 콜백으로 넘긴다. 스크롤로 행이 다시 만들어져도 다시 디코딩하지 않게
    /// 최근 이미지를 <see cref="Capacity"/>개까지 기억하고, 원본 픽셀 크기는 따로 기억해 행을 만들기 전 높이 추정에 쓴다.
    /// </summary>
    internal static class BlockImageLoader
    {
        private const int Capacity = 64;

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

        private static BitmapSource Decode(string path, int decodeWidth)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                // 머리글만 읽어 원본 크기를 알아 둔다(작은 이미지는 늘려 디코딩하지 않게).
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
                var pixelWidth = frame.PixelWidth;
                var pixelHeight = frame.PixelHeight;
                lock (Gate)
                    Sizes[path] = new Size(pixelWidth, pixelHeight);

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
}
