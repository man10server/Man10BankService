-- Migration: Add server_loan_log table for server loan operations logging
-- Contains only the DDL required for the new log table and its index.

CREATE TABLE server_loan_log
(
    id      INT AUTO_INCREMENT PRIMARY KEY,
    player  VARCHAR(16)                    NOT NULL,
    uuid    VARCHAR(36)                    NOT NULL,
    action  VARCHAR(16)                    NOT NULL COMMENT 'borrow/repay/interest',
    amount  DECIMAL(20,0)                  NOT NULL,
    date    DATETIME     DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE INDEX server_loan_log_uuid_date_index ON server_loan_log (uuid, date);

