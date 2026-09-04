# AGENTS.md — BrewYou Workspace Instructions

Welcome to the **BrewYou** fullstack project workspace. This file provides authoritative instructions, architectural boundaries, and conventions for AI agents operating within this repository.

---

## 1. Project Orientation & Scope

- **Workspace**: `BrewYou`
- **Objective**: Fullstack application development encompassing client applications (frontend), server APIs & services (backend), data modeling, and robust automated verification following **GitHub Flow**.
- **Scope Restriction**: All file operations, command executions, and search activities must strictly remain within the `BrewYou` workspace root unless explicitly authorized by the user.

---

## 2. Specialized Agent Roles & Delegation Matrix

The workspace defines specialized subagents under [`.agents/`](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/) to handle complex fullstack disciplines:

| Agent Role | Definition File | Primary Focus |
| :--- | :--- | :--- |
| **Software Architect** | [software-architect.agent.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/software-architect.agent.md) | High-level system design, domain modeling, ADRs, tier boundaries, scalability |
| **Test Architect** | [test-architect.agent.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/test-architect.agent.md) | Test pyramid strategy, test runners, coverage gates, mock harness, regression defense |
| **Frontend Developer** | [frontend-developer.agent.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/frontend-developer.agent.md) | UI components, state management, client routing, styling, responsive design, a11y |
| **Backend Developer** | [backend-developer.agent.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/backend-developer.agent.md) | Server endpoints, business logic, DB models, migrations, auth, and backend testing |
| **API Contract Reviewer**| [api-contract-reviewer.agent.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/api-contract-reviewer.agent.md) | OpenAPI specs, schema synchronization, shared DTOs/types, error response parity |

---

## 3. Operational Guidelines

### A. Architectural Boundaries & Tier Separation
- **Frontend (Client)**: Strictly presentation, user interaction, and client-side validation. Never embed direct database calls, secret API keys, or raw business logic in client code.
- **Backend (Server)**: Follow clean layering:
  - **Controllers/Routers**: Transport handling, request validation, HTTP status mapping.
  - **Services**: Pure business rules, orchestration, domain logic.
  - **Repositories/Data Access**: Database queries, ORM interactions, transaction boundaries.
- **Shared / Contracts**: API request/response schemas, DTOs, and enum types should be synchronized across frontend and backend.

### B. Planning & GitHub Flow
- For cross-tier features (modifying both frontend and backend), establish an explicit implementation plan before making modifications.
- Adhere strictly to **GitHub Flow**: branch from `main`, use conventional commits, verify locally, open a PR using [.github/pull_request_template.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.github/pull_request_template.md), ensure CI passes, and squash-merge.
- Significant architectural choices must be documented in ADRs (Architectural Decision Records) following guidance from the **Software Architect**.

### C. Execution Safety & Code Integrity
- Run commands with non-destructive arguments.
- Avoid deleting files or clearing database records without explicit user approval.
- Preserve existing comments, type annotations, and docstrings.

---

## 4. Modular Rules Reference

All agents must adhere to the rules in [`.agents/rules/`](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/rules/):
- [architecture-principles.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/rules/architecture-principles.md): Clean architecture, separation of concerns, SOLID, and domain design.
- [code-standards.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/rules/code-standards.md): Code style, readability, frontend/backend conventions, and error handling.
- [api-guidelines.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/rules/api-guidelines.md): RESTful endpoint conventions, HTTP status codes, uniform JSON envelopes, and versioning.
- [security-guidelines.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/rules/security-guidelines.md): Authentication, RBAC, input sanitization, injection prevention, CORS, and CSRF.
- [testing-and-qa.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/rules/testing-and-qa.md): Multi-tier test pyramid, isolation, mock harnesses, and regression testing.
- [git-workflow.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/rules/git-workflow.md): GitHub Flow lifecycle, conventional commit format, branch hygiene, and PR review standards.

---

## 5. Workspace Skills & Workflows

Workspace skills are located under [`.agents/skills/`](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.agents/skills/):
- **`/github-flow`**: Orchestrate complete GitHub Flow lifecycle: branching, committing, PR creation, CI checking, and merging.
- **`/pr-review`**: Perform automated self-review or peer review on PR diffs against fullstack quality criteria.
- **`/project-overview`**: Inspect fullstack project structure, dependencies, frameworks, and architecture.
- **`/build-and-test`**: Run cross-tier builds, static linter checks, and test suites.
- **`/architecture-review`**: Conduct architectural integrity, boundary separation, and test readiness audits.
- **`/api-contract-sync`**: Validate and synchronize API contracts and TypeScript types between frontend and backend.
- **`/db-migrate`**: Safely apply, rollback, and verify database schema migrations.

---

## 6. Verification Protocol

After implementing code changes:
1. Verify syntax and static analysis / linting across both frontend and backend codebases.
2. Run relevant unit and integration test suites.
3. Verify that API contracts match across tiers with zero type discrepancies.
4. Perform sanity checks to verify expected behavior without regressions.
5. Provide a clear, concise walkthrough of changes made and verification results.
