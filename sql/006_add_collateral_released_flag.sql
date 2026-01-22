ALTER TABLE loan_table
    ADD COLUMN collateral_released TINYINT(1) DEFAULT 0 NOT NULL;
