# ArcPFDview — 完全仕様書

> 正式名称: **ArcPFDview** / コードネーム: **AcroPDF**

> バージョン: 1.0.0  
> 作成日: 2026-03-01  
> ステータス: 策定完了 / 開発着手前

---

## 1. プロジェクト概要

### 1.1 目的

Windows および Linux で動作する、Adobe Acrobat Reader 相当の機能を持つ軽量 PDF ビューワーを OSS として開発する。サブスクリプション不要・広告なし・起動が速いことを最重要要件とする。

### 1.2 ターゲットユーザー

- ビジネス文書を日常的に扱うオフィスワーカー
- 技術文書・論文を多数同時に参照する研究者・エンジニア
- Adobe Acrobat のコストを避けたい個人・中小企業

### 1.3 設計原則

- **軽量**: 起動 2 秒以内、メモリ使用量 150MB 以下（100 ページ PDF 表示時）
- **ネイティブ**: Web 技術（Electron/Tauri）を使わずネイティブ描画
- **MVVM 厳守**: View にビジネスロジックを書かない
- **非同期優先**: UI スレッドをブロックしない
- **拡張性**: 将来的なプラグイン対応を見据えた設計

---

## 2. 技術スタック

| 役割 | 採用技術 | 選定理由 |
|------|---------|---------|
| 言語 | C# 12 / .NET 8 | 型安全・高速・Codex との相性 |
| UI フレームワーク | Avalonia UI 11 | WPF 系 XAML / Win+Linux 対応 |
| PDF 描画エンジン | PDFium.NET SDK (NuGet) | Google Chrome 採用の高品質エンジン |
| 2D 描画 | SkiaSharp | Avalonia 標準の GPU 描画 |
| MVVM 基盤 | CommunityToolkit.Mvvm | Microsoft 公式・軽量 |
| テスト | xUnit + Moq | .NET 標準テストスタック |
| CI/CD | GitHub Actions | 自動ビルド・テスト |
| 配布 (Windows) | MSIX / self-contained exe | |
| 配布 (Linux) | AppImage / .deb | |

---

## 3. UI レイアウト仕様

UIデザインは `pdf-viewer-wireframe.html` を正とする。以下はその仕様を文書化したもの。

### 3.1 画面構成（全体レイアウト）

```
┌─────────────────────────────────────────────────────────────┐
│ ① メニューバー                                height: 28px  │
├─────────────────────────────────────────────────────────────┤
│ ② タブバー                                   height: 36px  │
├─────────────────────────────────────────────────────────────┤
│ ③ メインツールバー                           height: 40px  │
├──────────────┬──────────────────────────┬───────────────────┤
│              │                          │                   │
│ ④ 左サイドバー│   ⑤ メイン表示エリア     │  ⑥ 右サイドバー   │
│   width:220px│      (flex:1)            │    width:240px    │
│              │                          │                   │
├──────────────┴──────────────────────────┴───────────────────┤
│ ⑦ ステータスバー                             height: 22px  │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 ① メニューバー

高さ 28px。背景色 `#1e1e1e`。

**メニュー項目:**

- **ファイル**: 開く / 最近のファイル / 閉じる / 保存 / 名前を付けて保存 / 印刷 / 終了
- **編集**: テキストのコピー / すべて選択 / 検索 / 置換 / 環境設定
- **表示**: ズームイン/アウト / ページに合わせる / 幅に合わせる / 表示モード / サイドバー / フルスクリーン
- **ツール**: テキスト選択 / ハンドツール / ハイライト / コメント / 手書き / 図形
- **注釈**: すべての注釈を表示 / 注釈のエクスポート / 注釈の削除
- **ウィンドウ**: 分割ビュー / タブ一覧 / 新しいウィンドウ
- **ヘルプ**: バージョン情報 / ドキュメント

### 3.3 ② タブバー

高さ 36px。背景色 `#1e1e1e`。タブはファイル名を表示し、ホバーで閉じる (×) ボタンを表示。

**タブの状態:**

| 状態 | 背景色 | 文字色 |
|------|-------|-------|
| アクティブ | `#1a1a1a` + 上ボーダー除去 | `#e8e8e8` |
| 非アクティブ | `#2a2a2a` | `#999` |
| ホバー | `#333` | `#e8e8e8` |

- アイコン: PDFファイルアイコン（赤）
- 最大幅: 200px / 最小幅: 120px
- 末尾に「+」ボタンでファイルを開く
- タブをドラッグして並び替え可能
- タブを右クリックでコンテキストメニュー（複製 / 他を閉じる / 右を閉じる / エクスプローラーで開く）

