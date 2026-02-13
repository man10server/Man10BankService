SET @col_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'loan_table'
      AND COLUMN_NAME = 'collateral_released'
);

SET @sql := IF(@col_exists > 0,
               'SELECT 1',
               'ALTER TABLE loan_table ADD COLUMN collateral_released TINYINT(1) DEFAULT 0 NOT NULL');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
