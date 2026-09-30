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
