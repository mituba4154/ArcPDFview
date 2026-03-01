# AcroPDF — Copilot Agent Instructions

> 正式名称: **ArcPDFview**（コードネーム: **AcroPDF**）

> このファイルは GitHub Copilot Agent (Codex) が自動的に参照する全体指示書です。  
> 作業前に必ずこのファイルを読み、現在の進捗状況と実装ルールを把握してください。

---

## プロジェクト概要

**AcroPDF** — Windows / Linux 向けの軽量 PDF ビューワー  
Adobe Acrobat Reader と同等の機能を持ち、サブスクリプション不要・広告なし。

```
言語:    C# 12 / .NET 8
UI:      Avalonia UI 11
PDF:     PDFium.NET SDK
描画:    SkiaSharp
MVVM:    CommunityToolkit.Mvvm
テスト:  xUnit + Moq
```

詳細仕様: `docs/spec.md`  
工程計画: `docs/phases.md`  
UI参照:   `docs/pdf-viewer-wireframe.html`

---

## コーディングルール（必ず守ること）

### 絶対ルール

```csharp
// ✅ 正しい: nullable 有効化
#nullable enable

// ✅ 正しい: public メンバーに XML doc コメント
/// <summary>PDFページをレンダリングします。</summary>
/// <param name="page">対象ページ</param>
/// <param name="dpi">解像度</param>
public async Task<SKBitmap> RenderPageAsync(PdfPage page, double dpi) { ... }

// ✅ 正しい: I/O・レンダリングは非同期
public async Task<PdfDocument> OpenAsync(string path, CancellationToken ct = default)

// ❌ 禁止: View に直接ロジックを書く
public partial class MainWindow : Window
{
    private void Button_Click(object sender, RoutedEventArgs e)
    {
        // ❌ ここでPDFを開かない！ ViewModelに委譲する
    }
}

// ✅ 正しい: Command 経由で ViewModel に処理させる
// View の XAML: <Button Command="{Binding OpenFileCommand}" />
```

### 命名規則

| 対象 | 規則 | 例 |
|------|------|---|
| ViewModel | `*ViewModel.cs` | `TabViewModel.cs` |
| View (XAML) | `*View.axaml` | `ThumbnailPanelView.axaml` |
| Window (XAML) | `*Window.axaml` | `MainWindow.axaml` |
| Interface | `I*Service.cs` | `IPdfRenderService.cs` |
| Service 実装 | `*Service.cs` | `PdfiumRenderService.cs` |
| Control | `*Control.cs` | `PdfPageControl.cs` |
| テスト | `*Tests.cs` | `PdfiumRenderServiceTests.cs` |

### ディレクトリ規則

```
src/AcroPDF.Core/          → Models のみ（UI依存なし）
src/AcroPDF.Services/      → ビジネスロジック・外部ライブラリのラッパー
src/AcroPDF.ViewModels/    → ViewModel のみ（Avalonia依存なし）
src/AcroPDF.App/           → View・Control・App エントリポイント
tests/                     → テストプロジェクト
```

### カラースキーム参照

UIの色は `src/AcroPDF.App/Assets/Styles/Colors.axaml` の変数を使うこと。  
直接 `#1a1a1a` のようなハードコードをしない。

```xml
<!-- ✅ 正しい -->
<SolidColorBrush Color="{DynamicResource BgDark}" />

<!-- ❌ 禁止 -->
<SolidColorBrush Color="#1a1a1a" />
```

| 変数名 | 値 | 用途 |
|--------|-----|------|
| `BgDark` | `#1a1a1a` | ウィンドウ背景・アクティブタブ |
| `BgPanel` | `#252525` | サイドバー背景 |
| `BgPanel2` | `#2e2e2e` | ホバー・サブパネル |
| `BgToolbar` | `#323232` | ツールバー背景 |
| `Accent` | `#e8521a` | アクセントカラー（Adobe オレンジ系） |
| `Accent2` | `#f07030` | アクセントライト |
| `TextPrimary` | `#e8e8e8` | メインテキスト |
| `TextSecondary` | `#999999` | サブテキスト |
| `TextDim` | `#666666` | ヒント・補足テキスト |
| `Border` | `#3a3a3a` | 境界線 |
| `BorderLight` | `#444444` | 明るい境界線 |

