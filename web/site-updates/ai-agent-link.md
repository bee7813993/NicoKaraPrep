# サイトの更新案: MCP を Codex などほかの AI エージェントからも（AI エージェントと連携）

- **状態**: 未反映
- **元にした変更**: ファイル > Claude と連携 を「AI エージェントと連携」に広げ、Codex への登録のボタンと、ほかの AI エージェント（Cursor・Gemini CLI・VS Code など）向けの設定のコピーを足した変更（ブランチ claude/mcp-multi-agent-support。PR はまだ）
- **反映する時期**: 0.2.0 の次の版（この変更を含む版）を Microsoft Store で配信してから。0.2.0 のメニューの名前は「Claude と連携」のままなので、それより前には反映しません
- **対象**: ykrmoe_web の `html/apps/NicoKaraPrep/` の `manual.html`・`privacy.html`・`index.html`
- **パッチ**: [ai-agent-link.patch](ai-agent-link.patch)（ykrmoe_web の main `dc8e592` に当たることを `git apply --check` で確かめました。[pr14-manual.patch](pr14-manual.patch)・[pr15-manual.patch](pr15-manual.patch) とは別の箇所で、先に当てても後に当てても、どちらも当たることを確かめました）

## 当て方

ykrmoe_web のリポジトリのルートで当てます。

```
git apply <このリポジトリ>/web/site-updates/ai-agent-link.patch
```

**privacy.html の最終更新日（「最終更新日: 2026年10月8日」）は、パッチでは変えていません。反映する日の日付に直してください**（本ポリシーの 8 章で、変えたときは最終更新日を改めると書いているため）。

当たらないとき（サイト側で直したあとなど）は、下の「変更点」を手で直してください。

## 変更点

アプリのマニュアル（`マニュアル.md` の 18 章など）に合わせたものです。メニューの名前は「ファイル > AI エージェントと連携...」になりました。

### manual.html

1. `<meta name="description">` の「Claude からの操作（MCP）」を「Claude・Codex などの AI エージェントからの操作（MCP）」にします。
2. 目次と 18 章の見出しの「18. MCP（Claude などからの操作）」を「18. MCP（AI エージェントからの操作）」にします。
3. 1 章の「Claude などの AI から MCP で操作することもできます」を「Claude・Codex などの AI エージェントから…」にします。
4. 設定の表の「MCP サーバーを有効にする」の説明を「Claude・Codex などの AI エージェントから…。下の「AI エージェントと連携...」で…」にします。
5. 困ったとき: 「Claude から操作できない」「Claude からの書き込みが…断られる」を「AI エージェントから…」に、「ファイル > Claude と連携...」を「ファイル > AI エージェントと連携...」にし、「「Codex に登録」が「Codex が見つかりません」と答える」の項目を足します（Codex アプリだけのときは、表示された 3 行を `%USERPROFILE%\.codex\config.toml` の終わりに書き足して Codex を起動し直す）。
6. 18 章:
   - 冒頭の文を「Claude・Codex などの AI エージェントから…」にします。
   - 18.1 の手順 2 を「ファイル > AI エージェントと連携... を開き、使っているアプリの行のボタンを押します（ボタンの左に、今の登録の状態）」にし、「Codex で使う（Codex アプリ・Codex CLI・IDE の拡張機能）→ 「Codex に登録」」の項目を足します（設定を共有しているので 1 回の登録でどれからも使える）。
   - 手順 3 を「AI エージェントの会話から…。Claude Code・Codex は、登録したあとに始めた会話から使えます（Codex で出てこないときは、Codex を起動し直します）」にします。
   - 設定の画面から開く項目の「Claude と連携...」を「AI エージェントと連携...」にします。
   - 「ほかの AI エージェント（Cursor・Gemini CLI・VS Code（GitHub Copilot）など）」の項目を足します（画面の「ほかの AI エージェントで使う」に出るコマンドを引数 `--mcp` の MCP サーバーとして登録する、`mcpServers` の形をコピーして貼る、VS Code は `servers`）。
   - 「うまくいかないとき」の囲みを「AI エージェントと連携」の画面のことにし、Claude Code・Codex はコマンドを実行する、Codex アプリだけなら 3 行を設定ファイルに書き足す、にします。
   - 18.3 の「Claude を起動し直す必要はありません」を「AI エージェントを起動し直す必要はありません」にします。

### privacy.html（3 章「AI アシスタントとの連携（MCP）」と英語の要約）

1. 「Claude などの AI アシスタントのアプリ」を「Claude・Codex などの AI アシスタントのアプリ」にします。
2. 「ファイル > Claude と連携」を「ファイル > AI エージェントと連携」にします。
3. 登録のときにパソコンの中で行うことに **Codex の設定への登録** を足します（「Claude Code・Codex の設定への登録と、Claude Desktop の拡張機能のファイルの作成」）。アプリは、Codex の設定ファイル（`~/.codex/config.toml`）に、Codex のコマンド（`codex mcp add`）で登録します。開発者や第三者へ送る情報が無いことは変わりません。
4. 「登録は Claude 側でいつでも解除できます」を「登録は AI アシスタントのアプリ側でいつでも解除できます」にします。
5. 英語の要約の「AI assistant apps such as Claude」を「such as Claude and Codex」に、「(File > Link with Claude)」を「(File > Link with AI agents)」にします。

### index.html（紹介ページ）

1. 特徴 09 の見出し「Claude から操作（MCP）」を「AI エージェントから操作（MCP）」にし、本文を「ファイル > AI エージェントと連携 のボタンひとつで、Claude Desktop・Claude Code・Codex から…」にします。
2. 「一緒に使うもの」の「Claude Desktop・Claude Code から操作することもできます（任意）」を「Claude Desktop・Claude Code・Codex などの AI エージェントから操作することもできます（任意）」にします。

## 反映したあとに確かめること

- 公開中の 3 ページに「Claude と連携」「Link with Claude」の文字が残っていないこと
- manual.html の 18.1 に「Codex で使う」「ほかの AI エージェント」の項目があり、箇条書きの入れ子が崩れていないこと。困ったときに「Codex が見つかりません」の項目があること
- privacy.html の最終更新日が反映した日になっていること
- index.html の特徴 09 の見出しが「AI エージェントから操作（MCP）」になっていること
