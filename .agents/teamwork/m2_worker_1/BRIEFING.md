# BRIEFING — 2026-11-04T09:50:30Z

## Mission
Implement Milestone 2: Recipe Lifecycle, Batch RecipeName Snapshot & Active Batch Safeguards across Backend, Frontend, Backup/Restore, i18n, and Tests.

## 🔒 My Identity
- Archetype: worker
- Roles: implementer, qa, specialist
- Working directory: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m2_worker_1\
- Original parent: d80eeb54-d338-4b53-876e-3933dc2f1932
- Milestone: Milestone 2 (Recipe Lifecycle, Batch RecipeName Snapshot & Active Batch Safeguards)

## 🔒 Key Constraints
- Integrity Mandate: No hardcoded test results, genuine logic, real state and behavior.
- Fullstack consistency: C# backend, PostgreSQL/EF Core entity mapping, SvelteKit frontend, TypeScript DTOs.
- Backup & Restore backwards compatibility: safe optional/nullable schema additions, round-trip tests.
- i18n key parity across en, sv, de, fr, es locales.
- Automated tests covering active batch safeguards (409 Conflict, force=true 200, dynamic recipe renaming, backup round-trip, frontend delete modal).
- 0 lint/format warnings.

## Current Parent
- Conversation ID: d80eeb54-d338-4b53-876e-3933dc2f1932
- Updated: 2026-11-04T09:50:30Z

## Task Summary
- **What to build**: Milestone 2 fullstack changes (Batch.RecipeName snapshot, Recipe delete active-batch safeguard, backup/restore support, frontend delete modal + active batches API, fallback bottle label print, batch creation fallback, comprehensive test suites).
- **Success criteria**: All automated tests pass, zero lint/format errors, genuine fullstack implementation.
- **Interface contracts**: PROJECT.md, SCOPE.md, BackupContracts, api.ts.
- **Code layout**: src/BrewYou.ApiService, src/BrewYou.Web, tests/BrewYou.ApiService.Tests.

## Change Tracker
- **Files modified**: None yet
- **Build status**: Not run yet
- **Pending issues**: None

## Quality Status
- **Build/test result**: Not run yet
- **Lint status**: Not run yet
- **Tests added/modified**: TBD

## Loaded Skills
- Source: .agents/skills/api-contract-sync/SKILL.md
  - Local copy: .agents/teamwork/m2_worker_1/skills/api-contract-sync.md
  - Core methodology: API contract inspection, schema synchronization between backend and frontend DTOs.
- Source: .agents/skills/db-migrate/SKILL.md
  - Local copy: .agents/teamwork/m2_worker_1/skills/db-migrate.md
  - Core methodology: Non-breaking database schema changes, User Backup & Restore backwards compatibility.
- Source: .agents/skills/build-and-test/SKILL.md
  - Local copy: .agents/teamwork/m2_worker_1/skills/build-and-test.md
  - Core methodology: Multi-tier build, lint, and test execution with zero error/warning tolerance.

## Key Decisions Made
- Follow explorer specifications exactly: snapshot RecipeName on Batch creation and deletion, implement active batch check returning 409 Conflict with code `RECIPE_IN_USE_BY_ACTIVE_BATCHES`, support `?force=true` query parameter, implement fallback bottle label canvas, update backup/restore with safe optional RecipeName.

## Artifact Index
- DISPATCH.md — Assignment instructions
- progress.md — Liveness heartbeat and step tracking
- handoff.md — Final 5-component handoff report
