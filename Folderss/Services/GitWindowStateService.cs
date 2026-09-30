using System;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Folderss.Services
{
    /// <summary>Git 창 변경 사항 탭의 보기 상태. 설정 창이 아니라 창에서 바꾸는 즉시 저장한다.</summary>
    public sealed class GitWindowState
    {
        public const double DefaultListWidth = 340;
        public const double MinListWidth = 200;
        public const double MaxListWidth = 4000;
        public const double MinWeight = 0.05;
        public const double MaxWeight = 20;

        /// <summary>변경 목록을 트리로 볼지.</summary>
        public bool ChangesTreeMode { get; set; }

        /// <summary>변경 목록 열(왼쪽) 너비(px).</summary>
        public double ChangesListWidth { get; set; } = DefaultListWidth;

        /// <summary>변경됨 ↔ 스테이지됨 목록 높이 비율(* 값).</summary>
        public double UnstagedWeight { get; set; } = 1;
        public double StagedWeight { get; set; } = 1;
    }

    /// <summary><c>%LOCALAPPDATA%\Folderss\git-window.xml</c> 읽기·쓰기. 읽기 실패는 기본값, 쓰기 실패는 예외.</summary>
    public static class GitWindowStateService
    {
        public static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Folderss", "git-window.xml");

        public static GitWindowState Load() => Load(DefaultPath);

        public static GitWindowState Load(string path)
        {
            var state = new GitWindowState();
            if (!File.Exists(path))
                return state;

            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var root = doc.DocumentElement;
                if (root == null)
                    return state;

                if (bool.TryParse(root.GetAttribute("changesTree"), out var tree))
                    state.ChangesTreeMode = tree;
                if (TryParse(root.GetAttribute("listWidth"), out var width))
                    state.ChangesListWidth = Math.Clamp(width, GitWindowState.MinListWidth, GitWindowState.MaxListWidth);
                // 비율은 둘 다 읽혀야 쓴다(한쪽만 바뀌면 의도와 다른 비율이 된다).
                if (TryParse(root.GetAttribute("unstagedWeight"), out var unstaged) && TryParse(root.GetAttribute("stagedWeight"), out var staged))
                {
                    state.UnstagedWeight = Math.Clamp(unstaged, GitWindowState.MinWeight, GitWindowState.MaxWeight);
                    state.StagedWeight = Math.Clamp(staged, GitWindowState.MinWeight, GitWindowState.MaxWeight);
                }
            }
            catch (Exception ex)
            {
                // 보기 상태일 뿐이라 기본값으로 연다. 원인은 디버그 출력에 남긴다.
                System.Diagnostics.Debug.WriteLine("git-window.xml 읽기 실패: " + ex);
            }
            return state;
        }

        public static void Save(GitWindowState state) => Save(state, DefaultPath);

        public static void Save(GitWindowState state, string path)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var doc = new XmlDocument();
            var root = doc.CreateElement("gitWindow");
            root.SetAttribute("changesTree", state.ChangesTreeMode.ToString());
            root.SetAttribute("listWidth", state.ChangesListWidth.ToString("R", CultureInfo.InvariantCulture));
            root.SetAttribute("unstagedWeight", state.UnstagedWeight.ToString("R", CultureInfo.InvariantCulture));
            root.SetAttribute("stagedWeight", state.StagedWeight.ToString("R", CultureInfo.InvariantCulture));
            doc.AppendChild(root);
            SettingsFile.Write(path, doc.Save);
        }

        private static bool TryParse(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
        }
    }
}
