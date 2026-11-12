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

mainへのpushとPull RequestではWindows上のCIがReleaseビルドと単体テストを実行します。警告はビルドエラーとして扱います。リリースも同じ `tests/ci.runsettings` を使用し、`Category=DesktopIntegration` のテスト（実窓・プロセス間通信）だけ除外します。ローカルでCIと同じ確認をする場合：

```powershell
dotnet build WindowRescue.slnx -c Release -p:TreatWarningsAsErrors=true
dotnet test WindowRescue.slnx -c Release --no-build --settings tests/ci.runsettings
```

## 1 ファイル exe の作成

`build.bat` を実行すると `dist\WindowRescue.exe`（.NET ランタイム同梱）と `dist\WindowRescue-win-x64.zip` を作ります。UTF-8のバッチで、起動時にコードページ65001へ切り替えます。アプリが起動中だと上書きできないため、トレイの「終了」で閉じてから実行します。

コマンドで行う場合：

```powershell
dotnet publish src/WindowRescue/WindowRescue.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o dist
```

## バージョン

製品名・バージョン・開発元・著作権はルートの `Directory.Build.props` で管理します。exe のプロパティと、アプリの「バージョン情報」（トレイメニュー・設定画面から表示）に反映されます。

### 同梱ランタイムの更新

`WindowRescue.csproj` の `RuntimeFrameworkVersion` は固定を続けます。自己完結型の配布物に入る版を明示し、開発機とCIのSDKの違いで古い版へ戻ったり、未検証の版へ変わったりすることを防ぐためです。

`Check bundled runtime` ワークフローが毎週水曜02:23 UTC、手動実行時、ランタイム設定・検知スクリプトを変更するPRで、[Microsoftの公開メタデータ](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)と比較します。古い版・サポート終了・取得失敗はジョブ失敗として表示されます。GitHubのActions通知を有効にして失敗通知を受け取ってください。この通信は保守用CIだけで行い、アプリには更新確認通信を追加しません。定期実行はワークフローが既定ブランチに入った後に有効になります。導入前のブランチではPRのチェックを利用でき、一度実行されたワークフローはGitHub CLI/APIでブランチを指定して手動実行することもできます。

ランタイムだけを更新する場合：

1. 公式メタデータの同じmajor.minorの最新安定版へ `RuntimeFrameworkVersion` を変更する。
2. `python .github/scripts/check_runtime.py`、共通ビルド・テストを実行する。
3. 自己完結型で発行し、`obj/project.assets.json` の `Microsoft.NETCore.App.Runtime.win-x64` と `Microsoft.WindowsDesktop.App.Runtime.win-x64` が指定版であること、発行したexeの起動・救出を確認する。
4. アプリのパッチ番号を上げ、`CHANGELOG.md` にランタイムの旧版・新版を記載する。通常のリリース手順でタグと配布物を作成する。既存の配布済みexeには自動で反映されない。

検知の検証は作業ブランチの固定値を1パッチ古くしてPRのチェックを実行し、失敗を確認してから固定値を戻します。PRで起動済みなら、`gh workflow run runtime-update.yml --ref <作業ブランチ>` でも確認できます。Actions画面の手動実行ボタンは既定ブランチへの導入後に表示されます。ローカルでも同じスクリプトが終了コード1を返します。

## リリース

1. [リリース実機確認表](release-checklist.md)をリリースごとに実施する。
2. `Directory.Build.props` の `<Version>` と `CHANGELOG.md` を更新し、該当変更を `.github/release-notes.md` の英語のChanges欄へ反映してコミットする。
3. 同じ番号のタグを push する。

`v` で始まるタグを push すると、GitHub Actions（`.github/workflows/release.yml`）がタグと `Directory.Build.props` のバージョンの一致を確かめ、テストして 1 ファイル exe を作り、GitHub リリースに添付します。画面を操作する結合テストとプロセス間通信のテストは CI では実行しません。

```powershell
git tag v0.2.0
git push origin v0.2.0
```

配布は固定名のZIPとSHA-256を使い、過去のリリースの添付物は変更しません。リリースノートとVirusTotalの結果メッセージは英語で記載します。ワークフローは公開済みZIP内のexeを実際にスキャンします。未完了の解析は `Scan release with VirusTotal` にリリースタグを指定して再試行できます。詳細は[共通リリース基準](https://github.com/fa-yoshinobu/release_standard)を参照してください。

## 構成

設定形式の移行は `SettingsStore.MigrateAndValidate` にまとめます。現在の公開形式はVersion 1だけです。形式を変更する場合は旧版ごとの移行をここへ追加し、移行後に設定画面と同じ数値範囲で検証します。未知の新しいVersionは読み替えず、元ファイルを保存禁止にして今回だけ既定値で起動します。起動時と設定画面でその旨を案内します。既知形式の破損は従来どおり `.broken` へ退避します。

画面情報は `DisplayManager.Connected` に保持します。OSからの取得は起動時、画面構成・作業領域の変更時、ウインドウ一覧の「更新」を押したときだけ行います。ミニウインドウの移動や表示更新では取得し直しません。形式は `.editorconfig`、改行は `.gitattributes` で指定し、バッチのみCRLF、他のテキストはLF・UTF-8を使います。

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
