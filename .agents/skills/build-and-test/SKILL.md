---
name: build-and-test
description: Build, lint, and run multi-tier test suites across the BrewYou frontend, backend, and fullstack integration workflows.
---

# Multi-Tier Build & Test Skill

Use this skill when compiling the project, executing test suites across frontend and backend tiers, running static analysis/linters, and verifying fullstack integration.

---

## Workflow Steps

1. **Pre-Flight & .editorconfig Validation**:
   - Check required runtimes (.NET SDK, Node.js, etc.).
   - Verify compliance with [.editorconfig](../../../.editorconfig) rules.
   - On Windows in sandboxed environments, `dotnet format` requires `BypassSandbox: true` because Roslyn's `BuildHost` uses `NamedPipeClientStream` IPC (`dotnet format src/BrewYou.ApiService/BrewYou.ApiService.csproj --verify-no-changes --no-restore` and `dotnet format tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --verify-no-changes --no-restore`).

2. **Frontend Verification**:
   - On Windows with `nvm4w` (`C:\nvm4w\nodejs`), Node/npm are outside the sandbox, so run frontend commands with `BypassSandbox: true` and `Push-Location src/BrewYou.Web` (avoid `npm --prefix ... exec -- <tool> <relative-path>`, which resolves relative file paths from the repository root instead of `src/BrewYou.Web`):
     - Type-checking: `npm run check` (`svelte-kit sync && svelte-check`)
     - Formatting & linting: `npm run lint` (`prettier --check . && eslint .`) with zero warnings allowed
     - Unit & component tests: `npm run test:unit -- --run` (`vitest --run`)
     - Production bundle build: `npm run build`

3. **Backend Verification**:
   - Build the test project single-proc inside the sandbox to avoid multi-proc MSBuild `_GetProjectReferenceTargetFrameworkProperties` failures:
     `dotnet build tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --no-restore -m:1`
   - Execute backend unit & integration tests with `--no-build` (and `BypassSandbox: true` on Windows if `vstest.console` cannot connect to `testhost` over sandboxed loopback TCP):
     `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --no-build`

4. **Integration & Smoke Verification**:
   - If contract tests or E2E smoke tests exist, execute them against the built packages.
   - Report a consolidated status matrix of all passing/failing suites.
