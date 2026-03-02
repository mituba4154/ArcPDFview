# GitHub Release で Windows `.exe` を公開する手順

ArcPDFview では、タグ push (`v*.*.*`) をトリガーに `.exe` / `.msi` / `.zip` を作成し、GitHub Release へ自動公開するワークフローが既に用意されています。

- Workflow: `.github/workflows/release.yml`
- トリガー: `push.tags: v*.*.*`
- Windows 成果物: `ArcPDFview-win-x64.exe`, `ArcPDFview-win-x64.msi`, `ArcPDFview-win-x64.zip`

## 事前準備

1. デフォルトブランチ（通常 `main`）に公開したい内容をマージする。
2. ローカルで最低限のビルド確認を行う。

```bash
dotnet restore ArcPDFview.sln
dotnet build ArcPDFview.sln -c Release
```

## リリース作成（推奨: タグ駆動）

以下を実行すると、GitHub Actions が自動で Release を作成して成果物を添付します。

```bash
git checkout main
git pull

git tag v0.6.0
git push origin v0.6.0
```

> タグ名は workflow の条件に合わせて **`v` から始まるセマンティックバージョン**にしてください（例: `v1.2.3`）。

## 実行後の確認

1. GitHub の **Actions** で `Release Packages` ワークフローが成功していることを確認。
2. GitHub の **Releases** で該当タグのリリースを開く。
3. Assets に次があることを確認。
   - `ArcPDFview-win-x64.exe`
   - `ArcPDFview-win-x64.msi`
   - `ArcPDFview-win-x64.zip`

## よくあるハマりどころ

- タグ形式が `v*.*.*` に一致しない（`0.6.0` など）
  - `v0.6.0` のように `v` を付与する。
- リリースが更新されない
  - 同名タグの再 push では意図通りにならない場合があるため、タグを作り直す。
- 既存 Release の文面を調整したい
  - ワークフロー内の `Generate release notes` を編集するか、Release 画面で手動編集する。

