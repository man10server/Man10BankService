-- Man10Bank DECIMAL(20,0) migration
-- Generated at 2025-08-23T18:37:42+09:00
-- Strategy: add *_new DECIMAL(20,0) columns, copy with TRUNCATE(), drop old DOUBLE columns, rename *_new to original names.
-- Note: If NULLs are not desired, the optional lines fill NULL with 0.

START TRANSACTION;
SET FOREIGN_KEY_CHECKS = 0;

-- user_bank.balance
ALTER TABLE user_bank ADD COLUMN balance_new DECIMAL(20,0) NULL;
UPDATE user_bank SET balance_new = TRUNCATE(balance, 0);
UPDATE user_bank SET balance_new = 0 WHERE balance_new IS NULL; -- optional
ALTER TABLE user_bank DROP COLUMN balance;
ALTER TABLE user_bank CHANGE COLUMN balance_new balance DECIMAL(20,0) NOT NULL;

-- money_log.amount
ALTER TABLE money_log ADD COLUMN amount_new DECIMAL(20,0) NULL;
UPDATE money_log SET amount_new = TRUNCATE(amount, 0);
UPDATE money_log SET amount_new = 0 WHERE amount_new IS NULL; -- optional
ALTER TABLE money_log DROP COLUMN amount;
ALTER TABLE money_log CHANGE COLUMN amount_new amount DECIMAL(20,0) NOT NULL;

-- Normalize NULLs then enforce NOT NULL and add/fix indexes

-- atm_log: fill NULLs and enforce NOT NULL
UPDATE atm_log SET player = '' WHERE player IS NULL;
UPDATE atm_log SET uuid = '' WHERE uuid IS NULL;
UPDATE atm_log SET deposit = 0 WHERE deposit IS NULL;
UPDATE atm_log SET date = CURRENT_TIMESTAMP WHERE date IS NULL;
ALTER TABLE atm_log
  MODIFY player VARCHAR(16) NOT NULL,
  MODIFY uuid   VARCHAR(36) NOT NULL,
  MODIFY deposit TINYINT(1) NOT NULL DEFAULT 0,
  MODIFY date   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;
CREATE INDEX IF NOT EXISTS atm_log_uuid_player_date_index ON atm_log (uuid, player, date);

-- cheque_tbl: fill NULLs and enforce NOT NULL
UPDATE cheque_tbl SET player = '' WHERE player IS NULL;
UPDATE cheque_tbl SET uuid = '' WHERE uuid IS NULL;
UPDATE cheque_tbl SET note = '' WHERE note IS NULL;
UPDATE cheque_tbl SET date = CURRENT_TIMESTAMP WHERE date IS NULL;
UPDATE cheque_tbl SET use_date = CURRENT_TIMESTAMP WHERE use_date IS NULL;
UPDATE cheque_tbl SET use_player = '' WHERE use_player IS NULL;
UPDATE cheque_tbl SET used = 0 WHERE used IS NULL;
ALTER TABLE cheque_tbl
  MODIFY player VARCHAR(16) NOT NULL,
  MODIFY uuid   VARCHAR(36) NOT NULL,
  MODIFY note   VARCHAR(128) NOT NULL DEFAULT '',
  MODIFY date   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  MODIFY use_date   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  MODIFY use_player VARCHAR(16) NOT NULL DEFAULT '',
  MODIFY used   TINYINT NOT NULL DEFAULT 0;
CREATE INDEX IF NOT EXISTS cheque_tbl_uuid_index ON cheque_tbl (uuid);
CREATE INDEX IF NOT EXISTS cheque_tbl_player_index ON cheque_tbl (player);

-- estate_tbl: enforce NOT NULL defaults
UPDATE estate_tbl SET date = CURRENT_TIMESTAMP WHERE date IS NULL;
UPDATE estate_tbl SET player = '' WHERE player IS NULL;
ALTER TABLE estate_tbl
  MODIFY date   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  MODIFY player VARCHAR(16) NOT NULL DEFAULT '';

