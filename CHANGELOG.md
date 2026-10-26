# Changelog

この形式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に基づきます。

## [Unreleased]

### Changed

- Standardized fixed-name Windows ZIP assets, ZIP SHA-256 checksums and English release notes.

### Fixed

- Unified VirusTotal completion checks and preserved unrelated release notes during rescans.

## [0.2.0] - 2026-10-24

初回リリース。

### Added

- ミニウインドウ：置いた画面を「この画面」として「新しい窓をこの画面に出す」「小さい窓を補正する」「窓をこの画面に集める」を操作。ミニウインドウを動かすと表示先も追従し、「小さい窓を補正する」はオンにした時点で開いている極小の窓も補正
- 全ウインドウの一括救出・選択中の窓の救出（最小化・最大化の状態を維持、画面ごとの拡大率に対応）
- ウインドウ一覧：画面外・極小・操作できない窓を文字で探し、選んだ窓を救出
- 新しい窓の監視：監視しない／自動救出（画面外・はみ出し・極小だけ補正）／表示先を固定。アプリ別の除外
- トレイメニュー（この画面へ全ウインドウを救出、新しい窓をこの画面に表示、ミニウインドウを表示など）
- ショートカット（割り当ての変更、使えないキーの表示）
- デスクトップ右クリックメニューの登録・解除（現在のユーザーのみ、管理者権限不要）
- 日本語／English の切り替え（再起動不要）
- デジタル庁デザインシステム（DADS）を基準にした画面、アイコン
- バージョン情報（製品情報・使用ライブラリ・ライセンス）
- MIT License

[0.2.0]: https://github.com/fa-yoshinobu/window-rescue/releases/tag/v0.2.0
