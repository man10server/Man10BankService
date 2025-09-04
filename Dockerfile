# syntax=docker/dockerfile:1

# Enable multi-arch by honoring build args provided by BuildKit/buildx
ARG TARGETPLATFORM
ARG BUILDPLATFORM
ARG TARGETOS
ARG TARGETARCH

# Build stage (match target platform to avoid crossgen issues)
FROM --platform=$TARGETPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore only the app project first for better layer caching (RID-aware)
COPY Man10BankService/Man10BankService.csproj Man10BankService/
ARG TARGETOS
ARG TARGETARCH
RUN set -eux; \
    case "$TARGETARCH" in \
      amd64) RIDARCH=x64 ;; \
      arm64) RIDARCH=arm64 ;; \
      *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac; \
    dotnet restore Man10BankService/Man10BankService.csproj -r ${TARGETOS}-${RIDARCH}

# Copy the rest of the source
COPY . .

# Publish self-contained single-file to a clean folder
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
      -p:PublishSingleFile=true \
      -p:SelfContained=true \
      -p:PublishTrimmed=false \
      -p:EnableCompressionInSingleFile=false \
      -p:PublishReadyToRun=false \
      -p:UseAppHost=true

# Runtime stage
FROM --platform=$TARGETPLATFORM gcr.io/distroless/base-debian12:nonroot AS final
WORKDIR /app

# Kestrel on 8080 inside the container
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 8080

COPY --from=build /app/publish/Man10BankService /app/Man10BankService

ENTRYPOINT ["/app/Man10BankService"]