-- estate_history_tbl: enforce NOT NULL and composite index
UPDATE estate_history_tbl SET uuid = '' WHERE uuid IS NULL;
UPDATE estate_history_tbl SET date = CURRENT_TIMESTAMP WHERE date IS NULL;
UPDATE estate_history_tbl SET player = '' WHERE player IS NULL;
ALTER TABLE estate_history_tbl
  MODIFY uuid   VARCHAR(36) NOT NULL,
  MODIFY date   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  MODIFY player VARCHAR(16) NOT NULL DEFAULT '';
DROP INDEX estate_history_tbl_uuid_index ON estate_history_tbl;
CREATE INDEX estate_history_tbl_uuid_date_index ON estate_history_tbl (uuid, date);

-- loan_table: enforce NOT NULLs and add lender index
UPDATE loan_table SET lend_player = '' WHERE lend_player IS NULL;
UPDATE loan_table SET lend_uuid = '' WHERE lend_uuid IS NULL;
UPDATE loan_table SET borrow_player = '' WHERE borrow_player IS NULL;
UPDATE loan_table SET borrow_uuid = '' WHERE borrow_uuid IS NULL;
UPDATE loan_table SET borrow_date = CURRENT_TIMESTAMP WHERE borrow_date IS NULL;
UPDATE loan_table SET payback_date = CURRENT_TIMESTAMP WHERE payback_date IS NULL;
UPDATE loan_table SET collateral_item = '' WHERE collateral_item IS NULL;
ALTER TABLE loan_table
  MODIFY lend_player VARCHAR(16) NOT NULL DEFAULT '',
  MODIFY lend_uuid   VARCHAR(36) NOT NULL DEFAULT '',
  MODIFY borrow_player VARCHAR(16) NOT NULL DEFAULT '',
  MODIFY borrow_uuid   VARCHAR(36) NOT NULL DEFAULT '',
  MODIFY borrow_date   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  MODIFY payback_date  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  MODIFY collateral_item TEXT NOT NULL;
CREATE INDEX IF NOT EXISTS loan_table_lend_player_uuid_index ON loan_table (lend_player, lend_uuid);

-- money_log: clean NULLs, fix index
UPDATE money_log SET plugin_name = '' WHERE plugin_name IS NULL;
UPDATE money_log SET note = '' WHERE note IS NULL;
UPDATE money_log SET display_note = '' WHERE display_note IS NULL;
UPDATE money_log SET server = '' WHERE server IS NULL;
UPDATE money_log SET deposit = 1 WHERE deposit IS NULL;
ALTER TABLE money_log
  MODIFY plugin_name VARCHAR(16) NOT NULL DEFAULT '',
  MODIFY note        VARCHAR(64) NOT NULL DEFAULT '',
  MODIFY display_note VARCHAR(64) NOT NULL DEFAULT '',
  MODIFY server      VARCHAR(16) NOT NULL DEFAULT '',
  MODIFY deposit     TINYINT(1) NOT NULL DEFAULT 1;
DROP INDEX money_log_id_uuid_player_index ON money_log;
CREATE INDEX money_log_uuid_player_date_index ON money_log (uuid, player, date);

-- server_estate_history: enforce NOT NULLs and add date index
UPDATE server_estate_history SET year = 0 WHERE year IS NULL;
UPDATE server_estate_history SET month = 0 WHERE month IS NULL;
UPDATE server_estate_history SET day = 0 WHERE day IS NULL;
UPDATE server_estate_history SET hour = 0 WHERE hour IS NULL;
UPDATE server_estate_history SET date = CURRENT_TIMESTAMP WHERE date IS NULL;
ALTER TABLE server_estate_history
  MODIFY year  INT NOT NULL DEFAULT 0,
  MODIFY month INT NOT NULL DEFAULT 0,
  MODIFY day   INT NOT NULL DEFAULT 0,
  MODIFY hour  INT NOT NULL DEFAULT 0,
  MODIFY date  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;
CREATE INDEX IF NOT EXISTS server_estate_history_date_index ON server_estate_history (date);

