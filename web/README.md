# ykr.moe 設置用ファイル

`https://ykr.moe/apps/NicoKaraPrep/` に置くための紹介ページとマニュアルです。
既存サイト（`assets/style.css`）のデザイン・ヘッダー・フッターに合わせてあります。

## 設置するもの

サイトのルートを基準に、次のようにアップロードします。

```
apps/NicoKaraPrep/index.html      紹介ページ（https://ykr.moe/apps/NicoKaraPrep/）
apps/NicoKaraPrep/manual.html     使い方マニュアル
apps/NicoKaraPrep/privacy.html    プライバシーポリシー（既存ページをサイトのデザインに移植したもの）
apps/NicoKaraPrep/images/         画像（アプリアイコン・スクリーンショット）
index.html                        トップページ（提供アプリ一覧に「にこぷれっぷ」を追加したもの）
```

`privacy.html` は公開中のページを、サイト共通のヘッダー・フッター・スタイルへ移植したものです。上書きアップロードすると、他のページと同じ見た目になり、ヘッダーから紹介ページ・使い方へ戻れるようになります。0.2.0 に合わせて、2026-10-08 に「3. AI アシスタントとの連携（MCP）」の節と、実行エイリアスの説明（6 章）・英語の要約を足しました（「外部サービスとの連携も行いません」の一文は MCP と食い違うので外しました）。ストア版の審査でプライバシーポリシーの URL を参照されることがあるので、0.2.0 を提出する前にアップロードします。

アプリ名は対外的には「にこぷれっぷ」で統一しています。実行ファイル名・設定フォルダ名・GitHub リポジトリ名・このページの URL は `NicoKaraPrep` のままです。

- `web/assets/` の中身（`style.css`・`yukanavi-icon.png`）は**ローカル表示確認用のコピー**です。サイト側に同じものが既にあるので、アップロードは不要です。
- `images/nicokaraprep-icon.png` はアプリの `app.ico` から取り出した 256px の PNG です。

## ローカルで表示を確認する

```bash
cd web && python -m http.server 8791
```

`http://127.0.0.1:8791/apps/NicoKaraPrep/index.html` を開きます。

## トップページへの追加

`web/index.html` が、**公開中のトップページに「にこぷれっぷ」の項目を足しただけ**のファイルです（2026-08-31 時点の内容から作成。差分は下記の 1 ブロックのみ）。そのままサイトのルートへ上書きアップロードできます。

アップロード前に公開中のトップページが更新されていた場合は、上書きせず次のブロックを `#apps` セクションのゆかナビの `<article class="app-entry">` の下に挿入してください。

```html
<article class="app-entry">
  <img src="apps/NicoKaraPrep/images/nicokaraprep-icon.png" width="256" height="256" alt="にこぷれっぷのアプリアイコン">
  <div class="app-entry-content">
    <p class="app-category">ニコカラ制作支援アプリ</p>
    <h3>にこぷれっぷ</h3>
    <p>RhythmicaLyrics で作ったタイムタグ付き歌詞を、ニコカラメーカー3 向けに仕上げる Windows アプリです。</p>
    <p class="app-release-status">Windows版 配布中</p>
    <a class="primary-link" href="apps/NicoKaraPrep/index.html">にこぷれっぷを見る</a>
  </div>
</article>
```

アイコン画像は `apps/NicoKaraPrep/images/nicokaraprep-icon.png` を参照しているので、トップページ用に画像を追加する必要はありません。

## 入手先リンク

**Microsoft Store をメインの入手先**として案内しています。zip（GitHub Releases）は「インストールせずに使いたい場合」の副の導線です。

| URL | 参照している場所 |
|---|---|
| `https://apps.microsoft.com/detail/9nb4v45g33bp` | `index.html` のダウンロードボタンとアプリ情報、`manual.html` 1 章 |
| `https://github.com/bee7813993/NicoKaraPrep/releases/latest` | `index.html` のダウンロード欄とアプリ情報、`manual.html` 1 章 |
| `https://github.com/bee7813993/NicoKaraPrep` | `index.html` のアプリ情報 |

入手先を変える場合は、上記の箇所を書き換えます。「Microsoft Store で配信中」の表記はトップページの提供アプリ欄・紹介ページのステータスにも入っています。

## スクリーンショット

`Store素材\0.2.0` フォルダ（ストア提出用と同じ写真、2026-10-08 撮影）の画像を `images/` に配置済みです。使う場所の章番号は manual.html のものです。

| ファイル名 | 元ファイル（Store素材\0.2.0） | 使う場所 |
|---|---|---|
| `shot-main.png` | 行リスト＋字幕のプレビュー.png | index.html「画面」／manual.html |
| `shot-font-settings.png` | フォント設定ビュー.png | index.html「画面」／manual.html |
| `shot-n3proj-import.png` | n3proj の読み込みの確認画面.png | index.html「画面」／manual.html |
| `shot-show-time.png` | 表示時刻の自動調整.png | index.html「画面」／manual.html |
| `shot-insert.png` | 絵文字挿入ビュー（F2）と定型文.png | index.html「画面」／manual.html |
| `shot-check-list.png` | チェック結果の一覧.png | index.html「画面」／manual.html |
| `shot-layout.png` | レイアウト設定ビュー.png | index.html「画面」／manual.html |
| `shot-subtitle-action.png` | 字幕アクション.png | index.html「画面」／manual.html |
| `shot-emoji-list.png` | 絵文字リスト編集.png | index.html「画面」／manual.html |
| `shot-player-top.png` | メディア再生を上の列に置いた配置.png | index.html「画面」／manual.html |

0.1.0 の写真の `shot-check-insert.png` は使わなくなったので消しました（サイトに残っていれば消してかまいません）。差し替えるときは同じファイル名で上書きすれば、HTML の変更は不要です（大きさが変わるときは img の width・height も直します）。
