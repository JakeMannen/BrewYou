# AGENTS.md — BrewYou Workspace Instructions

Welcome to the **BrewYou** fullstack craft brewing workspace. This file provides authoritative instructions, architectural boundaries, and conventions for AI agents operating in this repository.

---

## 1. Project Orientation & Scope

- **Workspace**: `BrewYou`
- **Objective**: Fullstack craft brewing application (SvelteKit client frontend, ASP.NET Core Web API backend, EF Core / PostgreSQL data layer, Aspire orchestration).
- **Scope Restriction**: All file operations, command executions, and search activities must strictly remain within the `BrewYou` workspace root.

---

## 2. Specialized Roles, Skills & Mandatory Activation Gates

To guarantee domain correctness and architectural integrity, the agent **MUST** activate specialized skills and consult domain roles before touching production code.

### A. Skill & Role Activation Matrix

| Domain / Task Trigger | Required Skill | Persona / Role File | Mandatory Pre-Implementation Action |
| :--- | :--- | :--- | :--- |
| **Recipe handling, BeerXML/BeerJSON, batch calculations, BJCP style validation, mash/hop schedules** | `brewing-recipe-schemas` | [master-brewer.md](.agents/master-brewer.md) | **MUST** read the skill instructions and verify brewing logic/equations against BJCP guidelines before writing code. |
| **New or modified API endpoints, DTO changes, schema updates** | `api-contract-sync` | [api-contract-reviewer.md](.agents/api-contract-reviewer.md) | **MUST** verify synchronization between backend C# DTOs and frontend TypeScript types. |
| **Cross-cutting system design, database modeling, tier boundaries** | `architecture-review` | [software-architect.md](.agents/software-architect.md) | **MUST** conduct architectural review and produce an implementation plan before making modifications. |
| **Aspire AppHost, container orchestration, service discovery** | `aspire` / `aspire-orchestration` | [software-architect.md](.agents/software-architect.md) | **MUST** check Aspire configuration parity with `docker-compose.yml`. |
| **Database schema migrations, seed data changes, user-scoped domain models, relational mutations** | `db-migrate` | [backend-developer.md](.agents/backend-developer.md) | **MUST** follow migration safe-practices, verify cascading and ripple effects on ancestral and dependent entities (deletions, renames, moves), and update User Backup & Restore (`BackupContracts`, `BackupMigrationPipeline`, `BackupService`) to preserve backwards compatibility. |
| **Designing test suites, integration tests, mock harnesses** | `build-and-test` | [test-architect.md](.agents/test-architect.md) | **MUST** ensure test coverage meets project quality standards. |
| **Authentication, authorization, tokens, secrets, security headers** | — | [cybersecurity-expert.md](.agents/cybersecurity-expert.md) | **MUST** perform OWASP threat-check on new inputs/endpoints. |

### B. Mandatory Execution Rules for Skills & Subagents

1. **Pre-Flight Skill Check**:
   - Whenever a task touches a domain listed in the matrix above, the agent **MUST** invoke `view_file` on the corresponding `SKILL.md` (or define/invoke the relevant subagent) **BEFORE** editing any source files.
   - *Example*: Modifying recipe import/export or calculations without first inspecting `brewing-recipe-schemas` is considered a protocol violation.

2. **Domain Separation & Persona Adoption**:
   - For brewing domain questions and recipe mechanics, strictly adopt the **Master Brewer** perspective: brewing calculations (IBU, SRM, ABV, gravity temperature adjustments) must be mathematically sound, unit-safe (metric primary), and BJCP 2021 compliant.
   - For backend/frontend separation, adhere strictly to the **Software Architect** and **API Contract Reviewer** guidelines.

3. **When Direct Implementation is Allowed**:
   - Direct edits without prior skill consultation are strictly limited to: typo fixes, styling adjustments (CSS/Tailwind), minor text localization updates, or fixing isolated compile/lint errors in existing files.

---

## 3. Mandatory Invariants & Operational Rules

### A. Architectural Boundaries & Tier Separation

- **Frontend (Client)**: Presentation, UI state, and client-side validation only. Never embed direct database access, server secrets, or raw business calculations.
- **Backend (Server)**: Controllers/Endpoints (transport & validation) -> Services (domain & business logic) -> Repositories/EF Core (data access).
- **Contracts**: Request/response DTOs and types must remain synchronized between client and server.

### B. Mandatory Linting & .editorconfig Enforcement