-- server_loan_tbl: enforce NOT NULLs and add index
UPDATE server_loan_tbl SET player = '' WHERE player IS NULL;
UPDATE server_loan_tbl SET uuid = '' WHERE uuid IS NULL;
UPDATE server_loan_tbl SET borrow_date = CURRENT_TIMESTAMP WHERE borrow_date IS NULL;
UPDATE server_loan_tbl SET last_pay_date = CURRENT_TIMESTAMP WHERE last_pay_date IS NULL;
UPDATE server_loan_tbl SET failed_payment = 0 WHERE failed_payment IS NULL;
UPDATE server_loan_tbl SET stop_interest = 0 WHERE stop_interest IS NULL;
ALTER TABLE server_loan_tbl
  MODIFY player VARCHAR(16) NOT NULL DEFAULT '',
  MODIFY uuid   VARCHAR(36) NOT NULL DEFAULT '',
  MODIFY borrow_date   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  MODIFY last_pay_date DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  MODIFY failed_payment INT NOT NULL DEFAULT 0,
  MODIFY stop_interest  TINYINT NOT NULL DEFAULT 0;
CREATE INDEX IF NOT EXISTS server_loan_tbl_player_index ON server_loan_tbl (player);

-- user_bank: fix index and ensure NOT NULLs
ALTER TABLE user_bank
  MODIFY player VARCHAR(16) NOT NULL,
  MODIFY uuid   VARCHAR(36) NOT NULL;
DROP INDEX user_bank_id_uuid_player_index ON user_bank;
CREATE INDEX user_bank_uuid_index   ON user_bank (uuid);
CREATE INDEX user_bank_player_index ON user_bank (player);
-- atm_log.amount
ALTER TABLE atm_log ADD COLUMN amount_new DECIMAL(20,0) NULL;
UPDATE atm_log SET amount_new = TRUNCATE(amount, 0);
UPDATE atm_log SET amount_new = 0 WHERE amount_new IS NULL; -- optional
ALTER TABLE atm_log DROP COLUMN amount;
ALTER TABLE atm_log CHANGE COLUMN amount_new amount DECIMAL(20,0) NOT NULL;

-- cheque_tbl.amount
ALTER TABLE cheque_tbl ADD COLUMN amount_new DECIMAL(20,0) NULL;
UPDATE cheque_tbl SET amount_new = TRUNCATE(amount, 0);
UPDATE cheque_tbl SET amount_new = 0 WHERE amount_new IS NULL; -- optional
ALTER TABLE cheque_tbl DROP COLUMN amount;
ALTER TABLE cheque_tbl CHANGE COLUMN amount_new amount DECIMAL(20,0) NOT NULL;

-- server_loan_tbl.borrow_amount
ALTER TABLE server_loan_tbl ADD COLUMN borrow_amount_new DECIMAL(20,0) NULL;
UPDATE server_loan_tbl SET borrow_amount_new = TRUNCATE(borrow_amount, 0);
UPDATE server_loan_tbl SET borrow_amount_new = 0 WHERE borrow_amount_new IS NULL; -- optional
ALTER TABLE server_loan_tbl DROP COLUMN borrow_amount;
ALTER TABLE server_loan_tbl CHANGE COLUMN borrow_amount_new borrow_amount DECIMAL(20,0) NOT NULL;

-- server_loan_tbl.payment_amount
ALTER TABLE server_loan_tbl ADD COLUMN payment_amount_new DECIMAL(20,0) NULL;
UPDATE server_loan_tbl SET payment_amount_new = TRUNCATE(payment_amount, 0);
UPDATE server_loan_tbl SET payment_amount_new = 0 WHERE payment_amount_new IS NULL; -- optional
ALTER TABLE server_loan_tbl DROP COLUMN payment_amount;
ALTER TABLE server_loan_tbl CHANGE COLUMN payment_amount_new payment_amount DECIMAL(20,0) NOT NULL;

-- loan_table.amount
ALTER TABLE loan_table ADD COLUMN amount_new DECIMAL(20,0) NULL;
UPDATE loan_table SET amount_new = TRUNCATE(amount, 0);
UPDATE loan_table SET amount_new = 0 WHERE amount_new IS NULL; -- optional
ALTER TABLE loan_table DROP COLUMN amount;
ALTER TABLE loan_table CHANGE COLUMN amount_new amount DECIMAL(20,0) NOT NULL;

