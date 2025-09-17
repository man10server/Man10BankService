-- cheque_tbl に op カラムを追加する
-- 既に存在する場合は何もしない

SET @col_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'cheque_tbl'
      AND COLUMN_NAME = 'op'
);

SET @sql := IF(@col_exists > 0,
               'SELECT 1',
               'ALTER TABLE cheque_tbl ADD COLUMN op TINYINT DEFAULT 0 NOT NULL');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

