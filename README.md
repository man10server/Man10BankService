## docker-composeでの立ち上げ方法

1. APIサーバーをローカルにcloneで落として `docker compose up -d`
これでAPIサーバーとテスト用のMySQLサーバーが立ち上がる。
下の画像のようにログが出ていたら成功。
終了するときはCtrl-CでOK
    
    ![スクリーンショット 2026-01-17 11.48.16.png](attachment:570e71db-d17b-419c-8e3a-ee99944ea8b2:スクリーンショット_2026-01-17_11.48.16.png)
    
2. 疎通確認
ポート番号などは環境に合わせて変更
- ヘルスチェックコマンド
`curl -X 'GET' 'http://0.0.0.0:8080/api/Health' -H 'accept: application/json'`
以下のように出力されたら問題ない
`{"service":"Man10BankService","serverTimeUtc":"2026-01-17T02:52:26.090967Z","startedAtUtc":"2026-01-17T02:48:08.5268261Z","uptimeSeconds":257,"database":true}`

- Swagger
URL[http://localhost:8080/swagger/index.html]
Swaggerはバージョンによってはうまくいかなかったりするかも
3. プラグインをサーバーに入れてConfigの設定
Configの設定方法は以下の通り
    
    ```yaml
    api:
      baseUrl: http://bank:8080 # 例: http://localhost:8080 または https://api.example.com
      apiKey: '' # 必要に応じて設定（未使用なら空）
      timeout:
        requestMs: 10000 # リクエスト全体のタイムアウト
        connectMs: 3000 # 接続確立のタイムアウト
        socketMs: 10000 # ソケット読み書きのタイムアウト
      retries: 2 # 失敗時の自動リトライ回数（0で無効）
    ```
    
    baseUrlのところに、ヘルスチェックなどで疎通確認したURLを入力
    同じDockerCompose内で起動した場合は `bank`  で繋がる
    ゲーム内またはマイクラコンソールから `/bankop health` で接続が確認できる
    

### MySQLに繋がらない場合

初回起動でデータベースの生成ができなかった場合(ヘルスチェックで `"database":false` になる場合)

1. DBコンソールに入る
ユーザー名とパスワードは `docker-compose.yml` に記載
2. 以下のテーブル作成クエリを実行
`CREATE DATABSE man10_bank;`
`USE man10_bank;`
`# 以下のリンクのクエリを実行`
`# https://github.com/man10server/Man10BankService/blob/master/sql/db.sql`
3. 再度ヘルスチェックで接続できているか確認