-- estate_tbl.*
ALTER TABLE estate_tbl ADD COLUMN vault_new DECIMAL(20,0) NULL,
  ADD COLUMN bank_new DECIMAL(20,0) NULL,
  ADD COLUMN cash_new DECIMAL(20,0) NULL,
  ADD COLUMN estate_new DECIMAL(20,0) NULL,
  ADD COLUMN loan_new DECIMAL(20,0) NULL,
  ADD COLUMN shop_new DECIMAL(20,0) NULL,
  ADD COLUMN crypto_new DECIMAL(20,0) NULL,
  ADD COLUMN total_new DECIMAL(20,0) NULL;
UPDATE estate_tbl SET
  vault_new = TRUNCATE(vault, 0),
  bank_new = TRUNCATE(bank, 0),
  cash_new = TRUNCATE(cash, 0),
  estate_new = TRUNCATE(estate, 0),
  loan_new = TRUNCATE(loan, 0),
  shop_new = TRUNCATE(shop, 0),
  crypto_new = TRUNCATE(crypto, 0),
  total_new = TRUNCATE(total, 0);
UPDATE estate_tbl SET
  vault_new = IFNULL(vault_new, 0),
  bank_new = IFNULL(bank_new, 0),
  cash_new = IFNULL(cash_new, 0),
  estate_new = IFNULL(estate_new, 0),
  loan_new = IFNULL(loan_new, 0),
  shop_new = IFNULL(shop_new, 0),
  crypto_new = IFNULL(crypto_new, 0),
  total_new = IFNULL(total_new, 0);
ALTER TABLE estate_tbl
  DROP COLUMN vault,
  DROP COLUMN bank,
  DROP COLUMN cash,
  DROP COLUMN estate,
  DROP COLUMN loan,
  DROP COLUMN shop,
  DROP COLUMN crypto,
  DROP COLUMN total;
ALTER TABLE estate_tbl
  CHANGE COLUMN vault_new vault DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN bank_new bank DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN cash_new cash DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN estate_new estate DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN loan_new loan DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN shop_new shop DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN crypto_new crypto DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN total_new total DECIMAL(20,0) NOT NULL;

-- estate_history_tbl.*
ALTER TABLE estate_history_tbl ADD COLUMN vault_new DECIMAL(20,0) NULL,
  ADD COLUMN bank_new DECIMAL(20,0) NULL,
  ADD COLUMN cash_new DECIMAL(20,0) NULL,
  ADD COLUMN estate_new DECIMAL(20,0) NULL,
  ADD COLUMN loan_new DECIMAL(20,0) NULL,
  ADD COLUMN shop_new DECIMAL(20,0) NULL,
  ADD COLUMN crypto_new DECIMAL(20,0) NULL,
  ADD COLUMN total_new DECIMAL(20,0) NULL;
UPDATE estate_history_tbl SET
  vault_new = TRUNCATE(vault, 0),
  bank_new = TRUNCATE(bank, 0),
  cash_new = TRUNCATE(cash, 0),
  estate_new = TRUNCATE(estate, 0),
  loan_new = TRUNCATE(loan, 0),
  shop_new = TRUNCATE(shop, 0),
  crypto_new = TRUNCATE(crypto, 0),
  total_new = TRUNCATE(total, 0);
UPDATE estate_history_tbl SET
  vault_new = IFNULL(vault_new, 0),
  bank_new = IFNULL(bank_new, 0),
  cash_new = IFNULL(cash_new, 0),
  estate_new = IFNULL(estate_new, 0),
  loan_new = IFNULL(loan_new, 0),
  shop_new = IFNULL(shop_new, 0),
  crypto_new = IFNULL(crypto_new, 0),
  total_new = IFNULL(total_new, 0);
ALTER TABLE estate_history_tbl
  DROP COLUMN vault,
  DROP COLUMN bank,
  DROP COLUMN cash,
  DROP COLUMN estate,
  DROP COLUMN loan,
  DROP COLUMN shop,
  DROP COLUMN crypto,
  DROP COLUMN total;
ALTER TABLE estate_history_tbl
  CHANGE COLUMN vault_new vault DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN bank_new bank DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN cash_new cash DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN estate_new estate DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN loan_new loan DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN shop_new shop DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN crypto_new crypto DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN total_new total DECIMAL(20,0) NOT NULL;

