# リポジトリ運用ガイドライン

## プロジェクト構成・モジュール構成
- ルート: ソリューション `Man10BankService.sln`。
- アプリ: `Man10BankService/`（ASP.NET Core Minimal API、.NET 9）。
  - エントリーポイント: `Program.cs`
  - 設定: `appsettings.json`, `appsettings.Development.json`
  - HTTP サンプル: `Man10BankService.http`
  - ビルド成果物: `bin/`, `obj/`（Git で無視）

## ビルド・テスト・開発コマンド
- Build: `dotnet build` — パッケージを復元し、ソリューションをコンパイルする。
- Run (dev): `dotnet run --project Man10BankService` — Kestrel を起動。OpenAPI ドキュメントは Development のみ `/openapi/v1.json` で提供。
- Watch: `dotnet watch --project Man10BankService run` — ファイル変更でホットリロード。
- Restore: `dotnet restore` — NuGet 依存関係を取得。

## コーディングスタイル・命名規則
- 言語: C#（`nullable` と `implicit usings` を有効化）。
- インデント: 4 スペース、UTF-8、LF 改行。
- 命名: 型/メソッドは PascalCase、ローカル変数/引数は camelCase、非同期メソッドは `Async` 接尾辞を付与。
- Minimal API: ルート名は明確に（例: `.WithName("GetWeatherForecast")`）。小さなレコード/DTO は使用箇所近くに配置し、肥大化したら `Man10BankService/Models/` 配下へ。
- フォーマット: 可能ならプッシュ前に `dotnet format` を実行。
- コメント: 実装時はコード内にコメントを付けない（必要な説明は PR やコミットメッセージに記載）。

## テストガイドライン
- フレームワーク: xUnit（推奨）。`tests/Man10BankService.Tests/` に並列実行可能なテストプロジェクトを作成。
- 命名: `ClassName_MethodName_ShouldExpected`（ファイル名・メソッド名）。
- 実行: テストプロジェクト作成後はリポジトリルートで `dotnet test`。
- カバレッジ（推奨）: `coverlet` または `dotnet test /p:CollectCoverage=true` を使用。

## コミット & プルリクエスト ガイドライン
- コミット: 簡潔で命令形のメッセージを使用。Conventional Commits を推奨（例: `feat: add account deposit endpoint`）。
- 機能追加のたびにコミット: 機能を追加するごとに、関連変更を小さく分割して Git にコミットする（動く単位で頻繁にコミット）。
- PR: 目的、関連 Issue、簡単なテストノートを含める。必要に応じて API レスポンスのスクリーンショットや OpenAPI の差分を添付。
- CI 準備: ローカルで `dotnet build` と（存在する場合）`dotnet test` が成功し、コンソールに警告が出ない状態にする。

## ビルド・コミット運用ルール（重要）
- Man10BankService のコードを編集した後は、必ず `dotnet build` を実行し、コンパイルエラーがないことを確認する。
- コンパイルエラーがあれば修正する。
- 修正が完了してコンパイルが通ってから Git にコミットする。
- テストコードのみの変更であっても、コンパイル（`dotnet build` または `dotnet test`）を実行してエラーがないことを確認する。

## セキュリティ & 設定のヒント
- シークレット: シークレットはコミットしない。ローカル開発では環境変数またはユーザーシークレット（`dotnet user-secrets`）を使用。
- プロファイル: アプリは環境ごとに `appsettings.*.json` を読み込む。ローカルの `dotnet run` は既定で `Development`。
- HTTPS: HTTPS リダイレクトを強制。必要に応じて開発証明書を信頼設定（`dotnet dev-certs https --trust`）。

## コミュニケーションルール
- 言語: すべてのコミュニケーション（Issue、PR、コメント、ドキュメント、コミットメッセージ）は日本語で行う。
- 明瞭性: 結論→理由→補足の順で簡潔に記述し、必要に応じてコード断片やパス（例: `Program.cs`）を示す。
- コミット文言: Git のコミットメッセージは日本語で書く（Conventional Commits の type/scope は英語でも可。本文・説明・BREAKING CHANGE は日本語）。
- 例外・ログ: 例外メッセージやログ、ユーザー向けエラーテキストも日本語で書く（短く具体的に。復旧手順があれば併記）。
