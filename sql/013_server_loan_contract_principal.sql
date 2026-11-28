-- Migration: 新リボの契約に元本(principal)を記録する
--
-- 利息の上限(未払いの利息が 元本 × ServerLoan:MaxInterestMultiplier を超えない)を判定するため、
-- 残債のうちの元本を契約に持たせる。残債 − 元本 が未払いの利息になる。
--   server_loan_contract.principal           残債のうちの元本
--   server_loan_contract_log.principal_after 操作後の元本(監査用)
--
-- sql/012 の後に適用する。旧バージョンのサービスはこのファイルを適用しても動作する(追加のみ)。何度実行しても結果は同じ。

SET @col_exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'server_loan_contract'
                      AND COLUMN_NAME = 'principal');
SET @sql := IF(@col_exists > 0,
               'SELECT 1',
               'ALTER TABLE server_loan_contract ADD COLUMN principal DECIMAL(20, 0) DEFAULT 0 NOT NULL COMMENT ''残債のうちの元本'' AFTER borrow_amount');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- 列を追加した時点ですでに残債がある契約は、内訳が分からないので、その時点の残債の全額を元本とする。
SET @sql := IF(@col_exists > 0,
               'SELECT 1',
               'UPDATE server_loan_contract SET principal = borrow_amount WHERE borrow_amount > 0');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @col_exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'server_loan_contract_log'
                      AND COLUMN_NAME = 'principal_after');
SET @sql := IF(@col_exists > 0,
               'SELECT 1',
               'ALTER TABLE server_loan_contract_log ADD COLUMN principal_after DECIMAL(20, 0) DEFAULT 0 NOT NULL COMMENT ''操作後の元本(監査用)'' AFTER debt_after');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
