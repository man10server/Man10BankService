SET @released_at_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'loan_table'
      AND COLUMN_NAME = 'collateral_released_at'
);

SET @sql := IF(@released_at_exists > 0,
               'SELECT 1',
               'ALTER TABLE loan_table ADD COLUMN collateral_released_at DATETIME NULL');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @release_reason_exists := (
    SELECT COUNT(1)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'loan_table'
      AND COLUMN_NAME = 'collateral_release_reason'
);

SET @sql := IF(@release_reason_exists > 0,
               'SELECT 1',
               'ALTER TABLE loan_table ADD COLUMN collateral_release_reason VARCHAR(32) NULL');
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;