---

## 進捗管理

> ✅ = 完了 / 🔄 = 作業中 / ⬜ = 未着手 / ❌ = ブロック中

---

### Phase 0 — 基盤構築

**ステータス:** ✅ 完了  
**最終更新日:** 2026-03-01  
**担当 Codex セッション:** Codex (GPT-5)

| # | タスク | 状態 | 備考 |
|---|--------|------|------|
| 0-1 | .NET 8 / Avalonia 11 環境確認 | ✅ | 2026-03-01 完了。`dotnet --version`、`dotnet build ArcPDFview.sln`、`src/AcroPDF.App/Program.cs` |
| 0-2 | ソリューション構造の作成 | ✅ | 2026-03-01 完了。`ArcPDFview.sln`、`src/AcroPDF.*`、`tests/AcroPDF.*.Tests` |
| 0-3 | NuGet パッケージ追加 | ✅ | 2026-03-01 完了。`src/AcroPDF.App/AcroPDF.App.csproj`、`src/AcroPDF.ViewModels/AcroPDF.ViewModels.csproj`、`src/AcroPDF.Services/AcroPDF.Services.csproj` |
| 0-4 | 空ウィンドウ起動確認 | ✅ | 2026-03-01 完了。`src/AcroPDF.App/App.axaml`、`src/AcroPDF.App/Views/MainWindow.axaml` |
| 0-5 | カラースキーム定義 Colors.axaml | ✅ | 2026-03-01 完了。`src/AcroPDF.App/Assets/Styles/Colors.axaml` |
| 0-6 | copilot-instructions.md 配置 | ✅ | 2026-03-01 完了。`.github/copilot-instructions.md` |
| 0-7 | GitHub Actions 設定 | ✅ | 2026-03-01 完了。`.github/workflows/build.yml`（Win + Linux マトリクス） |
| 0-8 | README.md 作成 | ✅ | 2026-03-01 完了。`README.md` |

**Phase 0 完了チェックリスト:**
- [x] `dotnet build` がエラーなく通る
- [x] Avalonia ウィンドウが Windows / Linux で起動する
- [x] GitHub Actions 初回ビルドが成功する

**⚠️ Phase 0 実装時の注意点:**

```
PDFium の NuGet パッケージについて:
  - 第一: Sdcb.PdfiumBuild (MIT ライセンス)
  
Linux での PDFium:
  - libpdfium.so のパスを RuntimeIdentifier ごとに設定が必要な場合あり
  - .csproj の RuntimeIdentifier: win-x64, linux-x64 を明示すること
  
Avalonia のテーマ:
  - Fluent テーマをベースにカスタムスタイルを重ねる
  - テーマの初期化は App.axaml.cs で行う
```

---

### Phase 1 — コア PDF 表示機能

**ステータス:** ⬜ 未着手  
**最終更新日:** —  
**担当 Codex セッション:** —  
**前提:** Phase 0 の全タスク完了

