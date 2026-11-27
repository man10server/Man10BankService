-- Migration: サーバーローンを「契約」単位で扱うための追加変更
-- 既存の行は書き換えない(追加のみ)。適用前のサービスもこのスキーマのまま動作する。
--
-- 適用前に、server_loan_tbl に uuid の重複や空の行が無いことを確認する(あると一意制約の追加が失敗する)。
--   SELECT uuid, COUNT(*) AS cnt FROM server_loan_tbl GROUP BY uuid HAVING cnt > 1 OR uuid = '';

-- -----------------------------------------------------------
-- server_loan_tbl: 1 プレイヤー(本垢) = 1 契約を一意制約で保証する
-- -----------------------------------------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'server_loan_tbl'
                  AND INDEX_NAME = 'server_loan_tbl_uuid_unique');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE server_loan_tbl ADD UNIQUE INDEX server_loan_tbl_uuid_unique (uuid)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