-- server_estate_history.*
ALTER TABLE server_estate_history ADD COLUMN vault_new DECIMAL(20,0) NULL,
  ADD COLUMN bank_new DECIMAL(20,0) NULL,
  ADD COLUMN cash_new DECIMAL(20,0) NULL,
  ADD COLUMN estate_new DECIMAL(20,0) NULL,
  ADD COLUMN loan_new DECIMAL(20,0) NULL,
  ADD COLUMN shop_new DECIMAL(20,0) NULL,
  ADD COLUMN crypto_new DECIMAL(20,0) NULL,
  ADD COLUMN total_new DECIMAL(20,0) NULL;
UPDATE server_estate_history SET
  vault_new = TRUNCATE(vault, 0),
  bank_new = TRUNCATE(bank, 0),
  cash_new = TRUNCATE(cash, 0),
  estate_new = TRUNCATE(estate, 0),
  loan_new = TRUNCATE(loan, 0),
  shop_new = TRUNCATE(shop, 0),
  crypto_new = TRUNCATE(crypto, 0),
  total_new = TRUNCATE(total, 0);
UPDATE server_estate_history SET
  vault_new = IFNULL(vault_new, 0),
  bank_new = IFNULL(bank_new, 0),
  cash_new = IFNULL(cash_new, 0),
  estate_new = IFNULL(estate_new, 0),
  loan_new = IFNULL(loan_new, 0),
  shop_new = IFNULL(shop_new, 0),
  crypto_new = IFNULL(crypto_new, 0),
  total_new = IFNULL(total_new, 0);
ALTER TABLE server_estate_history
  DROP COLUMN vault,
  DROP COLUMN bank,
  DROP COLUMN cash,
  DROP COLUMN estate,
  DROP COLUMN loan,
  DROP COLUMN shop,
  DROP COLUMN crypto,
  DROP COLUMN total;
ALTER TABLE server_estate_history
  CHANGE COLUMN vault_new vault DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN bank_new bank DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN cash_new cash DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN estate_new estate DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN loan_new loan DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN shop_new shop DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN crypto_new crypto DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN total_new total DECIMAL(20,0) NOT NULL;

SET FOREIGN_KEY_CHECKS = 1;
COMMIT;
-- Man10Bank DECIMAL(20,0) migration
-- Generated at 2025-08-23T18:37:42+09:00
-- Strategy: add *_new DECIMAL(20,0) columns, copy with TRUNCATE(), drop old DOUBLE columns, rename *_new to original names.
-- Note: This script only converts numeric columns to DECIMAL(20,0).

START TRANSACTION;
SET FOREIGN_KEY_CHECKS = 0;

-- user_bank.balance
ALTER TABLE user_bank ADD COLUMN balance_new DECIMAL(20,0) NULL;
UPDATE user_bank SET balance_new = TRUNCATE(balance, 0);
UPDATE user_bank SET balance_new = 0 WHERE balance_new IS NULL; -- optional
ALTER TABLE user_bank DROP COLUMN balance;
ALTER TABLE user_bank CHANGE COLUMN balance_new balance DECIMAL(20,0) NOT NULL;

-- money_log.amount
ALTER TABLE money_log ADD COLUMN amount_new DECIMAL(20,0) NULL;
UPDATE money_log SET amount_new = TRUNCATE(amount, 0);
UPDATE money_log SET amount_new = 0 WHERE amount_new IS NULL; -- optional
ALTER TABLE money_log DROP COLUMN amount;
ALTER TABLE money_log CHANGE COLUMN amount_new amount DECIMAL(20,0) NOT NULL;

-- atm_log.amount
ALTER TABLE atm_log ADD COLUMN amount_new DECIMAL(20,0) NULL;
UPDATE atm_log SET amount_new = TRUNCATE(amount, 0);
UPDATE atm_log SET amount_new = 0 WHERE amount_new IS NULL; -- optional
ALTER TABLE atm_log DROP COLUMN amount;
ALTER TABLE atm_log CHANGE COLUMN amount_new amount DECIMAL(20,0) NOT NULL;

-- cheque_tbl.amount
ALTER TABLE cheque_tbl ADD COLUMN amount_new DECIMAL(20,0) NULL;
UPDATE cheque_tbl SET amount_new = TRUNCATE(amount, 0);
UPDATE cheque_tbl SET amount_new = 0 WHERE amount_new IS NULL; -- optional
ALTER TABLE cheque_tbl DROP COLUMN amount;
ALTER TABLE cheque_tbl CHANGE COLUMN amount_new amount DECIMAL(20,0) NOT NULL;

