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

`privacy.html` は公開中のページを**本文はそのままに**、サイト共通のヘッダー・フッター・スタイルへ移植したものです（本文の差分がないことは確認済み）。上書きアップロードすると、他のページと同じ見た目になり、ヘッダーから紹介ページ・使い方へ戻れるようになります。

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

`Store素材` フォルダの画像を `images/` に配置済みです（両ページとも表示状態）。

| ファイル名 | 元ファイル | 使う場所 |
|---|---|---|
| `shot-main.png` | `shot-main.png` | index.html「画面」／manual.html 3 章 |
| `shot-insert.png` | `shot-insert.png` | index.html「画面」／manual.html 7 章 |
| `shot-check-list.png` | `shot-check_lineedit.png` | index.html「画面」／manual.html 9 章 |
| `shot-check-insert.png` | `shot-check_emojiinseart.png` | index.html「画面」／manual.html 9 章 |
| `shot-emoji-list.png` | `shot-emoji-list.png`（ダイアログ部分だけを切り出し） | index.html「画面」／manual.html 6 章 |

差し替えるときは同じファイル名で上書きすれば、HTML の変更は不要です。
