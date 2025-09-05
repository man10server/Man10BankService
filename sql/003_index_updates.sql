-- MySQL 8.4 では DROP/CREATE INDEX の IF EXISTS/IF NOT EXISTS が使えないため、
-- INFORMATION_SCHEMA を参照して条件付きで実行する。

-- ユーティリティ: インデックス存在チェック用の共通クエリの雛形
-- 使用例:
--   SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
--                   WHERE TABLE_SCHEMA = DATABASE()
--                     AND TABLE_NAME = 'table'
--                     AND INDEX_NAME = 'index');
--   SET @sql := IF(@exists > 0, 'ALTER TABLE table DROP INDEX index', 'DO 0');
--   PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------
-- atm_log: (uuid, player, date)
-- ---------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'atm_log'
                  AND INDEX_NAME = 'atm_log_uuid_player_date_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE atm_log ADD INDEX atm_log_uuid_player_date_index (uuid, player, date)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------
-- cheque_tbl: used, uuid, player
-- ---------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'cheque_tbl'
                  AND INDEX_NAME = 'cheque_tbl_used_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE cheque_tbl ADD INDEX cheque_tbl_used_index (used)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'cheque_tbl'
                  AND INDEX_NAME = 'cheque_tbl_uuid_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE cheque_tbl ADD INDEX cheque_tbl_uuid_index (uuid)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'cheque_tbl'
                  AND INDEX_NAME = 'cheque_tbl_player_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE cheque_tbl ADD INDEX cheque_tbl_player_index (player)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------------------
-- estate_history_tbl: drop old uuid-only, ensure (uuid, date)
-- -------------------------------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'estate_history_tbl'
                  AND INDEX_NAME = 'estate_history_tbl_uuid_index');
SET @sql := IF(@exists > 0,
               'ALTER TABLE estate_history_tbl DROP INDEX estate_history_tbl_uuid_index',
               'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'estate_history_tbl'
                  AND INDEX_NAME = 'estate_history_tbl_uuid_date_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE estate_history_tbl ADD INDEX estate_history_tbl_uuid_date_index (uuid, date)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------
-- estate_tbl: uuid
-- ---------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'estate_tbl'
                  AND INDEX_NAME = 'estate_tbl_uuid_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE estate_tbl ADD INDEX estate_tbl_uuid_index (uuid)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -------------------------------------
-- loan_table: borrower & lender indexes
-- -------------------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'loan_table'
                  AND INDEX_NAME = 'loan_table_player_uuid_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE loan_table ADD INDEX loan_table_player_uuid_index (borrow_player, borrow_uuid)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'loan_table'
                  AND INDEX_NAME = 'loan_table_lend_player_uuid_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE loan_table ADD INDEX loan_table_lend_player_uuid_index (lend_player, lend_uuid)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------
-- money_log: drop legacy, ensure (uuid,player,date)
-- ---------------------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'money_log'
                  AND INDEX_NAME = 'money_log_id_uuid_player_index');
SET @sql := IF(@exists > 0,
               'ALTER TABLE money_log DROP INDEX money_log_id_uuid_player_index',
               'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'money_log'
                  AND INDEX_NAME = 'money_log_uuid_player_date_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE money_log ADD INDEX money_log_uuid_player_date_index (uuid, player, date)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------
-- server_estate_history: date + ymdh
-- ---------------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'server_estate_history'
                  AND INDEX_NAME = 'server_estate_history_date_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE server_estate_history ADD INDEX server_estate_history_date_index (date)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'server_estate_history'
                  AND INDEX_NAME = 'server_estate_history_year_month_day_hour_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE server_estate_history ADD INDEX server_estate_history_year_month_day_hour_index (year, month, day, hour)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- -----------------------------
-- server_loan_tbl: uuid+amount, player
-- -----------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'server_loan_tbl'
                  AND INDEX_NAME = 'server_loan_tbl_uuid_borrow_amount_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE server_loan_tbl ADD INDEX server_loan_tbl_uuid_borrow_amount_index (uuid, borrow_amount)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'server_loan_tbl'
                  AND INDEX_NAME = 'server_loan_tbl_player_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE server_loan_tbl ADD INDEX server_loan_tbl_player_index (player)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------
-- server_loan_log: (uuid,date)
-- ---------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'server_loan_log'
                  AND INDEX_NAME = 'server_loan_log_uuid_date_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE server_loan_log ADD INDEX server_loan_log_uuid_date_index (uuid, date)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------
-- user_bank: drop legacy, ensure (uuid) & (player)
-- ---------------------------
SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'user_bank'
                  AND INDEX_NAME = 'user_bank_id_uuid_player_index');
SET @sql := IF(@exists > 0,
               'ALTER TABLE user_bank DROP INDEX user_bank_id_uuid_player_index',
               'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'user_bank'
                  AND INDEX_NAME = 'user_bank_uuid_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE user_bank ADD INDEX user_bank_uuid_index (uuid)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.STATISTICS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND TABLE_NAME = 'user_bank'
                  AND INDEX_NAME = 'user_bank_player_index');
SET @sql := IF(@exists > 0,
               'SELECT 1',
               'ALTER TABLE user_bank ADD INDEX user_bank_player_index (player)');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
