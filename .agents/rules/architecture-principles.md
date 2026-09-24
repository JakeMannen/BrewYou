# Architecture Principles & System Design

Core design standards for fullstack development in the BrewYou project, curated by the **Software Architect**.

---

## 1. Subagent Consultation on New Features
 
- **Targeted Delegation Policy**: Involve specialized subagents when tasks require deep focus in their respective domain:
  - **Software Architect**: System design, tier boundaries, domain models.
  - **Frontend Developer**: UI components, state management, accessibility, client routing.
  - **Backend Developer**: API handlers, database schema, transactional logic, services.
  - **API Contract Reviewer**: Contract synchronization, request/response models, uniform envelopes.
  - **Test Architect**: Test pyramid strategy, coverage requirements, mock harness design.
  - **Cybersecurity Expert**: Threat modeling, OWASP Top 10 defense, authorization boundaries, secret hygiene.
  - **Master Brewer**: Brewing domain expertise (grains, hops, yeast, water chemistry, BJCP styles, brewing calculations). (Does not write or comment on software code.)
- Straightforward features, bug fixes, or minor tweaks should be handled directly by the primary agent without unnecessary subagent context overhead.

---

## 2. Separation of Concerns & Tier Boundaries

- **Presentation Layer (Frontend)**:
  - Focuses on user interaction, state reflection, routing, and accessible UI.
  - Must never contain business transaction rules, database queries, or server environment secrets.
- **Application & Domain Layer (Backend)**:
  - Houses business models, domain invariants, authentication, authorization, and process workflows.
  - Exposes deterministic, typed APIs to consumers.
- **Data Access Layer**:
  - Encapsulates database queries, ORM mapping, connection pools, and migration scripts.
  - Repositories should return typed domain models or DTOs, keeping SQL/query logic out of controllers and services.

---

## 3. Clean Architecture & SOLID Principles

- **Single Responsibility (SRP)**: Each module, service, and component should have only one reason to change.
- **Open/Closed (OCP)**: Write extensible code through interfaces and polymorphism rather than modifying stable core modules.
- **Dependency Inversion (DIP)**: Depend upon abstractions (interfaces/contracts), not concrete implementations.
- **DRY vs. Decoupling**: Do not prematurely share code across tiers if sharing creates tight coupling between presentation and domain logic. Shared models should be restricted to contracts and DTOs.

---

## 4. Containerization & Stack Topology Parity

- **Topology Parity**: The Docker Compose service layout must remain identical in service names, port assignments, and dependency topology to the Aspire AppHost orchestration model.
- **Operational Stack Integrity**: Any architectural change that introduces a new dependency, alters inter-service communication protocols, adds environment variables, or shifts port bindings must concurrently update `docker-compose.yml` and corresponding container `Dockerfile`s.
- **Dual-Networking Consideration**: SvelteKit SSR processes execute container-to-container (`http://apiservice:5000`), whereas user browser sessions connect via host network mappings (`http://localhost:5000`). Architecture designs must accommodate both pathways without leaking internal container hostnames to client browsers.

---

## 5. Absolute Prohibition of Pro, Paid, or Subscription Tiers

- **Strict Invariant**: The BrewYou project is, and will permanently remain, a **100% free and open** craft brewing platform.
- **Prohibited Concepts**: NEVER introduce Pro, Premium, Paid, Subscription, Tiered, Paywalled, Monetization, or Billing concepts, badges, gating, features, database models, or endpoints into BrewYou.
- All brewing features (recipe formulation, BJCP styles, inventory management, fermentation logging, gauges, calculators, batch management) are universally and unconditionally accessible to every user without restrictions or tier differentiation.
