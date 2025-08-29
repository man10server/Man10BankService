-- Enforce NOT NULL constraints (normalize NULLs and set defaults)

START TRANSACTION;
SET FOREIGN_KEY_CHECKS = 0;

-- atm_log
UPDATE atm_log SET player = '' WHERE player IS NULL;
UPDATE atm_log SET uuid = '' WHERE uuid IS NULL;
UPDATE atm_log SET deposit = 0 WHERE deposit IS NULL;
UPDATE atm_log SET date = CURRENT_TIMESTAMP WHERE date IS NULL;
ALTER TABLE atm_log
  MODIFY player VARCHAR(16) NOT NULL,
  MODIFY uuid   VARCHAR(36) NOT NULL,
  MODIFY deposit TINYINT(1) NOT NULL DEFAULT 0,
  MODIFY date   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;

-- cheque_tbl
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

-- estate_tbl
UPDATE estate_tbl SET date = CURRENT_TIMESTAMP WHERE date IS NULL;
UPDATE estate_tbl SET player = '' WHERE player IS NULL;
ALTER TABLE estate_tbl
  MODIFY date   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  MODIFY player VARCHAR(16) NOT NULL DEFAULT '';

-- estate_history_tbl
UPDATE estate_history_tbl SET uuid = '' WHERE uuid IS NULL;
UPDATE estate_history_tbl SET date = CURRENT_TIMESTAMP WHERE date IS NULL;
UPDATE estate_history_tbl SET player = '' WHERE player IS NULL;
ALTER TABLE estate_history_tbl
  MODIFY uuid   VARCHAR(36) NOT NULL,
  MODIFY date   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  MODIFY player VARCHAR(16) NOT NULL DEFAULT '';

-- loan_table
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

-- money_log
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

-- server_estate_history
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

-- server_loan_tbl
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

-- user_bank
ALTER TABLE user_bank
  MODIFY player VARCHAR(16) NOT NULL,
  MODIFY uuid   VARCHAR(36) NOT NULL;

SET FOREIGN_KEY_CHECKS = 1;
COMMIT;

