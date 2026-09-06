-- Migration: 電子マネー(Vault Provider)用テーブルを追加する。
-- user_vault: 電子マネー残高の真実(source of truth)。Uuid を UNIQUE にして1プレイヤー1行。
-- vault_log : 電子マネーの取引ログ(銀行の money_log とは分離)。
--             冪等キー(operation_id)・操作元(source)・session・操作後残高/version を監査用に持つ。
-- 既存環境への再適用に備え IF NOT EXISTS で冪等にする。

CREATE TABLE IF NOT EXISTS user_vault
(
    id      INT AUTO_INCREMENT PRIMARY KEY,
    player  VARCHAR(16)              NOT NULL,
    uuid    VARCHAR(36)              NOT NULL,
    balance DECIMAL(20, 0) DEFAULT 0 NOT NULL,
    version BIGINT         DEFAULT 0 NOT NULL,
    CONSTRAINT uq_user_vault_uuid UNIQUE (uuid)
) COMMENT '電子マネー残高(真実)テーブル';

CREATE TABLE IF NOT EXISTS vault_log
(
    id            INT AUTO_INCREMENT PRIMARY KEY,
    player        VARCHAR(16)                            NOT NULL,
    uuid          VARCHAR(36)                            NOT NULL,
    plugin_name   VARCHAR(16)    DEFAULT ''              NOT NULL,
    note          VARCHAR(64)    DEFAULT ''              NOT NULL,
    display_note  VARCHAR(64)    DEFAULT ''              NOT NULL,
    server        VARCHAR(16)    DEFAULT ''              NOT NULL,
    deposit       TINYINT(1)     DEFAULT 1               NOT NULL,
    date          DATETIME       DEFAULT CURRENT_TIMESTAMP NOT NULL,
    amount        DECIMAL(20, 0)                         NOT NULL,
    operation_id  VARCHAR(36)    NULL,
    source        VARCHAR(16)    DEFAULT ''              NOT NULL,
    session_id    VARCHAR(64)    NULL,
    balance_after DECIMAL(20, 0) DEFAULT 0               NOT NULL,
    version_after BIGINT         DEFAULT 0               NOT NULL,
    -- 冪等キー。NULL は複数許容し、指定時のみ一意(重複送信は保存済み結果を返す)。
    CONSTRAINT uq_vault_log_operation_id UNIQUE (operation_id)
);

CREATE INDEX vault_log_uuid_player_date_index ON vault_log (uuid, player, date);
