# 튜토리얼: 주문서 플러그인 만들기

화면 위쪽에 **타이틀**, 그 아래에 **그리드**를 배치한 간단한 "주문서" 플러그인을 처음부터 만들어 Folderss에 등록하고 실행합니다.
소요 시간은 15분 정도입니다. 개념 설명은 [플러그인 개발 가이드](plugin-development.md)를 참고하세요.

완성하면 이런 화면이 나옵니다.

```
┌─ 주문서 ────────────────────────────────────────────────┐
│  주문서                                                  │
│ ┌──────────────────────┬──────┬────────┬──────────┐     │
│ │ 품목                 │ 수량 │  단가  │   금액   │     │
│ ├──────────────────────┼──────┼────────┼──────────┤     │
│ │ A4 복사용지          │   10 │  4,500 │   45,000 │     │
│ │ 볼펜(검정)           │   50 │    300 │   15,000 │     │
│ │ 스테이플러           │    2 │ 12,000 │   24,000 │     │
│ │                      │      │        │          │ ← 새 행 입력
│ └──────────────────────┴──────┴────────┴──────────┘     │
└─────────────────────────────────────────────────────────┘
```

- 수량이나 단가를 고치면 금액이 바로 다시 계산됩니다.
- 맨 아래 빈 행에 입력하면 품목이 추가되고, 행을 선택해 `Delete`를 누르면 삭제됩니다.
- Folderss 테마를 바꾸면 그리드 색도 함께 바뀝니다.

> 이 튜토리얼은 데이터를 저장하지 않습니다. 팝업을 닫으면 입력한 내용이 사라집니다. 저장은 마지막 "다음 단계"에서 다룹니다.

---

## 0. 준비

