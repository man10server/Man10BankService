#!/usr/bin/env sh
set -eu

docker buildx build --platform linux/arm64 -t man10-bank-service:local --load .
