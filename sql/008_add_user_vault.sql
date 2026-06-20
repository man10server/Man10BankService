-- 008: 電子マネー(user_vault / vault_log)テーブル追加と user_bank.uuid の UNIQUE 化
--
-- 注意: user_bank.uuid の UNIQUE 化は既存の重複 uuid があると失敗する。
--       導入前に重複行を整理すること(設計書 §9.2)。重複検出は以下で確認できる:
--         SELECT uuid, COUNT(*) c FROM user_bank GROUP BY uuid HAVING c > 1;

-- user_vault(電子マネー残高。唯一の真実。uuid は UNIQUE)
create table if not exists user_vault
(
    id      int auto_increment
        primary key,
    player  varchar(16)          not null,
    uuid    varchar(36)          not null,
    balance decimal(20) default 0 not null,
    version bigint      default 0 not null,
    constraint uq_user_vault_uuid
        unique (uuid)
);

-- vault_log(電子マネー専用ログ。operation_id は冪等キーで指定時 UNIQUE)
create table if not exists vault_log
(
    id            int auto_increment
        primary key,
    player        varchar(16)                           not null,
    uuid          varchar(36)                           not null,
    plugin_name   varchar(32) default ''                not null,
    amount        decimal(20)                           not null,
    note          varchar(64) default ''                not null,
    display_note  varchar(64) default ''                not null,
    server        varchar(16) default ''                not null,
    deposit       tinyint(1)  default 1                 not null,
    date          datetime    default CURRENT_TIMESTAMP not null,
    operation_id  varchar(64)                           null,
    source        varchar(16)                           not null,
    balance_after decimal(20)                           not null,
    constraint uq_vault_log_operation_id
        unique (operation_id)
);

-- vault_log の検索用インデックス(存在しなければ作成)
SET @idx_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'vault_log'
      AND INDEX_NAME = 'vault_log_uuid_date_index'
);
SET @sql := IF(@idx_exists > 0,
               'SELECT 1',
               'CREATE INDEX vault_log_uuid_date_index ON vault_log (uuid, date)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- user_bank.uuid を UNIQUE 化(move の FOR UPDATE 対象を 1 行に固定するため)
-- 既存の重複 uuid があるとここで失敗する。失敗した場合は重複整理後に再実行すること。
SET @uniq_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'user_bank'
      AND INDEX_NAME = 'uq_user_bank_uuid'
);
SET @sql := IF(@uniq_exists > 0,
               'SELECT 1',
               'ALTER TABLE user_bank ADD CONSTRAINT uq_user_bank_uuid UNIQUE (uuid)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