- **Strict Rule**: Formatting and static analysis compliance is **mandatory** across all files.
- All created and modified files must strictly comply with [.editorconfig](.editorconfig).
- Verification (`dotnet format --verify-no-changes`, `npm run lint`, `eslint`) must pass with **zero warnings and zero errors**.

### C. Mandatory Test Coverage for New Features

- **Strict Rule**: Every new feature or endpoint **MUST** be covered by automated unit and/or integration tests.
- Bug fixes must be verified with regression tests.

### D. Planning & GitHub Flow

- For cross-tier features, maintain an explicit plan before modifying code.
- **Mandatory Branching Rule**: All new features and tasks **MUST** start from a new branch created from the latest remote `main` (`git fetch origin`, `git checkout main`, `git pull origin main`, or `git checkout -b <branch-name> origin/main`). Never commit new feature work directly to an outdated branch or `main`.
- **Branch Naming & PR Rules**: All stated branch naming rules (`feat/<short-title>`, `fix/<short-title>`, etc.) and PR requirements (completing [.github/pull_request_template.md](.github/pull_request_template.md), passing CI gates, and squash-merging) strictly apply. Adhere to [git-workflow.md](.agents/rules/git-workflow.md) and [`github-flow`](.agents/skills/github-flow/SKILL.md).

### E. Execution Safety & Code Integrity

- Run commands with non-destructive arguments; preserve existing comments, annotations, and formatting.
- Avoid deleting files or clearing database records without explicit user approval.

### F. Mandatory Docker Compose Stack Integrity

- **Strict Rule**: `docker-compose.yml` and container Dockerfiles must be kept operational and buildable on all stack changes.
- **Parity with Aspire**: Service names (`postgres`, `apiservice`, `web`), ports (`5432`, `5000`, `3000`), and database identifiers (`brewyou-db`) must mirror [AppHost.cs](src/BrewYou.AppHost/AppHost.cs).
- **Dual-Networking Awareness**: SvelteKit SSR connects internally (`http://apiservice:5000`), while browser clients connect via host ports (`http://localhost:5000` via `PUBLIC_API_URL`). Maintain parity for both.
- **Security**: Non-root execution (`USER $APP_UID` in .NET, `USER node` in SvelteKit); bind ports to loopback (`127.0.0.1:<port>:<container_port>`).
- Health readiness dependencies (`condition: service_healthy`) are strictly required.

### G. Absolute Prohibition of Pro, Paid, or Subscription Tiers

- **Strict Invariant**: BrewYou is and permanently remains **100% free and open** craft brewing software.
- **Prohibited**: NEVER introduce Pro, Premium, Paid, Subscription, Tiered, Paywalled, Monetization, or Billing concepts, badges, gating, features, database models, or endpoints. All features are universally accessible to every user.

### H. Mandatory Internationalization & Localization (i18n)

- **Zero Hardcoded Strings**: All user-visible UI strings must use the localization engine (`t(...)` from `$lib/i18n`).
- **100% Key Parity**: New keys **must** be defined simultaneously in both [en.json](src/BrewYou.Web/src/lib/i18n/locales/en.json) and [sv.json](src/BrewYou.Web/src/lib/i18n/locales/sv.json).
- Use parameter interpolation (`{param}`) for dynamic text.

### I. Mandatory README Maintenance

- **Strict Rule**: The root [README.md](README.md) must be kept up to date whenever new features, endpoints, configuration options, or architectural changes are introduced.

### J. Mandatory User Backup & Restore Backwards Compatibility

- **Strict Rule**: Whenever a new feature adds or modifies user-scoped entities, child collections, user preferences/settings, equipment/IoT configurations, or collaboration structures, the User Backup & Restore system **MUST** be updated in lockstep while preserving full backwards compatibility with backups created on older versions.
- **Complete Backup Coverage**: Update [BackupContracts.cs](src/BrewYou.ApiService/Backup/Models/BackupContracts.cs), [BackupService.cs](src/BrewYou.ApiService/Services/BackupService.cs), and [api.ts](src/BrewYou.Web/src/lib/types/api.ts) so new user-owned data and preferences are exported, previewed, and restored across all conflict resolution modes (`MergeSkipExisting`, `MergeOverwriteExisting`, `ReplaceAll`).
- **Backwards-Compatible Schema Evolution**:
  - New properties on existing backup DTOs **must** be optional/nullable with safe domain defaults so older `.brewyou.json` files missing those properties deserialize and restore without errors.
  - When renaming, splitting, or restructuring backup fields or introducing breaking schema changes, increment `BackupSchemaConstants.CurrentSchemaVersion` and add a sequential `IBackupSchemaMigration` upcaster step (`FromVersion -> ToVersion`) in [BackupMigrationPipeline.cs](src/BrewYou.ApiService/Backup/Versioning/BackupMigrationPipeline.cs).
