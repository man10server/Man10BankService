CREATE TABLE atm_log
(
    id      INT AUTO_INCREMENT PRIMARY KEY,
    player  VARCHAR(16)          NOT NULL,
    uuid    VARCHAR(36)          NOT NULL,
    amount  DECIMAL(20,0)        NOT NULL,
    deposit TINYINT(1) DEFAULT 0 NOT NULL,
    date    DATETIME   DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE TABLE cheque_tbl
(
    id         INT AUTO_INCREMENT PRIMARY KEY,
    player     VARCHAR(16)                   NOT NULL,
    uuid       VARCHAR(36)                   NOT NULL,
    amount     DECIMAL(20,0)                 NOT NULL,
    note       VARCHAR(128)  DEFAULT ''      NOT NULL,
    date       DATETIME      DEFAULT CURRENT_TIMESTAMP NOT NULL,
    use_date   DATETIME      DEFAULT CURRENT_TIMESTAMP NOT NULL,
    use_player VARCHAR(16)   DEFAULT ''      NOT NULL,
    used       TINYINT       DEFAULT 0       NOT NULL
);

CREATE INDEX cheque_tbl_used_index  ON cheque_tbl (used);
CREATE INDEX cheque_tbl_uuid_index  ON cheque_tbl (uuid);
CREATE INDEX cheque_tbl_player_index ON cheque_tbl (player);

CREATE TABLE estate_history_tbl
(
    id     INT AUTO_INCREMENT PRIMARY KEY,
    uuid   VARCHAR(36)                                  NOT NULL,
    date   DATETIME     DEFAULT CURRENT_TIMESTAMP       NOT NULL,
    player VARCHAR(16)  DEFAULT ''                      NOT NULL,
    vault  DECIMAL(20,0) DEFAULT 0                      NOT NULL,
    bank   DECIMAL(20,0) DEFAULT 0                      NOT NULL,
    cash   DECIMAL(20,0) DEFAULT 0                      NOT NULL,
    estate DECIMAL(20,0) DEFAULT 0                      NOT NULL,
    loan   DECIMAL(20,0) DEFAULT 0                      NOT NULL,
    shop   DECIMAL(20,0) DEFAULT 0                      NOT NULL,
    crypto DECIMAL(20,0) DEFAULT 0                      NOT NULL,
    total  DECIMAL(20,0) DEFAULT 0                      NOT NULL
);

CREATE INDEX estate_history_tbl_uuid_date_index ON estate_history_tbl (uuid, date);

CREATE TABLE estate_tbl
(
    id     INT AUTO_INCREMENT PRIMARY KEY,
    uuid   VARCHAR(36)                               NOT NULL,
    date   DATETIME         DEFAULT CURRENT_TIMESTAMP NOT NULL,
    player VARCHAR(16)      DEFAULT ''               NOT NULL,
    vault  DECIMAL(20,0)    DEFAULT 0                NOT NULL,
    bank   DECIMAL(20,0)    DEFAULT 0                NOT NULL,
    cash   DECIMAL(20,0)    DEFAULT 0                NOT NULL,
    estate DECIMAL(20,0)    DEFAULT 0                NOT NULL,
    loan   DECIMAL(20,0)    DEFAULT 0                NOT NULL,
    shop   DECIMAL(20,0)    DEFAULT 0                NOT NULL,
    crypto DECIMAL(20,0)    DEFAULT 0                NOT NULL,
    total  DECIMAL(20,0)    DEFAULT 0                NOT NULL
) COMMENT '現在の個人の資産テーブル';

CREATE INDEX estate_tbl_uuid_index ON estate_tbl (uuid);

CREATE TABLE loan_table
(
    id              INT AUTO_INCREMENT PRIMARY KEY,
    lend_player     VARCHAR(16)  DEFAULT ''               NOT NULL,
    lend_uuid       VARCHAR(36)  DEFAULT ''               NOT NULL,
    borrow_player   VARCHAR(16)  DEFAULT ''               NOT NULL,
    borrow_uuid     VARCHAR(36)  DEFAULT ''               NOT NULL,
    borrow_date     DATETIME     DEFAULT CURRENT_TIMESTAMP NOT NULL,
    payback_date    DATETIME     DEFAULT CURRENT_TIMESTAMP NOT NULL,
    amount          DECIMAL(20,0) DEFAULT 0               NOT NULL,
    collateral_item TEXT         DEFAULT ''               NOT NULL
);

CREATE INDEX loan_table_player_uuid_index       ON loan_table (borrow_player, borrow_uuid);
CREATE INDEX loan_table_lend_player_uuid_index  ON loan_table (lend_player, lend_uuid);

CREATE TABLE money_log
(
    id           INT AUTO_INCREMENT PRIMARY KEY,
    player       VARCHAR(16)                          NOT NULL,
    uuid         VARCHAR(36)                          NOT NULL,
    plugin_name  VARCHAR(16)  DEFAULT ''              NOT NULL,
    amount       DECIMAL(20,0) DEFAULT 0              NOT NULL,
    note         VARCHAR(64)   DEFAULT ''              NOT NULL,
    display_note VARCHAR(64)   DEFAULT ''              NOT NULL,
    server       VARCHAR(16)   DEFAULT ''              NOT NULL,
    deposit      TINYINT(1)    DEFAULT 1               NOT NULL,
    date         DATETIME      DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE INDEX money_log_uuid_player_date_index ON money_log (uuid, player, date);

CREATE TABLE server_estate_history
(
    id     INT AUTO_INCREMENT PRIMARY KEY,
    vault  DECIMAL(20,0) DEFAULT 0                NOT NULL,
    bank   DECIMAL(20,0) DEFAULT 0                NOT NULL,
    cash   DECIMAL(20,0) DEFAULT 0                NOT NULL,
    estate DECIMAL(20,0) DEFAULT 0                NOT NULL,
    loan   DECIMAL(20,0) DEFAULT 0                NOT NULL,
    crypto DECIMAL(20,0) DEFAULT 0                NOT NULL,
    shop   DECIMAL(20,0) DEFAULT 0                NOT NULL,
    total  DECIMAL(20,0) DEFAULT 0                NOT NULL,
    year   INT            DEFAULT 0               NOT NULL,
    month  INT            DEFAULT 0               NOT NULL,
    day    INT            DEFAULT 0               NOT NULL,
    hour   INT            DEFAULT 0               NOT NULL,
    date   DATETIME       DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE INDEX server_estate_history_year_month_day_hour_index ON server_estate_history (year, month, day, hour);
CREATE INDEX server_estate_history_date_index ON server_estate_history (date);

CREATE TABLE server_loan_tbl
(
    id             INT AUTO_INCREMENT PRIMARY KEY,
    player         VARCHAR(16)  DEFAULT ''               NOT NULL COMMENT '借りたプレイヤー',
    uuid           VARCHAR(36)  DEFAULT ''               NOT NULL,
    borrow_date    DATETIME     DEFAULT CURRENT_TIMESTAMP NOT NULL COMMENT '借りた日',
    last_pay_date  DATETIME     DEFAULT CURRENT_TIMESTAMP NOT NULL COMMENT '最後に支払った日',
    borrow_amount  DECIMAL(20,0) DEFAULT 0               NOT NULL COMMENT '借りた金額の合計',
    payment_amount DECIMAL(20,0) DEFAULT 0               NOT NULL COMMENT '週ごとの支払額',
    failed_payment INT           DEFAULT 0               NOT NULL COMMENT '支払いに失敗した回数',
    stop_interest  TINYINT       DEFAULT 0               NOT NULL COMMENT '利息をたすかどうか'
);

CREATE INDEX server_loan_tbl_uuid_borrow_amount_index ON server_loan_tbl (uuid, borrow_amount);
CREATE INDEX server_loan_tbl_player_index ON server_loan_tbl (player);

-- ローン操作ログ（借入/返済/金利付与）
CREATE TABLE server_loan_log
(
    id      INT AUTO_INCREMENT PRIMARY KEY,
    player  VARCHAR(16)                    NOT NULL,
    uuid    VARCHAR(36)                    NOT NULL,
    action  VARCHAR(16)                    NOT NULL COMMENT 'borrow/repay/interest',
    amount  DECIMAL(20,0)                  NOT NULL,
    note    VARCHAR(64)  DEFAULT ''        NOT NULL,
    date    DATETIME     DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE INDEX server_loan_log_uuid_date_index ON server_loan_log (uuid, date);

CREATE TABLE user_bank
(
    id      INT AUTO_INCREMENT PRIMARY KEY,
    player  VARCHAR(16)       NOT NULL,
    uuid    VARCHAR(36)       NOT NULL,
    balance DECIMAL(20,0) DEFAULT 0 NOT NULL
);

CREATE INDEX user_bank_uuid_index   ON user_bank (uuid);
CREATE INDEX user_bank_player_index ON user_bank (player);