-- server_loan_tbl.borrow_amount
ALTER TABLE server_loan_tbl ADD COLUMN borrow_amount_new DECIMAL(20,0) NULL;
UPDATE server_loan_tbl SET borrow_amount_new = TRUNCATE(borrow_amount, 0);
UPDATE server_loan_tbl SET borrow_amount_new = 0 WHERE borrow_amount_new IS NULL; -- optional
ALTER TABLE server_loan_tbl DROP COLUMN borrow_amount;
ALTER TABLE server_loan_tbl CHANGE COLUMN borrow_amount_new borrow_amount DECIMAL(20,0) NOT NULL;

-- server_loan_tbl.payment_amount
ALTER TABLE server_loan_tbl ADD COLUMN payment_amount_new DECIMAL(20,0) NULL;
UPDATE server_loan_tbl SET payment_amount_new = TRUNCATE(payment_amount, 0);
UPDATE server_loan_tbl SET payment_amount_new = 0 WHERE payment_amount_new IS NULL; -- optional
ALTER TABLE server_loan_tbl DROP COLUMN payment_amount;
ALTER TABLE server_loan_tbl CHANGE COLUMN payment_amount_new payment_amount DECIMAL(20,0) NOT NULL;

-- loan_table.amount
ALTER TABLE loan_table ADD COLUMN amount_new DECIMAL(20,0) NULL;
UPDATE loan_table SET amount_new = TRUNCATE(amount, 0);
UPDATE loan_table SET amount_new = 0 WHERE amount_new IS NULL; -- optional
ALTER TABLE loan_table DROP COLUMN amount;
ALTER TABLE loan_table CHANGE COLUMN amount_new amount DECIMAL(20,0) NOT NULL;

-- estate_tbl.*
ALTER TABLE estate_tbl ADD COLUMN vault_new DECIMAL(20,0) NULL,
  ADD COLUMN bank_new DECIMAL(20,0) NULL,
  ADD COLUMN cash_new DECIMAL(20,0) NULL,
  ADD COLUMN estate_new DECIMAL(20,0) NULL,
  ADD COLUMN loan_new DECIMAL(20,0) NULL,
  ADD COLUMN shop_new DECIMAL(20,0) NULL,
  ADD COLUMN crypto_new DECIMAL(20,0) NULL,
  ADD COLUMN total_new DECIMAL(20,0) NULL;
UPDATE estate_tbl SET
  vault_new = TRUNCATE(vault, 0),
  bank_new = TRUNCATE(bank, 0),
  cash_new = TRUNCATE(cash, 0),
  estate_new = TRUNCATE(estate, 0),
  loan_new = TRUNCATE(loan, 0),
  shop_new = TRUNCATE(shop, 0),
  crypto_new = TRUNCATE(crypto, 0),
  total_new = TRUNCATE(total, 0);
UPDATE estate_tbl SET
  vault_new = IFNULL(vault_new, 0),
  bank_new = IFNULL(bank_new, 0),
  cash_new = IFNULL(cash_new, 0),
  estate_new = IFNULL(estate_new, 0),
  loan_new = IFNULL(loan_new, 0),
  shop_new = IFNULL(shop_new, 0),
  crypto_new = IFNULL(crypto_new, 0),
  total_new = IFNULL(total_new, 0);
ALTER TABLE estate_tbl
  DROP COLUMN vault,
  DROP COLUMN bank,
  DROP COLUMN cash,
  DROP COLUMN estate,
  DROP COLUMN loan,
  DROP COLUMN shop,
  DROP COLUMN crypto,
  DROP COLUMN total;
ALTER TABLE estate_tbl
  CHANGE COLUMN vault_new vault DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN bank_new bank DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN cash_new cash DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN estate_new estate DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN loan_new loan DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN shop_new shop DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN crypto_new crypto DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN total_new total DECIMAL(20,0) NOT NULL;

