# syntax=docker/dockerfile:1.6

# Multi-stage build for ASP.NET Core (.NET 9)
# - Builds the app using the SDK image
# - Runs on the ASP.NET runtime image (multi-arch: amd64/arm64)
# - appsettings.json は環境変数で上書き可能（例: Database__Host）

###############################################
# Build stage
###############################################
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

ARG TARGETARCH

# 依存復元（レイヤキャッシュ最適化のため .csproj のみ先にコピー）
COPY Man10BankService/Man10BankService.csproj Man10BankService/

# TARGETARCHに応じたRIDをでrestore実行
RUN set -eux; \
  case "${TARGETARCH:-amd64}" in \
    amd64) RID=linux-x64 ;; \
    arm64) RID=linux-arm64 ;; \
    *) echo "Unsupported TARGETARCH=${TARGETARCH}"; exit 1 ;; \
  esac; \
  dotnet restore Man10BankService/Man10BankService.csproj -r "$RID"

# 残りのソースをコピー
COPY . .

# 単一バイナリ発行
RUN set -eux; \
  case "${TARGETARCH:-amd64}" in \
    amd64) RID=linux-x64 ;; \
    arm64) RID=linux-arm64 ;; \
    *) echo "Unsupported TARGETARCH=${TARGETARCH}"; exit 1 ;; \
  esac; \
  dotnet publish Man10BankService/Man10BankService.csproj \
    -c Release \
    -r "$RID" \
    -p:PublishSingleFile=true \
    -p:SelfContained=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=None \
    -p:StripSymbols=true \
    -o /app/out

###############################################
# Runtime stage
###############################################
FROM gcr.io/distroless/cc-debian12:nonroot AS final
WORKDIR /app

# Kestrel を 8080 で待ち受け
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 8080

# ビルド成果物をコピー
COPY --from=build /app/out/Man10BankService /app/Man10BankService

# 実行
ENTRYPOINT ["/app/Man10BankService"]
