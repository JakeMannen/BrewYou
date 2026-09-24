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
   - Run format check (e.g. `dotnet format --verify-no-changes` or frontend format checkers).

2. **Frontend Verification**:
   - Run static analysis and linting (`npm run lint` / `eslint`) with zero warnings allowed.
   - Run type-checking (`tsc --noEmit` or equivalent).
   - Execute frontend unit and component tests (`npm test` / `vitest run`).
   - Run production bundle build (`npm run build`) to verify zero bundling or dependency errors.

3. **Backend Verification**:
   - Run compiler and static analysis with zero warnings/errors (`dotnet build /warnaserror` or equivalent).
   - Execute unit tests for business/domain services (`dotnet test`).
   - Execute integration tests for API endpoints with clean database teardown.

4. **Integration & Smoke Verification**:
   - If contract tests or E2E smoke tests exist, execute them against the built packages.
   - Report a consolidated status matrix of all passing/failing suites.