- Windows, .NET 8 SDK
- Folderss 소스 (이 저장소). 소스 없이 설치본만 있다면 [가이드 3절](plugin-development.md#3-프로젝트-설정-csproj)의 파일 참조 방식을 쓰세요.

이 튜토리얼은 저장소의 `samples\OrderFormPlugin` 폴더에 만든다고 가정합니다.

```
Folderss\
├── Folderss.PluginContract\
└── samples\
    └── OrderFormPlugin\          ← 여기에 만든다
        ├── OrderFormPlugin.csproj
        ├── plugin.json
        ├── OrderItem.cs
        └── OrderFormPlugin.cs
```

---

## 1. 프로젝트 파일 만들기

`samples\OrderFormPlugin\OrderFormPlugin.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>disable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- 계약 DLL은 Folderss 본체의 것을 쓰므로 zip에 넣지 않는다 (Private=false) -->
    <ProjectReference Include="..\..\Folderss.PluginContract\Folderss.PluginContract.csproj" Private="false" />
  </ItemGroup>

  <!-- 빌드할 때마다 bin\<구성>\net8.0-windows\OrderFormPlugin.zip 을 만든다 -->
  <Target Name="PackPlugin" AfterTargets="Build">
    <PropertyGroup>
      <PluginStage>$(IntermediateOutputPath)plugin-stage\</PluginStage>
    </PropertyGroup>
    <RemoveDir Directories="$(PluginStage)" />
    <Copy SourceFiles="plugin.json;$(TargetPath)" DestinationFolder="$(PluginStage)" />
    <ZipDirectory SourceDirectory="$(PluginStage)" DestinationFile="$(OutDir)OrderFormPlugin.zip" Overwrite="true" />
  </Target>

</Project>
```

포인트:
- `net8.0-windows` + `UseWPF`: WPF 화면을 만들기 위해 필요합니다.
- `Private="false"`: 계약 DLL(`Folderss.PluginContract.dll`)은 Folderss 본체에 이미 있으니 zip에 넣지 않습니다.
- `PackPlugin` 타깃: 빌드할 때마다 `plugin.json`과 DLL만 담은 zip을 만들어 줍니다.

---

## 2. `plugin.json` 작성

`samples\OrderFormPlugin\plugin.json`

```json
{
  "id": "tutorial.order-form",
  "name": "주문서",
  "version": "1.0.0",
  "description": "타이틀과 그리드로 만든 주문서 튜토리얼",
  "assembly": "OrderFormPlugin.dll",
  "type": "OrderFormPlugin.OrderFormPlugin",
  "hasSettings": false
}
```

- `name`의 "주문서"가 `⋯ 메뉴 > 플러그인`에 보이는 메뉴 이름이자 팝업 창 제목입니다. Folderss는 메뉴를 만들 때 이 파일만 읽고, DLL은 사용자가 실행할 때 로드합니다.
- `type`은 **네임스페이스.클래스 이름**입니다. 4단계의 클래스와 정확히 같아야 합니다.
- 설정 탭이 없으므로 `hasSettings`는 `false`입니다.

---

## 3. 주문 품목 모델

그리드의 한 행이 될 클래스입니다. 수량이나 단가가 바뀌면 `Amount`(금액)도 바뀌었다고 알려야 그리드의 금액 칸이 다시 그려집니다.

`samples\OrderFormPlugin\OrderItem.cs`

```csharp
using System.ComponentModel;

namespace OrderFormPlugin
{
    /// <summary>주문서 한 줄. 수량·단가가 바뀌면 금액도 다시 표시되도록 알린다.</summary>
    public sealed class OrderItem : INotifyPropertyChanged
    {
        private string _product;
        private int _quantity;
        private decimal _unitPrice;

        public string Product
        {
            get { return _product; }
            set { _product = value; Notify(nameof(Product)); }
        }

        public int Quantity
        {
            get { return _quantity; }
            set { _quantity = value; Notify(nameof(Quantity)); Notify(nameof(Amount)); }
        }

        public decimal UnitPrice
        {
            get { return _unitPrice; }
            set { _unitPrice = value; Notify(nameof(UnitPrice)); Notify(nameof(Amount)); }
        }

        /// <summary>금액 = 수량 × 단가 (읽기 전용)</summary>
        public decimal Amount
        {
            get { return _quantity * _unitPrice; }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Notify(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
```

---

## 4. 플러그인 진입점: 타이틀 + 그리드

`IFolderssPlugin`을 구현합니다.
- `Initialize`: 처음 실행할 때 한 번 호출됩니다.
- `CreateView`: 메뉴에서 실행할 때마다 호출되고, 돌려준 요소가 팝업 창의 내용이 됩니다.

화면 배치는 `DockPanel` 하나로 끝납니다. 타이틀은 위쪽(`Dock.Top`)에 붙이고, 마지막 자식인 그리드가 남은 공간을 모두 채웁니다.

`samples\OrderFormPlugin\OrderFormPlugin.cs`

```csharp
using Folderss.Plugins;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OrderFormPlugin
{
    public sealed class OrderFormPlugin : IFolderssPlugin
    {
        private IPluginManager _manager;

        public void Initialize(IPluginManager manager)
        {
            // 본체 기능(설정·폴더 패널 등)을 쓰려면 보관해 둔다. 이 튜토리얼에서는 쓰지 않는다.
            _manager = manager;
        }

        public FrameworkElement CreateView()
        {
            var root = new DockPanel { Margin = new Thickness(16) };

            // 1) 타이틀
            var title = new TextBlock
            {
                Text = "주문서",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);

            // 2) 그리드 (마지막 자식이라 남은 공간을 모두 채운다)
            root.Children.Add(CreateGrid());
            return root;
        }

        private static DataGrid CreateGrid()
        {
            var items = new ObservableCollection<OrderItem>
            {
                new OrderItem { Product = "A4 복사용지", Quantity = 10, UnitPrice = 4500m },
                new OrderItem { Product = "볼펜(검정)", Quantity = 50, UnitPrice = 300m },
                new OrderItem { Product = "스테이플러", Quantity = 2, UnitPrice = 12000m }
            };

            var grid = new DataGrid
            {
                ItemsSource = items,
                AutoGenerateColumns = false,
                CanUserAddRows = true,       // 맨 아래 빈 행에 입력하면 새 품목이 추가된다
                CanUserDeleteRows = true,    // 행을 선택하고 Delete
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
            };

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "품목",
                Binding = new Binding(nameof(OrderItem.Product)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "수량",
                Binding = new Binding(nameof(OrderItem.Quantity)),
                Width = 80
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "단가",
                Binding = new Binding(nameof(OrderItem.UnitPrice)) { StringFormat = "N0" },
                Width = 100
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "금액",
                Binding = new Binding(nameof(OrderItem.Amount)) { Mode = BindingMode.OneWay, StringFormat = "N0" },
                IsReadOnly = true,
                Width = 120
            });

            ApplyTheme(grid);
            return grid;
        }

        /// <summary>
        /// Folderss 테마에는 DataGrid 스타일이 없어서 기본 모양(밝은 배경·회색 헤더)이 나온다.
        /// 본체 테마 리소스 키에 DynamicResource로 연결하면 테마를 바꿀 때 함께 바뀐다.
        /// </summary>
        private static void ApplyTheme(DataGrid grid)
        {
            grid.SetResourceReference(Control.BackgroundProperty, "PanelBackground");
            grid.SetResourceReference(Control.ForegroundProperty, "PrimaryText");
            grid.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
            grid.SetResourceReference(DataGrid.RowBackgroundProperty, "PanelBackground");
            grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "BorderBrush");

            var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            header.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("SurfaceBackground")));
            header.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryText")));
            header.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("BorderBrush")));
            header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
            grid.ColumnHeaderStyle = header;
        }
    }
}
```

포인트:
- **`CreateView`는 매번 새 화면을 만듭니다.** 메뉴를 두 번 실행하면 주문서 창이 두 개 뜨고, 각 창은 따로 입력됩니다. 만든 요소를 필드에 담아 두고 다시 돌려주면 "이미 다른 부모가 있다"는 WPF 예외가 납니다.
- **금액 칸**은 `Mode = OneWay`, `IsReadOnly = true`로 둡니다. 계산값이라 편집할 수 없기 때문입니다.
- **`ApplyTheme`**: Folderss 테마에는 DataGrid 스타일이 없습니다. 이 부분을 빼면 어두운 테마에서도 흰 배경, 회색 헤더가 나옵니다. 색을 직접 넣지 말고 테마 리소스 키(`PanelBackground`, `PrimaryText` 등)에 연결해야 테마를 바꿀 때 함께 바뀝니다.

---

## 5. 빌드

```powershell
dotnet build .\samples\OrderFormPlugin
```

`samples\OrderFormPlugin\bin\Debug\net8.0-windows\OrderFormPlugin.zip`이 생깁니다. 압축을 열어 보면 두 파일만 들어 있어야 합니다.

```
OrderFormPlugin.dll
plugin.json
```

---

## 6. Folderss에 등록하고 실행

1. Folderss를 실행하고 `⋯ 메뉴 > 설정 > 플러그인`을 엽니다.
2. `플러그인 찾기…`를 누르고 5단계의 `OrderFormPlugin.zip`을 고릅니다.
3. 권한 경고를 확인하고 `예`를 누르면 목록에 `주문서 1.0.0 tutorial.order-form`이 나타납니다.
4. 설정 창을 닫고 `⋯ 메뉴 > 플러그인 > 주문서`를 누르면 주문서 팝업이 열립니다.

확인해 볼 것:
- [ ] 수량 `10`을 `20`으로 고치고 다른 칸으로 이동하면 금액이 `90,000`이 된다
- [ ] 맨 아래 빈 행에 품목을 입력하면 행이 추가된다
- [ ] `⋯ 메뉴 > 테마`를 바꾸면 그리드 배경·글자·헤더 색이 따라 바뀐다
- [ ] 메뉴에서 한 번 더 실행하면 독립된 주문서 창이 하나 더 뜬다

---

## 7. 코드를 고쳤을 때

1. `dotnet build .\samples\OrderFormPlugin`
2. 설정 > 플러그인 > `플러그인 찾기…`로 같은 zip을 다시 등록합니다(같은 `id`라 교체됩니다).
3. **Folderss를 재시작합니다.** 이미 로드된 DLL은 내릴 수 없어서, 재시작해야 새 버전이 로드됩니다.

---

## 8. 문제 해결

| 증상 | 원인과 해결 |
|---|---|
| 등록할 때 "플러그인 파일이 아닙니다" | zip 루트에 `plugin.json`이 없거나 JSON 형식 오류, 또는 `assembly`로 지정한 DLL이 zip에 없음 |
| 실행할 때 "플러그인을 열지 못했습니다 … TypeLoadException" | `plugin.json`의 `type` 오타. `OrderFormPlugin.OrderFormPlugin`처럼 네임스페이스까지 적었는지 확인 |
| "… IFolderssPlugin을 구현하지 않습니다" | 클래스가 `IFolderssPlugin`을 구현하지 않았거나, 다른 버전의 계약 DLL을 참조함 |
| 메뉴에 "(등록된 플러그인 없음)"만 보임 | 설정 > 플러그인 목록을 확인. 아래쪽에 "읽지 못한 zip n개"가 보이면 그 툴팁에 이유가 있음 |
| 어두운 테마에서 그리드만 흰색 | `ApplyTheme(grid)` 호출이 빠짐 |
| 고친 코드가 반영되지 않음 | 7단계의 **재시작** 누락 |

오류 기록은 `%LOCALAPPDATA%\Folderss\plugin-log.txt`에서 볼 수 있습니다.

---

## 9. 다음 단계

- **주문 저장**: `Initialize`에서 받은 `_manager.DataDirectory`에 `orders.json`으로 저장하고(`System.Text.Json`), `CreateView`에서 읽어 오세요. 저장은 종료 알림이 없으므로 값이 바뀔 때마다 하거나 저장 버튼을 두세요.
- **합계 표시**: `DockPanel.SetDock(total, Dock.Bottom)`인 `TextBlock`을 그리드보다 **먼저** `Children`에 추가하고, `items.CollectionChanged`와 각 품목의 `PropertyChanged`에서 합계를 다시 계산하세요.
- **기본값 설정 탭**: 부가세율 같은 값을 `IPluginSettingsPage`로 받으세요. [가이드 8절](plugin-development.md#8-설정-탭-ipluginsettingspage)을 참고하고, `plugin.json`에 `"hasSettings": true`를 넣으세요.
- **파일과 함께 쓰기**: `_manager.CreateFolderPanel(path)`로 팝업 옆에 폴더 패널을 붙여, 주문서를 저장할 폴더를 고르게 할 수 있습니다. 예제는 `samples\HelloPlugin`에 있습니다.