| # | タスク | 状態 | 備考 |
|---|--------|------|------|
| 1-1 | `IPdfRenderService` インターフェース実装 | ⬜ | |
| 1-2 | `PdfiumRenderService` の実装 | ⬜ | PDFium の `FPDFDocument_*` API を使用 |
| 1-3 | `PdfDocument` / `PdfPage` モデルの実装 | ⬜ | |
| 1-4 | パスワード付き PDF の対応 | ⬜ | ダイアログが必要 |
| 1-5 | ファイルドラッグ&ドロップ | ⬜ | Avalonia の `DragDrop` イベント |
| 1-6 | 起動引数からファイルを開く | ⬜ | `App.axaml.cs` の `OnStartup` |
| 1-7 | `PdfPageControl` (SkiaSharp) の作成 | ⬜ | `SKCanvasView` を継承 |
| 1-8 | 指定 DPI でページを `SKBitmap` にレンダリング | ⬜ | |
| 1-9 | ページキャッシュの実装 | ⬜ | `ConcurrentDictionary<int, SKBitmap>` |
| 1-10 | ズーム機能（25%〜400%） | ⬜ | |
| 1-11 | Ctrl+スクロールでズーム | ⬜ | |
| 1-12 | 幅/ページ/実寸に合わせる | ⬜ | |
| 1-13 | ツールバーのナビゲーションボタン | ⬜ | |
| 1-14 | ページ番号入力ジャンプ | ⬜ | |
| 1-15 | キーボードショートカット (← →) | ⬜ | |
| 1-16 | 連続スクロールモード | ⬜ | 仮想化は Phase 5 でよい |
| 1-17 | 単ページモード | ⬜ | |
| 1-18 | `TabViewModel` の実装 | ⬜ | |
| 1-19 | タブバー UI | ⬜ | wireframe を参照 |
| 1-20 | 「+」ボタンでファイルを開く | ⬜ | |
| 1-21 | タブ右クリックメニュー | ⬜ | |
| 1-22 | Ctrl+W でタブを閉じる | ⬜ | |
| 1-23 | 最後のタブを閉じても終了しない | ⬜ | |
| 1-24 | サムネイルパネルの基本 UI | ⬜ | |
| 1-25 | 非同期サムネイル生成 | ⬜ | `SemaphoreSlim` で 4 並列上限 |
| 1-26 | サムネイルクリックでジャンプ | ⬜ | |
| 1-27 | 現在ページのサムネイルをハイライト | ⬜ | |
| 1-28 | ステータスバーの基本表示 | ⬜ | |
| 1-29 | マウス座標の表示 | ⬜ | |

**Phase 1 完了チェックリスト:**
- [ ] PDF を開いて全ページ表示できる
- [ ] ズーム・ページ移動が正常に動作する
- [ ] 3 ファイルをタブで同時に開ける
- [ ] サムネイルサイドバーが動作する

**⚠️ Phase 1 実装時の注意点:**

```
PdfPageControl の実装:
  - Avalonia の SKCanvasView を継承
  - OnPaintSurface で SKCanvas に直接描画
  - ZoomLevel と CurrentPage を ObservableProperty にする
  - レンダリングは Task.Run でバックグラウンドスレッドに逃がす
  
ページキャッシュ:
  - ConcurrentDictionary<int, SKBitmap> で実装
  - Phase 5 で LRU に置き換える前提でシンプルに実装してよい
  - タブを閉じたとき Dispose で全キャッシュを破棄すること
  
連続スクロールモード（Phase 1 では簡易実装でよい）:
  - 全ページを ScrollViewer + StackPanel で縦並びにする
  - 500 ページ以上のパフォーマンスは Phase 5 で最適化
  
サムネイル生成:
  - 低 DPI（幅 140px ÷ ページ幅pt × 72 DPI）でレンダリング
  - SemaphoreSlim(3) で 3 並列に制限
  - CancellationToken でタブを閉じたときに生成を中断できるようにする
  
PDFium 座標系:
  - PDFium は左下原点、画面は左上原点
  - 変換: screenY = pageHeight - pdfY
```

---

### Phase 2 — 閲覧体験の完成

**ステータス:** ⬜ 未着手  
**最終更新日:** —  
**担当 Codex セッション:** —  
**前提:** Phase 1 の全タスク完了

