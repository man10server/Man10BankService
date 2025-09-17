create table atm_log
(
    id      int auto_increment
        primary key,
    player  varchar(16)                          not null,
    uuid    varchar(36)                          not null,
    deposit tinyint(1) default 0                 not null,
    date    datetime   default CURRENT_TIMESTAMP not null,
    amount  decimal(20)                          not null
);

create index atm_log_player_uuid_date_index
    on atm_log (player, uuid, date);

create table cheque_tbl
(
    id         int auto_increment
        primary key,
    player     varchar(16)                            not null,
    uuid       varchar(36)                            not null,
    note       varchar(128) default ''                not null,
    date       datetime     default CURRENT_TIMESTAMP not null,
    used       tinyint      default 0                 not null,
    use_date   datetime     default CURRENT_TIMESTAMP not null,
    use_player varchar(16)  default ''                not null,
    amount     decimal(20)                            not null,
    op         tinyint      default 0                 not null
);

create index cheque_tbl_player_uuid_index
    on cheque_tbl (player, uuid);

create index cheque_tbl_used_use_player_index
    on cheque_tbl (used, use_player);

create table estate_history_tbl
(
    id     int auto_increment
        primary key,
    uuid   varchar(36)                           not null,
    player varchar(16) default ''                not null,
    date   datetime    default CURRENT_TIMESTAMP not null,
    vault  decimal(20)                           not null,
    bank   decimal(20)                           not null,
    cash   decimal(20)                           not null,
    estate decimal(20)                           not null,
    loan   decimal(20)                           not null,
    shop   decimal(20)                           not null,
    crypto decimal(20)                           not null,
    total  decimal(20)                           not null
);

create index estate_history_tbl_player_uuid_date_index
    on estate_history_tbl (player, uuid, date);

create table estate_tbl
(
    id     int auto_increment
        primary key,
    uuid   varchar(36)                           not null,
    player varchar(16) default ''                not null,
    date   datetime    default CURRENT_TIMESTAMP not null,
    vault  decimal(20)                           not null,
    bank   decimal(20)                           not null,
    cash   decimal(20)                           not null,
    estate decimal(20)                           not null,
    loan   decimal(20)                           not null,
    shop   decimal(20)                           not null,
    crypto decimal(20)                           not null,
    total  decimal(20)                           not null
)
    comment '現在の個人の資産テーブル';

create index estate_tbl_player_uuid_index
    on estate_tbl (player, uuid);

create table loan_table
(
    id              int auto_increment
        primary key,
    lend_player     varchar(16) default ''                not null,
    lend_uuid       varchar(36) default ''                not null,
    borrow_player   varchar(16) default ''                not null,
    borrow_uuid     varchar(36) default ''                not null,
    borrow_date     datetime    default CURRENT_TIMESTAMP not null,
    payback_date    datetime    default CURRENT_TIMESTAMP not null,
    collateral_item text                                  not null,
    amount          decimal(20)                           not null
);

create index loan_table_lend_player_uuid_index
    on loan_table (lend_player, lend_uuid);

create index loan_table_player_uuid_index
    on loan_table (borrow_player, borrow_uuid);

create table money_log
(
    id           int auto_increment
        primary key,
    player       varchar(16)                           not null,
    uuid         varchar(36)                           not null,
    plugin_name  varchar(16) default ''                not null,
    balance      double      default -1                null,
    note         varchar(64) default ''                not null,
    display_note varchar(64) default ''                not null,
    server       varchar(16) default ''                not null,
    deposit      tinyint(1)  default 1                 not null,
    date         datetime    default CURRENT_TIMESTAMP not null,
    amount       decimal(20)                           not null
);

create index money_log_uuid_player_date_index
    on money_log (uuid, player, date);

create table server_estate_history
(
    id     int auto_increment
        primary key,
    year   int      default 0                 not null,
    month  int      default 0                 not null,
    day    int      default 0                 not null,
    hour   int      default 0                 not null,
    date   datetime default CURRENT_TIMESTAMP not null,
    vault  decimal(20)                        not null,
    bank   decimal(20)                        not null,
    cash   decimal(20)                        not null,
    estate decimal(20)                        not null,
    loan   decimal(20)                        not null,
    shop   decimal(20)                        not null,
    crypto decimal(20)                        not null,
    total  decimal(20)                        not null
);

create index server_estate_history_date_index
    on server_estate_history (date);

create index server_estate_history_year_month_day_hour_index
    on server_estate_history (year, month, day, hour);

create table server_loan_log
(
    id     int auto_increment
        primary key,
    player varchar(16)                        not null,
    uuid   varchar(36)                        not null,
    action varchar(16)                        not null comment 'borrow/repay/interest',
    amount decimal(20)                        not null,
    date   datetime default CURRENT_TIMESTAMP not null
);

create index server_loan_log_uuid_date_index
    on server_loan_log (uuid, date);

create table server_loan_tbl
(
    id             int auto_increment
        primary key,
    player         varchar(16) default ''                not null,
    uuid           varchar(36) default ''                not null,
    borrow_date    datetime    default CURRENT_TIMESTAMP not null,
    last_pay_date  datetime    default CURRENT_TIMESTAMP not null,
    failed_payment int         default 0                 not null,
    stop_interest  tinyint     default 0                 not null,
    borrow_amount  decimal(20)                           not null,
    payment_amount decimal(20)                           not null
);

create index server_loan_tbl_player_uuid_index
    on server_loan_tbl (player, uuid);

create table user_bank
(
    id      int auto_increment
        primary key,
    player  varchar(16) not null,
    uuid    varchar(36) not null,
    balance decimal(20) not null
);

create index user_bank_player_uuid_index
    on user_bank (player, uuid);