### 3.4 ③ メインツールバー

高さ 40px。背景色 `#323232`。

**グループ構成（左から）:**

**グループA — 操作ツール**

| ボタン | アイコン | 機能 |
|-------|---------|------|
| 選択 | 🖐 | ハンドツール（スクロール・テキスト選択） |
| テキスト | T | テキスト選択モード |
| 検索 | 🔍 | 検索バー表示 (Ctrl+F) |

**グループB — 注釈ツール**

| ボタン | アイコン | 機能 |
|-------|---------|------|
| ハイライト | 🖊 | テキストハイライト |
| コメント | 💬 | コメント付箋を追加 |
| 手書き | ✍️ | フリーハンド描画 |
| 図形 | □ | 矩形・楕円・矢印 |

**グループC — 編集ツール**

| ボタン | アイコン | 機能 |
|-------|---------|------|
| 編集 | ✏️ | テキスト編集モード |
| 画像 | 🖼 | 画像の挿入・編集 |

**中央 — ページナビゲーション**

```
[⟨⟨] [⟨]  [ページ入力 (46px)]  / 48  [⟩] [⟩⟩]
```

- `⟨⟨` : 先頭ページへ
- `⟨` : 前のページへ（←キーでも可）
- ページ入力: 数値入力後 Enter でジャンプ
- `⟩` : 次のページへ（→キーでも可）
- `⟩⟩` : 最終ページへ

**右 — ズーム**

```
[−]  [ズームセレクト (100%▼)]  [+]
```

ズームセレクト選択肢: 25% / 50% / 75% / 100% / 125% / 150% / 200% / 300% / 400% / 幅に合わせる / ページに合わせる / 実寸

**表示モード**

| ボタン | 機能 |
|-------|------|
| ▣ 単ページ | 1ページずつ表示 |
| ⊟ 連続 | スクロールで全ページ連続表示 |
| ⊞ 分割 | 左右2ペインに分割表示 |

**その他**

| ボタン | 機能 |
|-------|------|
| 🖨 印刷 | 印刷ダイアログ |
| 💾 保存 | 注釈付きで上書き保存 |

### 3.5 ④ 左サイドバー

幅 220px。背景色 `#252525`。4 タブ構成。

**サイドバータブ:**

| タブ | アイコン | 内容 |
|-----|---------|------|
| サムネイル | ▤ | 全ページのサムネイル一覧 |
| 目次 | ≡ | PDFのブックマーク（アウトライン） |
| 注釈 | 🔖 | 注釈一覧（ページ順） |
| 添付 | 🔗 | PDF埋め込みファイル一覧 |

**サムネイルパネル詳細:**
- サムネイル幅: 140px / アスペクト比: A4 (0.707)
- 選択中: アクセントカラー `#e8521a` でボーダー
- クリックで該当ページへジャンプ
- ページ番号をサムネイル下に表示

**目次パネル詳細:**
- ツリー表示（インデント対応）
- 折りたたみ/展開（▶/▼）
- クリックで該当ページへジャンプ

### 3.6 ⑤ メイン表示エリア

背景色 `#3a3a3a`（ページ外のグレーキャンバス）。

**PDF ページ表示:**
- ページ背景: 白 (`#ffffff`)
- ドロップシャドウ: `0 4px 20px rgba(0,0,0,0.6)`
- ページ番号オーバーレイ: ページ右下に表示
- 連続表示時: ページ間ギャップ 16px

**検索バー（Ctrl+F で出現）:**
- 右上に固定オーバーレイ表示
- テキスト入力 + 件数表示 (3/12件) + ∧∨ ナビボタン + ×閉じるボタン
- マッチ箇所: 黄色ハイライト `rgba(255, 180, 0, 0.35)`
- 現在のマッチ: 青ハイライト `rgba(100, 160, 255, 0.35)`

**フローティング注釈ツールバー（テキスト選択時出現）:**
- 画面下部中央に固定表示
- 角丸ピル型デザイン
- ボタン: ハイライト / 下線 / 取り消し線 / コメント / 手書き / 矩形 / 楕円 / 矢印 / スタンプ

**右クリックコンテキストメニュー（テキスト選択時）:**
- コピー
- ハイライト
- 下線を引く
- 取り消し線
- ─（セパレータ）
- コメントを追加
- リンクを追加
- ─（セパレータ）
- 選択テキストを検索