| # | タスク | 状態 | 備考 |
|---|--------|------|------|
| 2-1 | `ISearchService` / `SearchService` | ⬜ | `FPDFText_FindStart` API |
| 2-2 | 検索バー UI (Ctrl+F) | ⬜ | 右上オーバーレイ |
| 2-3 | マッチハイライト（黄色） | ⬜ | |
| 2-4 | 現在マッチハイライト（青） | ⬜ | |
| 2-5 | 検索件数表示 | ⬜ | |
| 2-6 | F3 / Shift+F3 ナビゲーション | ⬜ | |
| 2-7 | 大文字小文字区別オプション | ⬜ | |
| 2-8 | 正規表現検索 | ⬜ | |
| 2-9 | PDF ブックマーク取得 | ⬜ | `FPDF_Bookmark_*` API |
| 2-10 | 目次ツリー表示 | ⬜ | |
| 2-11 | 目次クリックでジャンプ | ⬜ | |
| 2-12 | テキスト選択モード | ⬜ | `FPDFText_GetCharBox` API |
| 2-13 | マウスドラッグで範囲選択 | ⬜ | |
| 2-14 | Ctrl+C でコピー | ⬜ | |
| 2-15 | 右クリックコンテキストメニュー | ⬜ | wireframe 参照 |
| 2-16 | 分割ビュー UI | ⬜ | |
| 2-17 | 各ペイン独立操作 | ⬜ | |
| 2-18 | 分割ラインのリサイズ | ⬜ | |
| 2-19 | 同ファイルの異なるページを並べる | ⬜ | |
| 2-20 | 異なるファイルを並べる | ⬜ | |
| 2-21 | 2 ページ見開きモード | ⬜ | |
| 2-22 | ページ回転（表示のみ） | ⬜ | |
| 2-23 | フルスクリーン（F11） | ⬜ | |
| 2-24 | ファイル監視・再読み込み提案 | ⬜ | `FileSystemWatcher` |
| 2-25 | `ISettingsService` / `SettingsService` | ⬜ | JSON 設定ファイル |
| 2-26 | 最近のファイル | ⬜ | |
| 2-27 | セッション復元 | ⬜ | |

**Phase 2 完了チェックリスト:**
- [ ] テキスト検索が動作しマッチがハイライトされる
- [ ] 目次サイドバーが機能する
- [ ] 分割ビューで 2 ページを同時表示できる
- [ ] セッション復元が動作する

**⚠️ Phase 2 実装時の注意点:**

```
検索ハイライトの描画:
  - PdfPageControl に List<Rect> SearchHighlights プロパティを追加
  - SKCanvas の描画ループ内で半透明矩形として重ね書き
  - 現在マッチ: rgba(100, 160, 255, 0.35)、他のマッチ: rgba(255, 180, 0, 0.35)
  
分割ビューの ViewModel 設計:
  - MainWindowViewModel に IsSplitView と SplitSecondaryTab プロパティを追加
  - 左ペイン = ActiveTab、右ペイン = SplitSecondaryTab
  
設定ファイルのパス:
  - Windows: %APPDATA%\AcroPDF\settings.json
  - Linux: ~/.config/AcroPDF/settings.json
  - ISettingsService 実装内で OS を判定して切り替える
```

---

### Phase 3 — 注釈機能

**ステータス:** ⬜ 未着手  
**最終更新日:** —  
**担当 Codex セッション:** —  
**前提:** Phase 2 の全タスク完了

| # | タスク | 状態 | 備考 |
|---|--------|------|------|
| 3-1 | Annotation 基底クラス / 派生クラス | ⬜ | |
| 3-2 | `IAnnotationService` / `AnnotationService` | ⬜ | |
| 3-3 | 注釈を PDF に直接埋め込み保存 | ⬜ | `FPDF_Annot_*` API |
| 3-4 | 既存注釈の読み込み | ⬜ | |
| 3-5 | ハイライト（4色） | ⬜ | |
| 3-6 | 下線 | ⬜ | |
| 3-7 | 取り消し線 | ⬜ | |
| 3-8 | 右クリックメニューから注釈追加 | ⬜ | |
| 3-9 | コメント付箋 UI | ⬜ | |
| 3-10 | テキスト入力・編集 | ⬜ | |
| 3-11 | 著者名・日時の記録 | ⬜ | |
| 3-12 | 付箋の開閉 | ⬜ | |
| 3-13 | フリーハンド描画モード | ⬜ | |
| 3-14 | ストローク記録 | ⬜ | |
| 3-15 | 色・太さの設定 UI | ⬜ | |
| 3-16 | 矩形の描画 | ⬜ | |
| 3-17 | 楕円の描画 | ⬜ | |
| 3-18 | 矢印の描画 | ⬜ | |
| 3-19 | 線の描画 | ⬜ | |
| 3-20 | ストロークカラー・塗り・太さ設定 UI | ⬜ | |
| 3-21 | プリセットスタンプ | ⬜ | |
| 3-22 | 注釈パネル UI（右サイドバー） | ⬜ | wireframe 参照 |
| 3-23 | カラーコード別アイコン | ⬜ | |
| 3-24 | クリックで注釈へジャンプ | ⬜ | |
| 3-25 | 注釈の削除 | ⬜ | |
| 3-26 | FDF エクスポート | ⬜ | |
| 3-27 | FDF インポート | ⬜ | |
| 3-28 | フローティング注釈ツールバー | ⬜ | wireframe 参照 |
| 3-29 | ツールバーから注釈種別を選択 | ⬜ | |

