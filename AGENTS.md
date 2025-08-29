# Repository Guidelines

## Project Structure & Module Organization
- Root: solution `Man10BankService.sln`.
- App: `Man10BankService/` (ASP.NET Core minimal API, .NET 9).
  - Entry: `Program.cs`
  - Config: `appsettings.json`, `appsettings.Development.json`
  - HTTP samples: `Man10BankService.http`
  - Build output: `bin/`, `obj/` (git-ignored)

## Build, Test, and Development Commands
- Build: `dotnet build` — restores packages and compiles the solution.
- Run (dev): `dotnet run --project Man10BankService` — starts Kestrel; OpenAPI doc is served only in Development at `/openapi/v1.json`.
- Watch: `dotnet watch --project Man10BankService run` — hot-reloads on file changes.
- Restore: `dotnet restore` — fetches NuGet deps.

## Coding Style & Naming Conventions
- Language: C# with `nullable` and `implicit usings` enabled.
- Indentation: 4 spaces; UTF-8; LF line endings.
- Naming: PascalCase for types/methods; camelCase for locals/params; suffix async methods with `Async`.
- Minimal APIs: prefer clear route names (e.g., `.WithName("GetWeatherForecast")`) and small records/DTOs near usage or under `Man10BankService/Models/` when they grow.
- Formatting: run `dotnet format` before pushing (if installed).

## Testing Guidelines
- Framework: xUnit (recommended). Create `tests/Man10BankService.Tests/` with a parallelizable test project.
- Naming: `ClassName_MethodName_ShouldExpected` (file and method level).
- Run tests: `dotnet test` (from repo root) once a test project exists.
- Coverage (recommended): use `coverlet` or `dotnet test /p:CollectCoverage=true`.

## Commit & Pull Request Guidelines
- Commits: use concise, imperative messages; consider Conventional Commits (e.g., `feat: add account deposit endpoint`).
- PRs: include purpose, linked issues, and brief testing notes; add screenshots of API responses or OpenAPI diffs when relevant.
- CI-readiness: ensure `dotnet build` and (if present) `dotnet test` pass locally; no console warnings.

## Security & Configuration Tips
- Secrets: do not commit secrets; prefer environment variables or user secrets (`dotnet user-secrets`) for local dev.
- Profiles: app reads `appsettings.*.json` by environment; default is `Development` when using `dotnet run` locally.
- HTTPS: app enforces HTTPS redirection; use the dev certificate (`dotnet dev-certs https --trust`) if needed.
