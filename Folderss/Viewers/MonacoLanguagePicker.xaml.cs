using Folderss.Controls;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Folderss.Viewers
{
    /// <summary>
    /// Monaco 탭 툴바의 언어 선택(#82). 단추에 현재 언어를 보이고, 누르면 찾기 칸과 언어 목록(맨 위 "자동")을 연다.
    /// 고르면 <see cref="LanguagePicked"/>(null = 자동)를 알린다. 언어 목록은 편집기가 보낸 런타임 등록 언어이고,
    /// 받기 전에는 정적 표(<see cref="MonacoLanguages.Languages"/>)를 쓴다.
    /// </summary>
    public partial class MonacoLanguagePicker : UserControl
    {
        public sealed class LanguageChoice
        {
            /// <summary>언어 id. null이면 "자동".</summary>
            public string Id { get; set; }
            public string Name { get; set; }
            public string Label { get; set; }
        }

        private List<LanguageChoice> _languages = new List<LanguageChoice>();
        private string _currentId;
        private bool _currentAuto = true;
        private DateTime _closedAt = DateTime.MinValue;

        /// <summary>사용자가 언어를 골랐다. 값이 null이면 "자동"(파일별 기억 삭제).</summary>
        public event EventHandler<string> LanguagePicked;

        public MonacoLanguagePicker()
        {
            InitializeComponent();
            SetLanguages(MonacoLanguages.Languages.Select(l => new KeyValuePair<string, string>(l.Id, l.DisplayName)));
            UpdateButton();
        }

        /// <summary>목록에 보일 언어(id, 표시 이름). 이름순으로 정렬한다.</summary>
        public void SetLanguages(IEnumerable<KeyValuePair<string, string>> languages)
        {
            _languages = languages
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(kv => new LanguageChoice
                {
                    Id = kv.Key,
                    Name = string.IsNullOrWhiteSpace(kv.Value) ? kv.Key : kv.Value,
                    Label = (string.IsNullOrWhiteSpace(kv.Value) || string.Equals(kv.Value, kv.Key, StringComparison.OrdinalIgnoreCase))
                        ? kv.Key
                        : kv.Value + "  (" + kv.Key + ")"
                })
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            UpdateButton();
        }

        /// <summary>편집기의 지금 언어. <paramref name="auto"/>는 파일 이름으로 자동 고른 것인지, <paramref name="huge"/>는 초대용량(plaintext 고정).</summary>
        public void SetCurrent(string id, bool auto, bool huge)
        {
            _currentId = id;
            _currentAuto = auto;
            if (_defaultToolTip == null)
                _defaultToolTip = PickerButton.ToolTip;
            PickerButton.IsEnabled = !huge;
            // 사용 안 함 상태에서도 이유가 보이도록 툴팁을 바꾼다.
            ToolTipService.SetShowOnDisabled(PickerButton, true);
            PickerButton.ToolTip = huge ? "초대용량 파일은 plaintext로만 엽니다(구문 강조 끔)." : _defaultToolTip;
            UpdateButton();
        }

        private object _defaultToolTip;

        private void UpdateButton()
        {
            var name = DisplayNameOf(_currentId) ?? "…";
            PickerText.Text = _currentAuto ? name : name + " ●";
        }

        private string DisplayNameOf(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            var choice = _languages.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
            return choice != null ? choice.Name : id;
        }

        private void PickerButton_Click(object sender, RoutedEventArgs e)
        {
            // StaysOpen=False 팝업은 단추를 누르는 순간(바깥 클릭) 먼저 닫히므로, 방금 닫혔으면 다시 열지 않는다.
            if ((DateTime.UtcNow - _closedAt).TotalMilliseconds < 250)
            {
                PickerButton.IsChecked = false;
                return;
            }
            PickerPopup.IsOpen = !PickerPopup.IsOpen;
            PickerButton.IsChecked = PickerPopup.IsOpen;
        }

        private void PickerPopup_Opened(object sender, EventArgs e)
        {
            FilterBox.Text = string.Empty;
            RefreshList();
            FilterBox.Focus();
            Keyboard.Focus(FilterBox);
        }

        private void PickerPopup_Closed(object sender, EventArgs e)
        {
            _closedAt = DateTime.UtcNow;
            PickerButton.IsChecked = false;
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshList();
        }

        private void RefreshList()
        {
            var filter = (FilterBox.Text ?? string.Empty).Trim();
            var items = new List<LanguageChoice>();
            if (filter.Length == 0)
            {
                var detected = _currentAuto ? DisplayNameOf(_currentId) : null;
                items.Add(new LanguageChoice
                {
                    Id = null,
                    Label = detected != null ? "자동 (파일 이름으로: " + detected + ")" : "자동 (파일 이름으로 고르기)"
                });
            }
            items.AddRange(_languages.Where(c => Matches(c, filter)));
            LanguageList.ItemsSource = items;

            var current = _currentAuto
                ? items.FirstOrDefault(c => c.Id == null)
                : items.FirstOrDefault(c => string.Equals(c.Id, _currentId, StringComparison.OrdinalIgnoreCase));
            LanguageList.SelectedItem = current ?? items.FirstOrDefault();
            if (LanguageList.SelectedItem != null)
                LanguageList.ScrollIntoView(LanguageList.SelectedItem);
        }

        private static bool Matches(LanguageChoice choice, string filter)
        {
            if (filter.Length == 0)
                return true;
            if (choice.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // ".go"처럼 확장자로 찾기: 정적 표의 확장자·파일 이름에서 찾는다.
            var info = MonacoLanguages.Find(choice.Id);
            return info != null &&
                (info.Extensions.Any(x => string.Equals(x, filter, StringComparison.OrdinalIgnoreCase)) ||
                 info.Filenames.Any(x => x.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                case Key.Up:
                    if (LanguageList.Items.Count == 0)
                        break;
                    var index = LanguageList.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                    LanguageList.SelectedIndex = Math.Max(0, Math.Min(LanguageList.Items.Count - 1, index));
                    LanguageList.ScrollIntoView(LanguageList.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Pick(LanguageList.SelectedItem as LanguageChoice);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    PickerPopup.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        }

        private void LanguageList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Pick(LanguageList.SelectedItem as LanguageChoice);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                PickerPopup.IsOpen = false;
                e.Handled = true;
            }
        }

        private void LanguageList_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;
            // 원본이 Run 등 ContentElement일 수 있어 ElementTree로 조상을 찾는다(CLAUDE.md "이벤트 원본에서 조상 찾기").
            var item = ElementTree.FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item != null)
                Pick(item.DataContext as LanguageChoice);
        }

        private void Pick(LanguageChoice choice)
        {
            if (choice == null)
                return;
            PickerPopup.IsOpen = false;
            LanguagePicked?.Invoke(this, choice.Id);
        }
    }
}
