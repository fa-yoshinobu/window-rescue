# 開発・ビルド

## 必要環境

- Windows 11
- .NET 10 SDK

## ビルド・テスト・実行

```powershell
dotnet build WindowRescue.slnx
dotnet test WindowRescue.slnx
src\WindowRescue\bin\Debug\net10.0-windows\WindowRescue.exe
```

テストには、テスト自身が作った窓だけを画面外に置いて救出を確かめる結合テストが含まれます（利用者の窓には触れません）。常駐中の Window Rescue がテスト用の窓を自動補正してしまうため、結合テストはアプリを終了してから実行します（起動中はその旨を表示して失敗します）。

## 1 ファイル exe の作成

`build.bat` を実行すると `dist\WindowRescue.exe`（.NET ランタイム同梱）を作ります。アプリが起動中だと上書きできないため、トレイの「終了」で閉じてから実行します。

コマンドで行う場合：

```powershell
dotnet publish src/WindowRescue/WindowRescue.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o dist
```

## バージョン

製品名・バージョン・開発元・著作権はルートの `Directory.Build.props` で管理します。exe のプロパティと、アプリの「バージョン情報」（トレイメニュー・設定画面から表示）に反映されます。

## リリース

1. `Directory.Build.props` の `<Version>` と `CHANGELOG.md` を更新してコミットする
2. 同じ番号のタグを push する

`v` で始まるタグを push すると、GitHub Actions（`.github/workflows/release.yml`）がタグと `Directory.Build.props` のバージョンの一致を確かめ、テストして 1 ファイル exe を作り、GitHub リリースに添付します。画面を操作する結合テストとプロセス間通信のテストは CI では実行しません。

```powershell
git tag v0.2.0
git push origin v0.2.0
```

## 構成

| パス | 内容 |
| --- | --- |
| `src/WindowRescue/Core` | 画面情報・窓の列挙・位置計算（`PlacementCalculator` / `WindowJudge`）・救出（`RescueEngine`）・新規窓の監視・ショートカット・右クリックメニュー登録・多重起動防止 |
| `src/WindowRescue/UI` | ミニウインドウ・救出パネル・ウインドウ一覧・設定・通知・トレイ、DADS 準拠の共通スタイル（`Theme.xaml`） |
| `src/WindowRescue/Resources` | 文字列リソース（英語が基底、`Strings.ja.resx` が日本語） |
| `tests/WindowRescue.Tests` | 単体テスト・結合テスト |
| `tools/make-icon.ps1` | アイコン（.ico と PNG）の生成 |
| `tools/gen_resx.py` | 文字列表から両言語の .resx を生成（`python tools/gen_resx.py src/WindowRescue/Resources`） |

## デザイン

- 設定・操作画面はデジタル庁デザインシステム（DADS）を基準にしています。色は `@digital-go-jp/design-tokens` 2.1.0 の値です。
- アイコンは、青い角丸（画面）の右上へはみ出した点線の窓を、黄色の矢印で手元の白い窓へ引き戻す図柄です。色は DADS の Blue-900（#0017C1）とフォーカス色の Yellow-300（#FFD43D）。16px でも判別できるよう、小さいサイズでは点線を省いています。

## 未対応・要確認

[TODO.md](../TODO.md) を参照。
