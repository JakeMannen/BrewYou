---
name: project-overview
description: Inspect and summarize the BrewYou fullstack workspace structure, frontend and backend architectures, dependencies, and configurations.
---

# Fullstack Project Overview Skill

Use this skill when analyzing the layout, tech stack, API contracts, database configuration, and overall architecture of the BrewYou project.

---

## Workflow Steps

1. **Fullstack Architecture Discovery**:
   - Locate client application directories (e.g. `frontend/`, `client/`, `web/`, `src/app`).
   - Locate server application directories (e.g. `backend/`, `server/`, `api/`, `src/api`).
   - Identify shared packages, DTOs, or schema directories (e.g. `shared/`, `packages/types/`, `contracts/`).

2. **Technology Stack Mapping**:
   - **Frontend**: Framework (React, Next.js, Vue, Svelte), UI component libraries, state management, CSS approach.
   - **Backend**: Runtime (Node.js, Python, Go), web framework (Express, Fastify, FastAPI, NestJS), ORM/Query builder (Prisma, Drizzle, SQLAlchemy).
   - **Database**: Engine (PostgreSQL, SQLite, MySQL, Redis), migration tool.
   - **Testing**: Test runners (Vitest, Jest, PyTest, Playwright, Cypress).

3. **API & Contract Surface**:
   - Locate OpenAPI/Swagger specs, GraphQL schemas, or tRPC routers.
   - Check contract synchronization between client API services and server endpoints.

4. **Summary & Readiness**:
   - Report active services, environment setup prerequisites, and current state.
