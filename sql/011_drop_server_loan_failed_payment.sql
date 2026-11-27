-- Migration: server_loan_tbl から使われていない failed_payment を削除
-- 延滞の判定は server_loan_log(直近の定期返済が失敗かどうか)で行っており、この列は加算されるだけで参照されていなかった。
--
-- 注意: この列を参照する旧バージョン(0.4.0 以前)のサービスは、削除後は動作しない。
--       新バージョンのデプロイと動作確認が済み、旧バージョンへ戻さないと判断してから適用する。
--       新バージョンは、この列が残っていても削除済みでも動作する。

SET @col_exists := (SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'server_loan_tbl'
                      AND COLUMN_NAME = 'failed_payment');
SET @sql := IF(@col_exists > 0,
               'ALTER TABLE server_loan_tbl DROP COLUMN failed_payment',
               'SELECT 1');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
