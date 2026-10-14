<p align="center"><img src="docs/icon.png" width="96" alt="StickerBoard アイコン"></p>

<h1 align="center">StickerBoard</h1>

<p align="center">
Windows 向けの軽量デスクトップボード — 画像を貼り、メモを書き、落書きして、<br>
付箋のように壁紙の上に置いておけます。
</p>

<p align="center">
<a href="README.md">English</a> · <a href="README.ko.md">한국어</a> · <b>日本語</b> · <a href="README.zh-Hans.md">简体中文</a> · <a href="README.zh-Hant.md">繁體中文</a>
</p>

<p align="center"><img src="docs/screenshot.png" alt="StickerBoard スクリーンショット"></p>

## 機能

- **画像** — `Ctrl+V` で貼り付け（スクリーンショット、ブラウザーの画像、コピーしたファイル）、またはファイルをドラッグ＆ドロップ。角のハンドルかマウスホイールでサイズ変更。
- **メモ** — 空き場所をダブルクリック。太字・斜体・下線・取り消し線、文字の色、蛍光ペン、部分的な文字サイズ、フォント、箇条書き、**チェックリスト**。
- **文字だけのメモ** — 付箋なしで文字だけ。柔らかい縁取りでどんな壁紙の上でも読みやすく表示。
- **ペン** — 何の上にでも描画・蛍光ペン、元に戻すにも対応。
- **デスクトップに常駐** — 背景の不透明度スライダー（完全な透明も可）、常に手前 / 通常 / 他のウィンドウの後ろ、位置とサイズの固定、最大化、内容に合わせたサイズ調整。
- **軽量** — 約 0.6 MB の実行ファイル、待機中の CPU 使用率 0%、すべて自動保存。
- **言語** — English、한국어、日本語、简体中文、繁體中文（Windows の言語に合わせて自動選択、右クリックメニューで変更可能）。

## ダウンロード

最新版は **[Releases](https://github.com/Eastar0102/StickerBoard/releases/latest)** から入手できます（Windows 10/11、x64）:

| ファイル | サイズ | 備考 |
| --- | --- | --- |
| `StickerBoard-<version>-win-x64.exe` | 約 0.6 MB | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) が必要 |
| `StickerBoard-<version>-win-x64-standalone.exe` | 大きめ | インストール不要でそのまま実行 |

インストーラーはありません。exe を実行するだけです。コード署名をしていないため、初回起動時に Windows SmartScreen の警告が出ることがあります（**詳細情報 → 実行**）。

## 使い方

| 操作 | 方法 |
| --- | --- |
| 画像 / テキストの貼り付け | `Ctrl+V`（テキストはメモになります） |
| 新しいメモ | 空き場所をダブルクリック、または **+** ボタン |
| 移動 | 画像はそのまま、メモは上部の帯をドラッグ |
| サイズ変更 | 角のハンドル（`Shift` で縦横比を固定しない）または画像の上でマウスホイール |
| 削除 / 削除を元に戻す | `Del` / `Ctrl+Z` |
| メモの書式 | `Ctrl+B` `Ctrl+I` `Ctrl+U`、取り消し線 `Ctrl+Shift+X`、箇条書き `Ctrl+Shift+L` |
| チェックリスト | `Ctrl+Enter` でチェックボックスを追加 / チェック、☐ をクリックでもチェック |
| メモの文字サイズ | `Ctrl+ホイール` |
| ペン | `P`（`H` 蛍光ペン、`E` 消しゴム、`Ctrl+Z` 元に戻す、`Esc` 終了） |
| 最大化 | `F11` またはタイトルバーをダブルクリック |
| 設定 | ボードを右クリック |

## データ

すべて `%APPDATA%\StickerBoard` に保存されます（`board.json`、`ink.isf`、`images\`）。
環境変数 `STICKERBOARD_DATA` で別のフォルダーを指定できます。テストや、もう 1 つ別のボードを起動したいときに便利です。

## ビルド

Windows に [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) が必要です。

```powershell
dotnet run

# 小さな単一ファイル exe（.NET 8 Desktop Runtime が必要）
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

# 単体で動く単一ファイル exe
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish-standalone
```

アイコンは `tools/make-icon.ps1` で生成しています。

## 翻訳

UI の文字列は [`Strings/`](Strings) フォルダーの JSON ファイルにあります（`en.json` が基準で、足りないキーは英語で表示されます）。
新しい言語を追加するには、`en.json` を `Strings/<コード>.json` にコピーして翻訳し、[`Localization.cs`](Localization.cs) の `L.Languages` にコードを追加して Pull Request を送ってください。既存の翻訳の修正も歓迎します。

## ライセンス

[MIT](LICENSE)