**分割ビュー:**
- QSplitter 相当の分割ライン（ドラッグでリサイズ）
- 分割ラインの幅: 3px、色: `#444`、ホバー時: `#e8521a`
- 左右それぞれ独立したページ表示

### 3.7 ⑥ 右サイドバー

幅 240px。背景色 `#252525`。

**注釈パネル（上部）:**
- ヘッダー: 「注釈パネル」＋件数バッジ
- 注釈カードのカラーコード:
  - 🟡 黄: ハイライト
  - 🔵 青: コメント
  - 🔴 赤: 下線
  - 🟢 緑: 手書き / 図形
- カードクリックで該当ページ・該当注釈へジャンプ

**ファイル情報パネル（下部）:**
- ページ数 / ファイルサイズ / 作成日 / PDF バージョン / 暗号化状態

### 3.8 ⑦ ステータスバー

高さ 22px。背景色 `#1e1e1e`。フォント: `IBM Plex Mono`。

**表示内容（左から）:**
- ● 準備完了（緑ドット）
- 現在のファイル名
- 現在ページ / 総ページ数
- ズーム率
- マウスカーソル位置 (x, y)
- プラットフォーム + .NET バージョン（右端）

### 3.9 カラースキーム

```
--bg-dark:      #1a1a1a   (ウィンドウ背景・アクティブタブ)
--bg-panel:     #252525   (サイドバー背景)
--bg-panel2:    #2e2e2e   (ホバー・サブパネル)
--bg-toolbar:   #323232   (ツールバー背景)
--accent:       #e8521a   (アクセント：Adobe オレンジ系)
--accent2:      #f07030   (アクセントライト)
--text-primary: #e8e8e8
--text-secondary:#999
--text-dim:     #666
--border:       #3a3a3a
--border-light: #444
```

---

## 4. 機能仕様

### 4.1 ファイル管理

| 機能 | 詳細 |
|------|------|
| ファイルを開く | ダイアログ・ドラッグ&ドロップ・起動引数・最近のファイル |
| 複数ファイル | タブで管理。最大タブ数: 無制限（設定で上限可） |
| 最近のファイル | 最大 20 件。メニューとスタート画面に表示 |
| 起動時復元 | 前回終了時に開いていたファイルとページを復元（設定で ON/OFF） |
| ファイル監視 | 外部で変更された場合に再読み込みを促す |

### 4.2 ページ表示

| 機能 | 詳細 |
|------|------|
| レンダリング品質 | DPI: 96 × ZoomLevel。プリント品質 300DPI 対応 |
| 表示モード | 単ページ / 連続スクロール / 2 ページ見開き |
| ズーム | 25%〜400%。Ctrl+スクロール / ピンチイン・アウト |
| 回転 | 90° 単位の回転（表示のみ・保存可） |
| ページ移動 | ボタン / キーボード / サムネイルクリック / ページ番号入力 |
| フルスクリーン | F11 でフルスクリーン表示 |

### 4.3 テキスト操作

| 機能 | 詳細 |
|------|------|
| テキスト選択 | マウスドラッグで範囲選択 |
| コピー | Ctrl+C でクリップボードにコピー |
| 全選択 | Ctrl+A でページ内全テキスト選択 |
| 検索 | Ctrl+F で検索バー。大文字小文字区別・正規表現オプション |
| 次/前 | F3 / Shift+F3 で検索結果を順番に移動 |

### 4.4 注釈機能

| 種類 | 詳細 |
|------|------|
| ハイライト | 色: 黄 / 緑 / 青 / ピンク |
| 下線 | 実線・波線 |
| 取り消し線 | — |
| コメント（付箋） | テキスト入力・著者名・日時記録 |
| フリーハンド | ペン描画・色・太さ設定 |
| 図形 | 矩形・楕円・矢印・線 |
| スタンプ | 承認済み・要確認 等のプリセット |
| 注釈の保存 | PDF に直接埋め込み。FDF でのエクスポートも可 |
| 注釈の削除 | 個別削除・一括削除 |

### 4.5 フォーム入力

| 機能 | 詳細 |
|------|------|
| テキストフィールド | クリックで編集可能 |
| チェックボックス | クリックで ON/OFF |
| ラジオボタン | グループ内で排他選択 |
| ドロップダウン | リストから選択 |
| 署名フィールド | 手書き署名・テキスト署名 |

### 4.6 ページ編集

