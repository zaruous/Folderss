using System;
using System.Collections.Generic;
using System.Linq;
using Folderss.Models;

namespace Folderss.Services
{
    /// <summary>변경 파일 목록(평면)을 폴더 트리로 묶는다. 순수 로직 — WPF 없이 테스트한다.</summary>
    public static class GitChangeTree
    {
        public static List<GitChangeNode> Build(IEnumerable<GitStatusEntry> entries)
        {
            var root = new GitChangeNode { Name = string.Empty, Path = string.Empty };
            foreach (var entry in entries)
            {
                // 추적 안 되는 폴더("dir/")는 폴더가 아니라 한 항목으로 둔다(안의 파일을 git이 따로 알려 주지 않음).
                var path = entry.Path.EndsWith("/", StringComparison.Ordinal) ? entry.Path.TrimEnd('/') : entry.Path;
                var parts = path.Split('/');
                var node = root;
                for (var i = 0; i < parts.Length - 1; i++)
                {
                    var folderPath = string.Join("/", parts, 0, i + 1);
                    var child = node.Children.FirstOrDefault(c => c.IsFolder && c.Path == folderPath);
                    if (child == null)
                    {
                        child = new GitChangeNode { Name = parts[i], Path = folderPath };
                        node.Children.Add(child);
                    }
                    node = child;
                }
                var leafName = parts[parts.Length - 1] + (entry.Path.EndsWith("/", StringComparison.Ordinal) ? "/" : string.Empty);
                node.Children.Add(new GitChangeNode { Name = leafName, Path = entry.Path, Entry = entry });
            }

            foreach (var child in root.Children)
                Compact(child);
            Sort(root);
            return root.Children;
        }

        /// <summary>모든 노드(자신 포함, 위에서 아래 순서).</summary>
        public static IEnumerable<GitChangeNode> AllNodes(IEnumerable<GitChangeNode> roots)
        {
            foreach (var node in roots)
            {
                yield return node;
                foreach (var child in AllNodes(node.Children))
                    yield return child;
            }
        }

        /// <summary>표시된 노드가 고른 파일 전부(폴더면 그 아래 전체). 폴더와 그 안 파일을 함께 골라도 한 번만, 트리 순서.</summary>
        public static List<GitStatusEntry> MarkedEntries(IEnumerable<GitChangeNode> roots)
        {
            return AllNodes(roots).Where(node => node.IsMarked).SelectMany(node => node.Entries).Distinct().ToList();
        }

        /// <summary>
        /// 변경 사항 목록·트리 복사(Ctrl+C 이름 / Ctrl+Shift+C 전체 경로) 텍스트. 저장소 기준 경로(슬래시 구분)를 한 줄에 하나씩, 중복은 한 번만.
        /// 추적 안 되는 폴더("dir/")의 끝 슬래시는 뗀다.
        /// </summary>
        public static string ClipboardText(IEnumerable<string> relativePaths, string rootPath, bool fullPath)
        {
            var lines = relativePaths
                .Select(path => path.TrimEnd('/'))
                .Distinct(StringComparer.Ordinal)
                .Select(path => fullPath
                    ? System.IO.Path.Combine(rootPath, path.Replace('/', System.IO.Path.DirectorySeparatorChar))
                    : path.Substring(path.LastIndexOf('/') + 1));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Shift+클릭 범위: 화면에 보이는 순서 <paramref name="visible"/>에서 기준점부터 누른 노드까지(양끝 포함).
        /// 기준점이 보이지 않으면 누른 노드 하나만.
        /// </summary>
        public static List<GitChangeNode> Range(IList<GitChangeNode> visible, GitChangeNode anchor, GitChangeNode target)
        {
            var from = visible.IndexOf(anchor);
            var to = visible.IndexOf(target);
            if (from < 0 || to < 0)
                return new List<GitChangeNode> { target };
            return visible.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1).ToList();
        }

        /// <summary>파일 없이 폴더 하나만 가진 폴더를 자식과 합친다: a → b → c.cs 는 "a/b" → c.cs.</summary>
        private static void Compact(GitChangeNode node)
        {
            while (node.IsFolder && node.Children.Count == 1 && node.Children[0].IsFolder)
            {
                var only = node.Children[0];
                node.Name = node.Name + "/" + only.Name;
                node.Path = only.Path;
                node.Children.Clear();
                node.Children.AddRange(only.Children);
            }
            foreach (var child in node.Children)
                Compact(child);
        }

        /// <summary>폴더 먼저, 그다음 파일. 각각 이름순(대/소문자 무시).</summary>
        private static void Sort(GitChangeNode node)
        {
            var sorted = node.Children
                .OrderBy(c => c.IsFolder ? 0 : 1)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            node.Children.Clear();
            node.Children.AddRange(sorted);
            foreach (var child in node.Children)
                Sort(child);
        }
    }
}
