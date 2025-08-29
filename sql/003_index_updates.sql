-- Drop legacy indexes and create improved indexes

START TRANSACTION;

-- atm_log: add composite index
DROP INDEX IF EXISTS atm_log_uuid_player_date_index ON atm_log;
CREATE INDEX atm_log_uuid_player_date_index ON atm_log (uuid, player, date);

-- cheque_tbl: add helper indexes
CREATE INDEX IF NOT EXISTS cheque_tbl_uuid_index   ON cheque_tbl (uuid);
CREATE INDEX IF NOT EXISTS cheque_tbl_player_index ON cheque_tbl (player);

-- estate_history_tbl: replace uuid-only with (uuid, date)
DROP INDEX IF EXISTS estate_history_tbl_uuid_index ON estate_history_tbl;
CREATE INDEX estate_history_tbl_uuid_date_index ON estate_history_tbl (uuid, date);

-- loan_table: add lender side index
CREATE INDEX IF NOT EXISTS loan_table_lend_player_uuid_index ON loan_table (lend_player, lend_uuid);

-- money_log: drop old and add (uuid, player, date)
DROP INDEX IF EXISTS money_log_id_uuid_player_index ON money_log;
CREATE INDEX money_log_uuid_player_date_index ON money_log (uuid, player, date);

-- server_estate_history: add date index
CREATE INDEX IF NOT EXISTS server_estate_history_date_index ON server_estate_history (date);

-- server_loan_tbl: add player index
CREATE INDEX IF NOT EXISTS server_loan_tbl_player_index ON server_loan_tbl (player);

-- user_bank: replace wide composite with separate indexes
DROP INDEX IF EXISTS user_bank_id_uuid_player_index ON user_bank;
CREATE INDEX user_bank_uuid_index   ON user_bank (uuid);
CREATE INDEX user_bank_player_index ON user_bank (player);

COMMIT;

