-- Migration: 入出金・送金の冪等キー(requestId)テーブルを追加
-- プラグインが要求ごとに生成する requestId と処理済み結果(残高)を保持し、
-- タイムアウト等で結果不明になった要求の再送時に二重実行せず初回結果を返すために使う。

CREATE TABLE IF NOT EXISTS idempotency_key
(
    request_id     VARCHAR(36)                            NOT NULL PRIMARY KEY,
    operation      VARCHAR(16)                            NOT NULL COMMENT 'deposit/withdraw/transfer',
    uuid           VARCHAR(36)                            NOT NULL,
    amount         DECIMAL(20,0)                          NOT NULL,
    result_balance DECIMAL(20,0)                          NOT NULL,
    created_at     DATETIME     DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE INDEX idempotency_key_created_at_index ON idempotency_key (created_at);