| 機能 | 詳細 |
|------|------|
| ページ抽出 | 指定ページを新規 PDF に保存 |
| ページ削除 | 指定ページを削除して保存 |
| ページ回転 | ページ単位で 90°/180°/270° 回転して保存 |
| PDF 結合 | 複数 PDF を結合して保存 |

### 4.7 印刷

| 機能 | 詳細 |
|------|------|
| 印刷 | Ctrl+P で印刷ダイアログ |
| 範囲 | 全ページ / 現在のページ / ページ範囲指定 |
| サイズ | 実寸 / ページに合わせる / 用紙に合わせる |
| 向き | 縦 / 横 / 自動 |

### 4.8 分割ビュー

- ツールバーの「分割」ボタンで現在のタブを左右 2 ペインに分割
- 各ペインは独立してページ・ズームを操作可能
- 同ファイルの異なるページを並べて比較可能
- 異なるファイルを並べることも可能（左右にそれぞれタブから選択）
- 分割ラインはドラッグでリサイズ

---

## 5. ディレクトリ構造

```
AcroPDF/
├── .github/
│   ├── workflows/
│   │   ├── build.yml
│   │   └── test.yml
│   ├── copilot-instructions.md         ← Copilot Agent 全体指示
│   └── skills/
│       ├── pdf-rendering.md
│       ├── avalonia-patterns.md
│       ├── mvvm-guide.md
│       └── testing-guide.md
├── src/
│   ├── AcroPDF.Core/
│   │   ├── Models/
│   │   │   ├── PdfDocument.cs
│   │   │   ├── PdfPage.cs
│   │   │   ├── PdfMetadata.cs
│   │   │   ├── Annotation.cs           ← 基底クラス
│   │   │   ├── HighlightAnnotation.cs
│   │   │   ├── CommentAnnotation.cs
│   │   │   ├── FreehandAnnotation.cs
│   │   │   ├── ShapeAnnotation.cs
│   │   │   ├── SearchResult.cs
│   │   │   └── AppSettings.cs
│   │   └── AcroPDF.Core.csproj
│   ├── AcroPDF.Services/
│   │   ├── Interfaces/
│   │   │   ├── IPdfRenderService.cs
│   │   │   ├── ISearchService.cs
│   │   │   ├── IAnnotationService.cs
│   │   │   └── ISettingsService.cs
│   │   ├── PdfiumRenderService.cs
│   │   ├── SearchService.cs
│   │   ├── AnnotationService.cs
│   │   └── SettingsService.cs
│   ├── AcroPDF.ViewModels/
│   │   ├── MainWindowViewModel.cs
│   │   ├── TabViewModel.cs
│   │   ├── ThumbnailPanelViewModel.cs
│   │   ├── ThumbnailViewModel.cs
│   │   ├── BookmarkPanelViewModel.cs
│   │   ├── AnnotationPanelViewModel.cs
│   │   ├── AnnotationViewModel.cs
│   │   ├── SearchViewModel.cs
│   │   └── StatusBarViewModel.cs
│   └── AcroPDF.App/
│       ├── Views/
│       │   ├── MainWindow.axaml
│       │   ├── MainWindow.axaml.cs
│       │   ├── PdfViewerView.axaml
│       │   ├── ThumbnailPanelView.axaml
│       │   ├── BookmarkPanelView.axaml
│       │   ├── AnnotationPanelView.axaml
│       │   └── SearchBarView.axaml
│       ├── Controls/
│       │   ├── PdfPageControl.cs       ← SkiaSharp カスタムコントロール
│       │   ├── TabBarControl.axaml
│       │   └── AnnotationOverlay.cs
│       ├── Converters/
│       │   └── ValueConverters.cs
│       ├── Assets/
│       │   └── Styles/
│       │       ├── Colors.axaml
│       │       ├── Typography.axaml
│       │       └── Controls.axaml
│       ├── App.axaml
│       ├── App.axaml.cs
│       └── AcroPDF.App.csproj
└── tests/
    ├── AcroPDF.Core.Tests/
    ├── AcroPDF.Services.Tests/
    └── AcroPDF.ViewModels.Tests/
```

---

## 6. クラス設計詳細

### 6.1 Models