**Phase 3 完了チェックリスト:**
- [ ] ハイライト・コメント・手書きが追加できる
- [ ] 注釈付き PDF を保存し再度開いたとき注釈が残っている
- [ ] 右サイドバーの注釈パネルが機能する

**⚠️ Phase 3 実装時の注意点:**

```
PDFium の注釈 API:
  - 書き込み: FPDF_Annot_*  (FPDF_CreateNewAnnot, FPDF_SetAnnotColor など)
  - 読み込み: FPDFPage_GetAnnot, FPDFPage_GetAnnotCount
  - 注釈保存後は FPDF_SaveAsCopy で PDF 全体を書き出す
  
座標系の変換（最重要）:
  - PDFium 座標: 左下原点（y軸上向き）、単位はポイント(pt)
  - 画面座標: 左上原点（y軸下向き）、単位はピクセル
  - 変換式:
    pdfX = screenX / dpiScale
    pdfY = pageHeightPt - (screenY / dpiScale)
  
AnnotationOverlay の実装:
  - PdfPageControl の上に透明な Canvas を重ねる（Avalonia の Panel で重ねる）
  - 注釈ごとに対応する Avalonia コントロールをオーバーレイに配置
  - フリーハンドは SKCanvas で描画（PdfPageControl のレンダリングに組み込む）
  
未保存注釈の保護:
  - タブを閉じるとき IsModified が true なら保存確認ダイアログを表示
```

---

### Phase 4 — 高度機能

**ステータス:** ⬜ 未着手  
**最終更新日:** —  
**担当 Codex セッション:** —  
**前提:** Phase 3 の全タスク完了

| # | タスク | 状態 | 備考 |
|---|--------|------|------|
| 4-1 | フォームフィールドの検出と表示 | ⬜ | `FPDF_FormFillInfo` API |
| 4-2 | テキストフィールドの入力 | ⬜ | |
| 4-3 | チェックボックス / ラジオボタン | ⬜ | |
| 4-4 | ドロップダウン | ⬜ | |
| 4-5 | 署名フィールド | ⬜ | |
| 4-6 | 印刷ダイアログ (Ctrl+P) | ⬜ | OS 印刷 API 経由 |
| 4-7 | ページ範囲指定 | ⬜ | |
| 4-8 | 印刷プレビュー | ⬜ | |
| 4-9 | 用紙サイズ・向き・部数 | ⬜ | |
| 4-10 | ページ抽出 | ⬜ | `FPDF_CopyViewerPreferences` |
| 4-11 | ページ削除 | ⬜ | `FPDFPage_Delete` |
| 4-12 | ページ回転（保存まで反映） | ⬜ | `FPDFPage_SetRotation` |
| 4-13 | PDF 結合 | ⬜ | `FPDF_ImportPages` |
| 4-14 | ページ並び替え UI | ⬜ | サムネイルのドラッグ&ドロップ |
| 4-15 | 埋め込みファイルの一覧表示 | ⬜ | |
| 4-16 | 埋め込みファイルの抽出・保存 | ⬜ | |
| 4-17 | 全ショートカットの最終実装 | ⬜ | spec.md の一覧参照 |
| 4-18 | 暗号化 PDF のパスワード解除 | ⬜ | |
| 4-19 | パーミッション対応 | ⬜ | 印刷禁止・コピー禁止等 |

**Phase 4 完了チェックリスト:**
- [ ] フォームの入力と保存が動作する
- [ ] 印刷ダイアログから印刷できる
- [ ] PDF の結合・ページ抽出が動作する

**⚠️ Phase 4 実装時の注意点:**

