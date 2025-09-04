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

# Publish self-contained files to a clean folder
RUN dotnet publish Man10BankService/Man10BankService.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM --platform=$TARGETPLATFORM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Kestrel on 8080 inside the container
ENV ASPNETCORE_URLS=http://+:8080
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

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Man10BankService.dll"]