```csharp
// PdfDocument.cs
public sealed class PdfDocument : IDisposable
{
    public string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath);
    public int PageCount { get; init; }
    public PdfMetadata Metadata { get; init; }
    public IReadOnlyList<PdfBookmark> Bookmarks { get; init; }
    public List<Annotation> Annotations { get; } = new();
    public bool IsModified { get; private set; }

    public Task<PdfPage> GetPageAsync(int pageIndex);
    public void MarkModified();
    public void Dispose();
}

// PdfPage.cs
public sealed class PdfPage
{
    public int PageNumber { get; init; }   // 1-based
    public double WidthPt { get; init; }   // ポイント単位
    public double HeightPt { get; init; }
    public double AspectRatio => HeightPt / WidthPt;

    public Task<SKBitmap> RenderAsync(double dpi, CancellationToken ct = default);
    public IEnumerable<TextBlock> GetTextBlocks();
    public IEnumerable<Rect> FindText(string query, bool caseSensitive = false);
}

// Annotation.cs（基底）
public abstract class Annotation
{
    public Guid Id { get; } = Guid.NewGuid();
    public int PageNumber { get; set; }
    public Rect Bounds { get; set; }
    public string Author { get; set; } = Environment.UserName;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public string? Comment { get; set; }
}

public sealed class HighlightAnnotation : Annotation
{
    public Color Color { get; set; } = Colors.Yellow;
    public HighlightType Type { get; set; } = HighlightType.Highlight;
    // Highlight / Underline / Strikethrough / Squiggly
}

public sealed class CommentAnnotation : Annotation
{
    public required string Text { get; set; }
    public bool IsOpen { get; set; } = false;
}

public sealed class FreehandAnnotation : Annotation
{
    public List<List<Point>> Strokes { get; set; } = new();
    public Color Color { get; set; } = Colors.Red;
    public double StrokeWidth { get; set; } = 2.0;
}

public sealed class ShapeAnnotation : Annotation
{
    public ShapeType Type { get; set; }  // Rectangle / Ellipse / Arrow / Line
    public Color StrokeColor { get; set; } = Colors.Red;
    public Color? FillColor { get; set; }
    public double StrokeWidth { get; set; } = 2.0;
}
```

### 6.2 Services

```csharp
// IPdfRenderService.cs
public interface IPdfRenderService : IDisposable
{
    Task<PdfDocument> OpenAsync(string filePath, string? password = null);
    Task<SKBitmap> RenderPageAsync(PdfPage page, double dpi, CancellationToken ct = default);
    Task SaveAsync(PdfDocument document, string? outputPath = null);
    Task SaveAnnotationsAsync(PdfDocument document);
    void Close(PdfDocument document);
}

// ISearchService.cs
public interface ISearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        PdfDocument document, string query,
        SearchOptions options, CancellationToken ct = default);
}

public record SearchResult(int PageNumber, Rect Bounds, int MatchIndex);
public record SearchOptions(bool CaseSensitive, bool UseRegex, bool WholeWord);

// IAnnotationService.cs
public interface IAnnotationService
{
    void Add(PdfDocument document, Annotation annotation);
    void Remove(PdfDocument document, Guid annotationId);
    void Update(PdfDocument document, Annotation annotation);
    Task ExportAsFdfAsync(PdfDocument document, string outputPath);
    Task ImportFdfAsync(PdfDocument document, string fdfPath);
}

// ISettingsService.cs
public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
    void AddRecentFile(string filePath);
    IReadOnlyList<string> GetRecentFiles();
    void RemoveRecentFile(string filePath);
}
```

### 6.3 ViewModels

```csharp
// MainWindowViewModel.cs
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<TabViewModel> _tabs = new();
    [ObservableProperty] private TabViewModel? _activeTab;
    [ObservableProperty] private bool _isSplitView;
    [ObservableProperty] private TabViewModel? _splitSecondaryTab;

    [RelayCommand] private async Task OpenFileAsync();
    [RelayCommand] private async Task OpenFilePathAsync(string path);
    [RelayCommand] private void CloseTab(TabViewModel tab);
    [RelayCommand] private void ToggleSplitView();
    [RelayCommand] private async Task PrintAsync();
}

// TabViewModel.cs
public partial class TabViewModel : ObservableObject, IDisposable
{
    public PdfDocument Document { get; }

    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private double _zoomLevel = 1.0;
    [ObservableProperty] private ViewMode _viewMode = ViewMode.Continuous;
    [ObservableProperty] private SidebarTab _activeSidebarTab = SidebarTab.Thumbnails;
    [ObservableProperty] private bool _isSearchVisible;
    [ObservableProperty] private string _searchQuery = "";

    public ThumbnailPanelViewModel ThumbnailPanel { get; }
    public BookmarkPanelViewModel BookmarkPanel { get; }
    public AnnotationPanelViewModel AnnotationPanel { get; }
    public SearchViewModel Search { get; }
    public StatusBarViewModel StatusBar { get; }

    [RelayCommand] private void NextPage();
    [RelayCommand] private void PrevPage();
    [RelayCommand] private void ZoomIn();
    [RelayCommand] private void ZoomOut();
    [RelayCommand] private void FitToWidth();
    [RelayCommand] private void FitToPage();
}
```