```
印刷の実装方針:
  - Avalonia の標準印刷 API より PDFium の FPDF_RenderPageBitmap を使って
    高品質ビットマップを生成し OS の印刷 API に渡す方が品質が高い
  - Windows: PrintDocument (System.Drawing.Printing)
  - Linux: CUPS API または GDI 相当
  
フォーム API:
  - PDFium の FPDF_FORMHANDLE が必要
  - フォーム入力の処理は FORM_OnLButtonDown / FORM_OnChar 等のイベントを使う
  
ページ並び替え:
  - 実装が複雑なため Phase 5 に延期してもよい
```

---

### Phase 5 — 仕上げ・リリース

**ステータス:** ⬜ 未着手  
**最終更新日:** —  
**担当 Codex セッション:** —  
**前提:** Phase 4 の全タスク完了

| # | タスク | 状態 | 備考 |
|---|--------|------|------|
| 5-1 | 連続スクロールの仮想化 | ⬜ | 表示範囲外を破棄 |
| 5-2 | 大容量 PDF のストリーミング読み込み | ⬜ | |
| 5-3 | ページキャッシュの LRU 化 | ⬜ | Phase 1 の簡易実装を置き換え |
| 5-4 | 起動時間の計測と最適化 | ⬜ | 目標: 2 秒以内 |
| 5-5 | メモリ使用量の計測と最適化 | ⬜ | 目標: 150MB 以下 |
| 5-6 | ホバー・フォーカス・クリックアニメーション | ⬜ | |
| 5-7 | スプラッシュ画面（オプション） | ⬜ | |
| 5-8 | ドラッグ&ドロップの視覚フィードバック | ⬜ | |
| 5-9 | エラーダイアログのデザイン統一 | ⬜ | |
| 5-10 | ローディングインジケータ | ⬜ | |
| 5-11 | i18n 基盤の実装（resx） | ⬜ | |
| 5-12 | 日本語リソース完備 | ⬜ | |
| 5-13 | 英語リソース完備 | ⬜ | |
| 5-14 | キーボードフォーカス完全対応 | ⬜ | |
| 5-15 | スクリーンリーダー対応 | ⬜ | AutomationProperties |
| 5-16 | ハイコントラストモード | ⬜ | |
| 5-17 | カバレッジ確認（Core 80%+ 目標） | ⬜ | |
| 5-18 | 統合テスト | ⬜ | |
| 5-19 | Win / Linux スモークテスト | ⬜ | |
| 5-20 | Windows self-contained .exe | ⬜ | |
| 5-21 | Windows MSIX インストーラー | ⬜ | |
| 5-22 | Linux AppImage | ⬜ | |
| 5-23 | Linux .deb パッケージ | ⬜ | |
| 5-24 | GitHub Releases 自動化 | ⬜ | |

**Phase 5 完了チェックリスト:**
- [ ] 起動時間 2 秒以内を達成
- [ ] Windows / Linux 両方でインストーラーが動作する
- [ ] 全テストがパスしている

---

## Codex へのタスク指示テンプレート

各タスクを Codex に依頼するときは以下のテンプレートを使うこと。

```markdown
## タスク: [番号] [名前]

### 作るもの
[具体的に何を実装するか 1〜3 文で]

### 参照ファイル
#file:src/AcroPDF.Services/Interfaces/IPdfRenderService.cs
#file:src/AcroPDF.Core/Models/PdfDocument.cs

### 制約
- MVVM 厳守（View にロジックを書かない）
- async/await: I/O とレンダリングは必ず非同期
- nullable enable
- XML doc コメント: public メンバー全てに
- xUnit で単体テストを同時に作成すること

### 完了条件
- [ ] [条件1]
- [ ] テストがパスする

### 備考
[このタスク固有の注意点を copilot-instructions.md の Phase 備考から転記]
```

---

## 進捗の更新方法

タスクが完了したら、このファイルの該当行を以下のように更新すること。

```markdown
<!-- Before -->
| 1-1 | `IPdfRenderService` インターフェース実装 | ⬜ | |

<!-- After -->
| 1-1 | `IPdfRenderService` インターフェース実装 | ✅ | 2026-03-15 完了。`src/AcroPDF.Services/Interfaces/IPdfRenderService.cs` |
```

Phase 全体が完了したら、**ステータス**を `✅ 完了`、**最終更新日**を記入すること。

---

*このファイルは GitHub Copilot Agent が自動参照します。手動で編集する際も書式を崩さないでください。*
