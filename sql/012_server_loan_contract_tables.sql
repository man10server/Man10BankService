-- Migration: 新しいサーバーローン(新リボ)用のテーブルを追加する
--
-- このバージョンから、サーバーローンは新しいテーブルで管理する。
--   server_loan_contract        契約(残債を持つ単位)
--   server_loan_contract_log    契約の操作の記録(借入枠と延滞はここから求める)
--   server_loan_contract_member 他のプレイヤーの契約に参加しているアカウント(サブ垢・連帯保証人)
--
-- これまでの server_loan_tbl / server_loan_log は「旧リボ」としてそのまま残す(このファイルでは変更しない)。
-- 旧バージョンのサービスはこのファイルを適用しても動作する(追加のみ)。何度実行しても結果は同じ。

CREATE TABLE IF NOT EXISTS server_loan_contract
(
    id             INT AUTO_INCREMENT PRIMARY KEY,
    uuid           VARCHAR(36)                        NOT NULL COMMENT '契約の本垢',
    player         VARCHAR(16)                        NOT NULL COMMENT '本垢の名前',
    borrow_amount  DECIMAL(20, 0) DEFAULT 0           NOT NULL COMMENT '残債(元本 + 利息)',
    payment_amount DECIMAL(20, 0) DEFAULT 0           NOT NULL COMMENT '支払額(設定)',
    stop_interest  TINYINT        DEFAULT 0           NOT NULL COMMENT '1 の間は利息を付けない',
    last_pay_date  DATETIME DEFAULT CURRENT_TIMESTAMP NOT NULL COMMENT '前回返済日',
    created_at     DATETIME DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT server_loan_contract_uuid_unique UNIQUE (uuid)
);

CREATE TABLE IF NOT EXISTS server_loan_contract_log
(
    id         INT AUTO_INCREMENT PRIMARY KEY,
    uuid       VARCHAR(36)                        NOT NULL COMMENT '契約の本垢',
    player     VARCHAR(16)                        NOT NULL COMMENT '本垢の名前',
    action     VARCHAR(16)                        NOT NULL COMMENT '操作の種類',
    amount     DECIMAL(20, 0)                     NOT NULL COMMENT '残債の増減',
    debt_after DECIMAL(20, 0)                     NOT NULL COMMENT '操作後の残債(監査用)',
    actor_uuid VARCHAR(36)                        NULL COMMENT '操作したプレイヤー。自動処理・運営の調整は NULL',
    date       DATETIME DEFAULT CURRENT_TIMESTAMP NOT NULL,
    INDEX server_loan_contract_log_uuid_date_index (uuid, date)
);

CREATE TABLE IF NOT EXISTS server_loan_contract_member
(
    uuid         VARCHAR(36)                        NOT NULL PRIMARY KEY COMMENT '参加するアカウント',
    owner_uuid   VARCHAR(36)                        NOT NULL COMMENT '参加先の契約の本垢(server_loan_contract.uuid)',
    linked_by    VARCHAR(16)                        NOT NULL COMMENT '紐付けた人(依頼した本人、または運営)',
    requested_at DATETIME DEFAULT CURRENT_TIMESTAMP NOT NULL,
    approved_at  DATETIME                           NULL COMMENT '承認日時。NULL の間は承認待ち',
    INDEX server_loan_contract_member_owner_uuid_index (owner_uuid)
);

-- -----------------------------------------------------------
-- 0.5.0 で登録済みのサブ垢・連帯保証人(server_loan_member の承認済みの行)を引き継ぐ
--   参加時に合算した残債は本垢の旧リボに残っている。引き継がないと、参加していたアカウントだけが
--   新リボを借りられてしまうため、参加の関係は新しいテーブルへ移す。
--   - server_loan_member が無い DB(0.4.0 以前から直接上げる場合)では何もしない。
--   - 承認待ちの行は引き継がない。
--   - 新しいメンバー表にすでに行があれば何もしない(二重に実行しても増えない)。
--   借入枠は契約のログから求めるので、参加のログ(MemberJoin)も同じ順番で残す。
-- -----------------------------------------------------------
SET @old_exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'server_loan_member');
SET @carry := IF(@old_exists > 0 AND (SELECT COUNT(1) FROM server_loan_contract_member) = 0, 1, 0);

SET @sql := IF(@carry = 1,
               'INSERT INTO server_loan_contract_log (uuid, player, action, amount, debt_after, actor_uuid, date)
                SELECT m.owner_uuid, COALESCE(t.player, ''''), ''MemberJoin'', 0, 0, m.uuid, m.approved_at
                FROM server_loan_member m
                         LEFT JOIN server_loan_tbl t ON t.uuid = m.owner_uuid
                WHERE m.approved_at IS NOT NULL
                ORDER BY m.approved_at, m.uuid',
               'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @sql := IF(@carry = 1,
               'INSERT INTO server_loan_contract_member (uuid, owner_uuid, linked_by, requested_at, approved_at)
                SELECT uuid, owner_uuid, linked_by, requested_at, approved_at
                FROM server_loan_member
                WHERE approved_at IS NOT NULL',
               'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
