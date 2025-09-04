# syntax=docker/dockerfile:1

# Enable multi-arch by honoring build args provided by BuildKit/buildx
ARG TARGETPLATFORM
ARG BUILDPLATFORM
ARG TARGETOS
ARG TARGETARCH

# Build stage
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore only the app project first for better layer caching
COPY Man10BankService/Man10BankService.csproj Man10BankService/
RUN dotnet restore Man10BankService/Man10BankService.csproj

# Copy the rest of the source
COPY . .

# Publish self-contained single-file (compressed) to a clean folder
ARG TARGETOS
ARG TARGETARCH
RUN set -eux; \
    case "$TARGETARCH" in \
      amd64) RIDARCH=x64 ;; \
      arm64) RIDARCH=arm64 ;; \
      *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac; \
    dotnet publish Man10BankService/Man10BankService.csproj \
      -c Release \
      -o /app/publish \
      -r ${TARGETOS}-${RIDARCH} \
      --self-contained true \
      /p:PublishSingleFile=true \
      /p:EnableCompressionInSingleFile=true \
      /p:UseAppHost=true \
      --no-restore

# Runtime stage
FROM --platform=$TARGETPLATFORM gcr.io/distroless/base-debian12:nonroot AS final
WORKDIR /app

# Kestrel on 8080 inside the container
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 8080

# Optional: set environment here or via `docker run -e ...`
# - Database__Host
# - Database__Port
# - Database__Name
# - Database__User
# - Database__Password
# - Database__TreatTinyAsBoolean=true
# Example:
# docker run -d -p 8080:8080 \
#  -e Database__Host=db \
#  -e Database__Port=3306 \
#  -e Database__Name=man10 \
#  -e Database__User=app \
#  -e Database__Password=secret \
#  --name man10-bank man10-bank-service:latest

COPY --from=build /app/publish/Man10BankService /app/Man10BankService

ENTRYPOINT ["/app/Man10BankService"]
