using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace Folderss
{
    /// <summary>설정 > 뷰어 "Monaco 언어 연결" 표의 한 줄(패턴 → Monaco 언어 id, #82).</summary>
    public class MonacoLanguageMappingItem : INotifyPropertyChanged
    {
        private string _pattern;
        private string _language;

        public string Pattern
        {
            get { return _pattern; }
            set { _pattern = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pattern))); }
        }

        public string Language
        {
            get { return _language; }
            set { _language = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    // 설정 > 뷰어의 Monaco 언어 연결(#82). 저장은 SaveAll의 TrySave 목록("Monaco 언어", monaco-language.json).
    public partial class SettingsWindow
    {
        private ObservableCollection<MonacoLanguageMappingItem> _workingMonacoLanguages;

        /// <summary>Monaco 등록 언어(번들 정적 표, 이름순). 표의 언어 콤보와 추가 콤보가 쓴다.</summary>
        public IReadOnlyList<ViewerOption> MonacoLanguageOptions { get; } = MonacoLanguages.Languages
            .Select(l => new ViewerOption
            {
                Key = l.Id,
                DisplayName = string.Equals(l.DisplayName, l.Id, StringComparison.OrdinalIgnoreCase)
                    ? l.Id
                    : l.DisplayName + " (" + l.Id + ")"
            })
            .OrderBy(o => o.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        private void InitializeMonacoLanguageSettings()
        {
            _workingMonacoLanguages = new ObservableCollection<MonacoLanguageMappingItem>(
                MonacoLanguageSettings.Shared.Mappings.Select(kv => new MonacoLanguageMappingItem { Pattern = kv.Key, Language = kv.Value }));
            MonacoLanguageMappingList.ItemsSource = _workingMonacoLanguages;
            NewMonacoLanguageCombo.ItemsSource = MonacoLanguageOptions;
            NewMonacoLanguageCombo.SelectedValue = "ini";
        }

        private void SaveMonacoLanguageSettings()
        {
            MonacoLanguageSettings.Shared.SaveMappings(_workingMonacoLanguages
                .Select(item => new KeyValuePair<string, string>(item.Pattern, item.Language)));
        }

        private void AddMonacoLanguage_Click(object sender, RoutedEventArgs e)
        {
            var pattern = MonacoLanguages.NormalizePattern(NewMonacoPatternBox.Text);
            var language = NewMonacoLanguageCombo.SelectedValue as string;
            if (pattern == null || pattern == ".ext" || !MonacoLanguages.IsValidLanguageId(language))
            {
                MessageBox.Show(
                    "확장자(.inc), 파일 이름(Makefile) 또는 패턴(Dockerfile.*)과 언어를 입력하세요.\n경로 구분자(\\, /)는 쓸 수 없습니다.",
                    "Monaco 언어 연결", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var existing = _workingMonacoLanguages.FirstOrDefault(m =>
                string.Equals(m.Pattern, pattern, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                existing.Language = language;
            else
                _workingMonacoLanguages.Add(new MonacoLanguageMappingItem { Pattern = pattern, Language = language });

            NewMonacoPatternBox.Text = ".ext";
        }

        private void RemoveMonacoLanguage_Click(object sender, RoutedEventArgs e)
        {
            var item = ((FrameworkElement)sender).DataContext as MonacoLanguageMappingItem;
            if (item != null)
                _workingMonacoLanguages.Remove(item);
        }
    }
}