- **Zero-Plaintext Secret Protection**: Any new credentials, tokens, or passwords added to user settings or equipment **must** be encrypted via [BackupSecretProtector.cs](src/BrewYou.ApiService/Backup/Security/BackupSecretProtector.cs) and never exported in plaintext.
- **Mandatory Regression Testing**: Extend [BackupEndpointsTests.cs](tests/BrewYou.ApiService.Tests/BackupEndpointsTests.cs) to verify both round-trip export/restore of the new feature data and backwards-compatible restoration of legacy schema versions (`v0`, `v1`, `v2`, etc.) onto the current version.

### K. Mandatory Cascading & Relational Integrity (Ancestral Ripple Effects)

- **Strict Invariant**: All new database-affecting features, schema changes, entity relationships, and bug fixes **MUST** comprehensively analyze and handle cascading and ripple effects across the entire entity hierarchy, with particular rigor applied to **ancestral (parent/owner)** objects.
- **Scope of Entity Mutations**: Whenever any entity (e.g., equipment instance, vessel, tap, keg, ingredient, recipe, or equipment profile) is deleted, renamed, reordered, or moved:
  - **Ancestral & Parent Object Reconciliation**:
    - The ripple effect on ancestral or parent objects referencing or embedding that entity must be handled explicitly and gracefully.
    - *Example*: Removal of an equipment instance must cleanly reconcile other equipment and parent setups (such as multi-vessel brewery systems, assigned HLT, Mash Tun, Boiler, Fermenter, or Packaging Vessel roles, tap-keg linkages, and sensor assignments) without leaving broken configurations, orphaned records, or dangling foreign keys.
  - **Relational Integrity & Constraint Violation Defense**:
    - Never rely solely on database-level triggers or implicit cascade rules, as EF Core in-memory test harnesses and relational database engines differ. Explicitly clean up or decouple join entities (e.g. `BatchSensorAssignment`), set foreign keys safely, or enforce domain guards.
    - When an entity is actively in use by upstream or downstream processes (e.g., equipment or recipe in an active batch), guard against destructive deletion with a `409 Conflict` unless an explicit force flag or decoupled snapshotting mechanism (e.g., snapshotting `RecipeName` onto batches) is provided.
  - **Renaming, Moving & Reassignment Integrity**:
    - Renaming an entity must preserve historical references and parent associations across all related entities, APIs, and UI views.
    - Moving or reassigning an entity within a hierarchy (e.g., moving a vessel to a different slot, reordering equipment profiles, or moving taps between locations) must update all ancestral references atomically and consistently.
  - **User Backup & Restore Parity**:
    - The User Backup & Restore pipeline (`BackupService`, `BackupContracts`) must sanitize or remap ancestral and foreign references upon restore so that missing, renamed, or moved entities do not produce dangling GUIDs or relational inconsistencies.
  - **Mandatory Regression & Adversarial Testing**:
    - Automated unit and integration tests must explicitly test lifecycle transitions (deletions, renamings, and moves) against ancestral and dependent objects to guarantee no orphaned records or unhandled foreign key exceptions occur.

---

## 4. References

- Detailed rules: [`.agents/rules/`](.agents/rules/) (`architecture-principles.md`, `code-standards.md`, `api-guidelines.md`, `security-guidelines.md`, `testing-and-qa.md`, `git-workflow.md`).
- Workspace skills: [`.agents/skills/`](.agents/skills/).

---

## 5. Verification Protocol

1. Verify linting, static analysis, and `.editorconfig` compliance across touched files (zero errors/warnings).
2. Run relevant unit and integration test suites.
3. Confirm contract alignment across frontend and backend.
4. Verify User Backup & Restore coverage and backwards compatibility (`BackupEndpointsTests`) when user-scoped models or settings are touched.
5. Verify cascading and relational integrity on ancestral and dependent objects (test deletions, renames, reassignments, and moves for zero orphaned data or foreign key violations).
6. Verify full i18n key parity across `en.json` and `sv.json` (and all supported locales).
7. Verify container configuration parity (`docker compose config`).
8. Confirm that [README.md](README.md) reflects any new capabilities, endpoints, or setup instructions introduced.
9. Present a concise summary of changes and verification results.
