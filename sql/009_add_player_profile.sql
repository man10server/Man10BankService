-- Migration: ログイン時に登録するプレイヤー名簿 player_profile を追加
-- プラグインがログイン時に UUID と Minecraft ID を送って登録する(改名はここで追従する)。
-- 名前解決はこの名簿 → user_bank の順で行い、Mojang API には問い合わせない。

CREATE TABLE IF NOT EXISTS player_profile
(
    uuid       VARCHAR(36)                            NOT NULL PRIMARY KEY,
    name       VARCHAR(16)                            NOT NULL,
    updated_at DATETIME     DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE INDEX player_profile_name_index ON player_profile (name);

-- 既存口座の名前で初期投入する(デプロイ直後から口座持ちのプレイヤーを解決できるようにする)。
-- 次回ログイン時に最新の名前で上書きされる。
INSERT IGNORE INTO player_profile (uuid, name)
SELECT uuid, player FROM user_bank WHERE player <> '';
