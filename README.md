# ArcPDFview (AcroPDF)

Windows / Linux 向けの軽量 PDF ビューワーです。  
現在は Phase 0（基盤構築）として、.NET 8 + Avalonia 11 の最小起動構成を実装しています。

## 開発環境

- .NET SDK 8
- Avalonia UI 11

## セットアップ

```bash
dotnet restore ArcPDFview.sln
dotnet build ArcPDFview.sln
dotnet test ArcPDFview.sln
```

## アプリ起動

```bash
dotnet run --project src/AcroPDF.App/AcroPDF.App.csproj
```

## Phase 0 で追加した主要 NuGet

- `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`
- `CommunityToolkit.Mvvm`
- `bblanchon.PDFium`（Apache-2.0）
- `SkiaSharp`
