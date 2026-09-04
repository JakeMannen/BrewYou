---
name: build-and-test
description: Build, lint, and run multi-tier test suites across the BrewYou frontend, backend, and fullstack integration workflows.
---

# Multi-Tier Build & Test Skill

Use this skill when compiling the project, executing test suites across frontend and backend tiers, running static analysis/linters, and verifying fullstack integration.

---

## Workflow Steps

1. **Pre-Flight Validation**:
   - Check required node/python/runtime environments and environment variable files (`.env.test`).
   - Validate database connectivity for test runners if integration tests require a test database.

2. **Frontend Verification**:
   - Run type-checking (`tsc --noEmit` or equivalent).
   - Run linter/formatting checks (`npm run lint`).
   - Execute frontend unit and component tests (`npm test` / `vitest run`).
   - Run production bundle build (`npm run build`) to verify zero bundling or dependency errors.

3. **Backend Verification**:
   - Run static analysis and type checks.
   - Execute unit tests for business/domain services.
   - Execute integration tests for API endpoints with clean database teardown.

4. **Integration & Smoke Verification**:
   - If contract tests or E2E smoke tests exist, execute them against the built packages.
   - Report a consolidated status matrix of all passing/failing suites.
