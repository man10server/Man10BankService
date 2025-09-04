# Build stage: restore and publish a single-file, self-contained binary (compressed)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Build args provided by BuildKit/buildx
ARG TARGETOS
ARG TARGETARCH
ARG BUILDPLATFORM

# Mitigate arm64 Roslyn/JIT issues under certain builders (e.g., W^X enforcement)
ENV DOTNET_EnableWriteXorExecute=0 \
    DOTNET_CLI_HOME=/tmp \
    NUGET_XMLDOC_MODE=skip

# Copy only project files first for better cache
COPY Man10BankService/Man10BankService.csproj Man10BankService/

# Copy the rest of the source
COPY . .

# Restore and publish per-arch (linux-x64 or linux-arm64) as self-contained single-file (compressed)
RUN set -eux; \
    if [ "${TARGETARCH}" = "amd64" ]; then RID=linux-x64; \
    elif [ "${TARGETARCH}" = "arm64" ]; then RID=linux-arm64; \
    else echo "Unsupported TARGETARCH=${TARGETARCH}"; exit 1; fi; \
    dotnet restore -r ${RID} Man10BankService/Man10BankService.csproj; \
    dotnet publish Man10BankService/Man10BankService.csproj \
      -c Release \
      -r ${RID} \
      --self-contained true \
      -o /app/publish \
      /p:PublishSingleFile=true \
      /p:EnableCompressionInSingleFile=true \
      /p:PublishReadyToRun=false \
      /p:TieredPGO=false \
      /p:DebugType=None \
      /p:InvariantGlobalization=true \
      /p:StripSymbols=true

# Final stage: distroless (no dotnet runtime included)
# Use distroless with C/C++ runtime (includes libgcc_s, libstdc++)
FROM gcr.io/distroless/cc-debian12:nonroot AS final
WORKDIR /app

# Copy single binary
COPY --from=build /app/publish/Man10BankService /app/Man10BankService

# Configure runtime
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# Run as non-root
USER nonroot:nonroot

ENTRYPOINT ["/app/Man10BankService"]
