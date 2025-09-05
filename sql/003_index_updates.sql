-- Drop legacy indexes and create improved indexes

START TRANSACTION;

-- atm_log: add composite index (use ALTER TABLE with IF EXISTS/IF NOT EXISTS for MySQL 8.4)
ALTER TABLE atm_log DROP INDEX IF EXISTS atm_log_uuid_player_date_index;
ALTER TABLE atm_log ADD INDEX IF NOT EXISTS atm_log_uuid_player_date_index (uuid, player, date);

-- cheque_tbl: add helper indexes
ALTER TABLE cheque_tbl ADD INDEX IF NOT EXISTS cheque_tbl_uuid_index (uuid);
ALTER TABLE cheque_tbl ADD INDEX IF NOT EXISTS cheque_tbl_player_index (player);

-- estate_history_tbl: replace uuid-only with (uuid, date)
ALTER TABLE estate_history_tbl DROP INDEX IF EXISTS estate_history_tbl_uuid_index;
ALTER TABLE estate_history_tbl ADD INDEX IF NOT EXISTS estate_history_tbl_uuid_date_index (uuid, date);

-- loan_table: add lender side index
ALTER TABLE loan_table ADD INDEX IF NOT EXISTS loan_table_lend_player_uuid_index (lend_player, lend_uuid);

-- money_log: drop old and add (uuid, player, date)
ALTER TABLE money_log DROP INDEX IF EXISTS money_log_id_uuid_player_index;
ALTER TABLE money_log ADD INDEX IF NOT EXISTS money_log_uuid_player_date_index (uuid, player, date);

-- server_estate_history: add date index
ALTER TABLE server_estate_history ADD INDEX IF NOT EXISTS server_estate_history_date_index (date);

-- server_loan_tbl: add player index
ALTER TABLE server_loan_tbl ADD INDEX IF NOT EXISTS server_loan_tbl_player_index (player);

-- user_bank: replace wide composite with separate indexes
ALTER TABLE user_bank DROP INDEX IF EXISTS user_bank_id_uuid_player_index;
ALTER TABLE user_bank ADD INDEX IF NOT EXISTS user_bank_uuid_index (uuid);
ALTER TABLE user_bank ADD INDEX IF NOT EXISTS user_bank_player_index (player);

COMMIT;
