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

# 依存復元（レイヤキャッシュ最適化のため .csproj のみ先にコピー）
COPY Man10BankService/Man10BankService.csproj Man10BankService/
RUN dotnet restore Man10BankService/Man10BankService.csproj

# 残りのソースをコピーして発行
COPY . .
RUN dotnet publish Man10BankService/Man10BankService.csproj -c Release -o /app/out

###############################################
# Runtime stage
###############################################
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Kestrel を 8080 で待ち受け
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# appsettings の上書き例（必要に応じて docker run -e で指定）
# 例: -e "Database__Host=db" -e "Database__User=app"

# 発行成果物をコピー
COPY --from=build /app/out .

# 実行
ENTRYPOINT ["dotnet", "Man10BankService.dll"]

