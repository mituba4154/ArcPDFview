# ArcPDFview (AcroPDF)

Windows / Linux 向けの軽量? PDF ビューワーです。サブスクリプション不要・広告なしで利用できます。

A lightweight PDF viewer for Windows and Linux.

v0.9.0
---

## 主な機能 / Features

### PDF 表示 / PDF Viewing

- 高品質レンダリング（PDFium エンジン） / High-quality rendering powered by PDFium
- ズーム 25%〜400%（Ctrl+スクロール対応） / Zoom 25%–400% (Ctrl+Scroll supported)
- 表示モード: 単ページ・連続スクロール・2 ページ見開き / View modes: single page, continuous scroll, two-page spread
- タブによる複数ファイルの同時表示 / Tab-based multi-document viewing
- 分割ビュー（左右独立操作） / Split view with independent pane control
- ページ回転・フルスクリーン (F11) / Page rotation and fullscreen (F11)

### テキスト操作 / Text

- テキスト選択・コピー (Ctrl+C) / Text selection and copy (Ctrl+C)
- テキスト検索 (Ctrl+F)（大文字小文字区別・正規表現対応） / Text search (Ctrl+F) with case-sensitive and regex options
- 検索結果ハイライトとナビゲーション (F3 / Shift+F3) / Search result highlighting and navigation (F3 / Shift+F3)

### 注釈 / Annotations

- ハイライト（4 色）・下線・取り消し線 / Highlight (4 colors), underline, strikethrough
- コメント付箋 / Comment sticky notes
- フリーハンド描画 / Freehand drawing
- 図形（矩形・楕円・矢印・線） / Shapes (rectangle, ellipse, arrow, line)
- プリセットスタンプ / Preset stamps
- 注釈の PDF 埋め込み保存・FDF エクスポート/インポート / Save annotations to PDF, FDF export/import

### フォーム / Forms

- テキストフィールド・チェックボックス・ラジオボタン・ドロップダウン・署名フィールド / Text fields, checkboxes, radio buttons, dropdowns, signature fields

### ページ編集 / Page Operations

- ページ抽出・削除・回転・並び替え / Extract, delete, rotate, reorder pages
- PDF 結合 / Merge PDFs

### 印刷 / Printing

- 印刷ダイアログ (Ctrl+P) / Print dialog (Ctrl+P)
- ページ範囲指定・用紙サイズ・向き・部数 / Page range, paper size, orientation, copies

### サイドバー / Sidebars

- サムネイルパネル / Thumbnail panel
- 目次（ブックマーク）ツリー / Table of contents (bookmark) tree
- 注釈一覧パネル / Annotation list panel
- 埋め込みファイル一覧 / Embedded files list

### その他 / Other

- パスワード保護 PDF の対応 / Password-protected PDF support
- ドラッグ＆ドロップでファイルを開く / Open files via drag & drop
- セッション復元（前回開いていたファイル・ページ） / Session restore (previously opened files and pages)
- 最近のファイル履歴 / Recent files history
- 外部ファイル変更の検知・再読み込み / External file change detection and reload prompt
- 多言語 UI（日本語・英語） / Multilingual UI (Japanese and English)
- キーボード完全操作・スクリーンリーダー対応・ハイコントラストモード / Full keyboard navigation, screen reader support, high contrast mode
- 豊富なキーボードショートカット / Extensive keyboard shortcuts
-初回起動時のみ重いです
---

## 技術スタック / Tech Stack

| 役割 / Role | 技術 / Technology |
|---|---|
| 言語 / Language | C# 12 / .NET 8 |
| UI フレームワーク / UI Framework | Avalonia UI 11 |
| PDF エンジン / PDF Engine | PDFium (`bblanchon.PDFium`) |
| 2D 描画 / 2D Rendering | SkiaSharp |
| MVVM | CommunityToolkit.Mvvm |
| DI | Microsoft.Extensions.DependencyInjection |
| テスト / Testing | xUnit + Moq |
| CI/CD | GitHub Actions |

---

## プロジェクト構造 / Project Structure

```
ArcPDFview.sln
├── src/
│   ├── AcroPDF.Core/          … モデル / Models
│   ├── AcroPDF.Services/      … ビジネスロジック / Business logic & service wrappers
│   ├── AcroPDF.ViewModels/    … ViewModel（Avalonia 非依存） / ViewModels (no Avalonia dependency)
│   └── AcroPDF.App/           … View・コントロール・エントリポイント / Views, controls & app entry point
└── tests/
    ├── AcroPDF.Core.Tests/
    ├── AcroPDF.Services.Tests/
    └── AcroPDF.ViewModels.Tests/
```

---

## 必要環境 / Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## ビルド・テスト / Build & Test

```bash
dotnet restore ArcPDFview.sln
dotnet build ArcPDFview.sln
dotnet test ArcPDFview.sln
```

## 起動 / Run

```bash
dotnet run --project src/AcroPDF.App/AcroPDF.App.csproj
```

---

## 対応 OS / Supported Platforms

- Windows 10 / 11 (x64)
- Linux — Ubuntu 22.04+, Debian 12+ (x64)

## 配布形式 / Distribution

| プラットフォーム / Platform | 形式 / Format |
|---|---|
| Windows | Self-contained `.exe` / MSIX |
| Linux | `.deb` / AppImage |

---


## リリース手順 / Release Guide

GitHub Release で `.exe` を公開する具体手順は `docs/release.md` を参照してください。

## キーボードショートカット / Keyboard Shortcuts

| ショートカット / Shortcut | 機能 / Action |
|---|---|
| Ctrl+O | ファイルを開く / Open file |
| Ctrl+W | タブを閉じる / Close tab |
| Ctrl+S | 保存 / Save |
| Ctrl+P | 印刷 / Print |
| Ctrl+F | 検索 / Search |
| Ctrl+G | ページへジャンプ / Go to page |
| Ctrl+= / Ctrl++ | ズームイン / Zoom in |
| Ctrl+- | ズームアウト / Zoom out |
| Ctrl+0 | 100% に戻す / Reset to 100% |
| ← / → | 前/次のページ / Previous/Next page |
| Home / End | 先頭/最終ページ / First/Last page |
| F11 | フルスクリーン / Fullscreen |
| F3 / Shift+F3 | 次/前の検索結果 / Next/Previous search result |
| Ctrl+Tab | 次のタブ / Next tab |
| Escape | 検索バーを閉じる・ツール解除 / Close search bar / Deselect tool |

---

## ライセンス / License

[Apache License 2.0](LICENSE)
