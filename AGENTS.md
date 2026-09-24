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
| **Database schema migrations, seed data changes** | `db-migrate` | [backend-developer.md](.agents/backend-developer.md) | **MUST** follow migration safe-practices and rollback verification. |
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

---

## 4. References

- Detailed rules: [`.agents/rules/`](.agents/rules/) (`architecture-principles.md`, `code-standards.md`, `api-guidelines.md`, `security-guidelines.md`, `testing-and-qa.md`, `git-workflow.md`).
- Workspace skills: [`.agents/skills/`](.agents/skills/).

---

## 5. Verification Protocol

1. Verify linting, static analysis, and `.editorconfig` compliance across touched files (zero errors/warnings).
2. Run relevant unit and integration test suites.
3. Confirm contract alignment across frontend and backend.
4. Verify full i18n key parity across `en.json` and `sv.json`.
5. Verify container configuration parity (`docker compose config`).
6. Confirm that [README.md](README.md) reflects any new capabilities, endpoints, or setup instructions introduced.
7. Present a concise summary of changes and verification results.