### 6.4 列挙型

```csharp
public enum ViewMode { SinglePage, Continuous, TwoPage }
public enum SidebarTab { Thumbnails, Bookmarks, Annotations, Attachments }
public enum HighlightType { Highlight, Underline, Strikethrough, Squiggly }
public enum ShapeType { Rectangle, Ellipse, Arrow, Line }
public enum ActiveTool { Hand, TextSelect, Highlight, Comment, Freehand, Shape }
```

---

## 7. キーボードショートカット

| ショートカット | 機能 |
|--------------|------|
| Ctrl+O | ファイルを開く |
| Ctrl+W | 現在のタブを閉じる |
| Ctrl+S | 保存 |
| Ctrl+P | 印刷 |
| Ctrl+F | 検索 |
| Ctrl+G | ページへジャンプ |
| Ctrl+= / Ctrl++ | ズームイン |
| Ctrl+- | ズームアウト |
| Ctrl+0 | 100% に戻す |
| Ctrl+Shift+H | 幅に合わせる |
| Ctrl+Shift+F | ページに合わせる |
| ← / → | 前/次のページ（単ページモード時） |
| Home / End | 先頭/最終ページ |
| F11 | フルスクリーン |
| Escape | 検索バー閉じる / ツール解除 |
| Ctrl+Tab / Ctrl+Shift+Tab | 次/前のタブ |
| F3 / Shift+F3 | 次/前の検索結果 |

---

## 8. 非機能要件

| 項目 | 要件 |
|------|------|
| 起動時間 | コールドスタート 2 秒以内 |
| メモリ | 100 ページ PDF 表示時 150MB 以下 |
| レンダリング | ページ切り替え 200ms 以内（キャッシュあり） |
| 対応 OS | Windows 10/11 x64、Ubuntu 22.04+、Debian 12+ |
| .NET | .NET 8 (self-contained 配布のため runtime 不要) |
| 最大 PDF サイズ | 1GB のファイルを開ける（ストリーミング読み込み） |
| 多言語 | 日本語・英語（UI ローカライズ対応） |
| アクセシビリティ | キーボードのみの完全操作 |

---

## 9. 設定仕様

```csharp
public record AppSettings
{
    // 表示
    public double DefaultZoom { get; init; } = 1.0;
    public ViewMode DefaultViewMode { get; init; } = ViewMode.Continuous;
    public bool RestoreSessionOnStartup { get; init; } = true;
    public int MaxRecentFiles { get; init; } = 20;

    // 注釈
    public Color DefaultHighlightColor { get; init; } = Colors.Yellow;
    public string DefaultAnnotationAuthor { get; init; } = Environment.UserName;

    // パフォーマンス
    public int PageCacheSize { get; init; } = 10;  // キャッシュするページ数
    public double RenderDpiMultiplier { get; init; } = 1.0;

    // UI
    public double LeftSidebarWidth { get; init; } = 220;
    public double RightSidebarWidth { get; init; } = 240;
    public bool ShowStatusBar { get; init; } = true;
    public string Language { get; init; } = "ja";
}
```

設定ファイルの保存先:
- Windows: `%APPDATA%\AcroPDF\settings.json`
- Linux: `~/.config/AcroPDF/settings.json`

---

## 10. エラーハンドリング方針

- パスワード保護されたPDF: パスワード入力ダイアログを表示
- 破損したPDF: エラーダイアログ + 読み込めた範囲まで表示を試みる
- 巨大なPDF: プログレスバー付きローディング表示
- ファイルが見つからない: ダイアログで通知し、最近のファイルリストから削除
- レンダリング失敗: 該当ページにエラー表示（他ページへの影響なし）
- 未保存の注釈がある状態で閉じる: 保存確認ダイアログ

---

*このドキュメントは `pdf-viewer-wireframe.html` と対になる仕様書です。UIデザインは同HTMLファイルを正とします。*