-- estate_history_tbl.*
ALTER TABLE estate_history_tbl ADD COLUMN vault_new DECIMAL(20,0) NULL,
  ADD COLUMN bank_new DECIMAL(20,0) NULL,
  ADD COLUMN cash_new DECIMAL(20,0) NULL,
  ADD COLUMN estate_new DECIMAL(20,0) NULL,
  ADD COLUMN loan_new DECIMAL(20,0) NULL,
  ADD COLUMN shop_new DECIMAL(20,0) NULL,
  ADD COLUMN crypto_new DECIMAL(20,0) NULL,
  ADD COLUMN total_new DECIMAL(20,0) NULL;
UPDATE estate_history_tbl SET
  vault_new = TRUNCATE(vault, 0),
  bank_new = TRUNCATE(bank, 0),
  cash_new = TRUNCATE(cash, 0),
  estate_new = TRUNCATE(estate, 0),
  loan_new = TRUNCATE(loan, 0),
  shop_new = TRUNCATE(shop, 0),
  crypto_new = TRUNCATE(crypto, 0),
  total_new = TRUNCATE(total, 0);
UPDATE estate_history_tbl SET
  vault_new = IFNULL(vault_new, 0),
  bank_new = IFNULL(bank_new, 0),
  cash_new = IFNULL(cash_new, 0),
  estate_new = IFNULL(estate_new, 0),
  loan_new = IFNULL(loan_new, 0),
  shop_new = IFNULL(shop_new, 0),
  crypto_new = IFNULL(crypto_new, 0),
  total_new = IFNULL(total_new, 0);
ALTER TABLE estate_history_tbl
  DROP COLUMN vault,
  DROP COLUMN bank,
  DROP COLUMN cash,
  DROP COLUMN estate,
  DROP COLUMN loan,
  DROP COLUMN shop,
  DROP COLUMN crypto,
  DROP COLUMN total;
ALTER TABLE estate_history_tbl
  CHANGE COLUMN vault_new vault DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN bank_new bank DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN cash_new cash DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN estate_new estate DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN loan_new loan DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN shop_new shop DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN crypto_new crypto DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN total_new total DECIMAL(20,0) NOT NULL;

-- server_estate_history.*
ALTER TABLE server_estate_history ADD COLUMN vault_new DECIMAL(20,0) NULL,
  ADD COLUMN bank_new DECIMAL(20,0) NULL,
  ADD COLUMN cash_new DECIMAL(20,0) NULL,
  ADD COLUMN estate_new DECIMAL(20,0) NULL,
  ADD COLUMN loan_new DECIMAL(20,0) NULL,
  ADD COLUMN shop_new DECIMAL(20,0) NULL,
  ADD COLUMN crypto_new DECIMAL(20,0) NULL,
  ADD COLUMN total_new DECIMAL(20,0) NULL;
UPDATE server_estate_history SET
  vault_new = TRUNCATE(vault, 0),
  bank_new = TRUNCATE(bank, 0),
  cash_new = TRUNCATE(cash, 0),
  estate_new = TRUNCATE(estate, 0),
  loan_new = TRUNCATE(loan, 0),
  shop_new = TRUNCATE(shop, 0),
  crypto_new = TRUNCATE(crypto, 0),
  total_new = TRUNCATE(total, 0);
UPDATE server_estate_history SET
  vault_new = IFNULL(vault_new, 0),
  bank_new = IFNULL(bank_new, 0),
  cash_new = IFNULL(cash_new, 0),
  estate_new = IFNULL(estate_new, 0),
  loan_new = IFNULL(loan_new, 0),
  shop_new = IFNULL(shop_new, 0),
  crypto_new = IFNULL(crypto_new, 0),
  total_new = IFNULL(total_new, 0);
ALTER TABLE server_estate_history
  DROP COLUMN vault,
  DROP COLUMN bank,
  DROP COLUMN cash,
  DROP COLUMN estate,
  DROP COLUMN loan,
  DROP COLUMN shop,
  DROP COLUMN crypto,
  DROP COLUMN total;
ALTER TABLE server_estate_history
  CHANGE COLUMN vault_new vault DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN bank_new bank DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN cash_new cash DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN estate_new estate DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN loan_new loan DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN shop_new shop DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN crypto_new crypto DECIMAL(20,0) NOT NULL,
  CHANGE COLUMN total_new total DECIMAL(20,0) NOT NULL;

SET FOREIGN_KEY_CHECKS = 1;
COMMIT;
