-- Migration: 電子マネー(Vault Provider)用テーブルを追加する。
-- user_vault: 電子マネー残高の真実(source of truth)。Uuid を UNIQUE にして1プレイヤー1行。
-- vault_log : 電子マネーの取引ログ(銀行の money_log とは分離)。
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
    id           INT AUTO_INCREMENT PRIMARY KEY,
    player       VARCHAR(16)                            NOT NULL,
    uuid         VARCHAR(36)                            NOT NULL,
    plugin_name  VARCHAR(16)  DEFAULT ''                NOT NULL,
    note         VARCHAR(64)  DEFAULT ''                NOT NULL,
    display_note VARCHAR(64)  DEFAULT ''                NOT NULL,
    server       VARCHAR(16)  DEFAULT ''                NOT NULL,
    deposit      TINYINT(1)   DEFAULT 1                 NOT NULL,
    date         DATETIME     DEFAULT CURRENT_TIMESTAMP NOT NULL,
    amount       DECIMAL(20, 0)                         NOT NULL
);

CREATE INDEX vault_log_uuid_player_date_index ON vault_log (uuid, player, date);
