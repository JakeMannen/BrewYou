# Testing and Quality Assurance Rules

Testing policies and verification standards across frontend and backend tiers in BrewYou, overseen by the **Test Architect**.

---

## 1. Mandatory Test Coverage for New Features

- **Strict Mandate**: **Every new feature MUST be covered by unit and/or integration tests.**
- No code implementing a new capability or endpoint will be approved or merged into `main` without automated tests demonstrating correctness.
- The **Test Architect** must be consulted during the design and planning of any new feature to establish the required test suites and scenarios.
- Coverage requirements:
  - **Happy Path**: Verifying expected results with standard inputs.
  - **Error Paths & Edge Cases**: Malformed inputs, missing fields, permission denials, boundary conditions.

---

## 2. Test Pyramid & Coverage Expectations

1. **Unit Tests (High volume, fast execution)**:
   - **Frontend**: Custom hooks, utility helpers, business calculations, pure reducers.
   - **Backend**: Service domain logic, algorithmic calculations, utility formatters, validation schemas.
   - Target: **>=85% line coverage** and **>=70% branch coverage** across backend services and core domain logic (`BrewYou.ApiService.Services`), with generated OpenAPI compiler artifacts explicitly excluded.

2. **Component & Integration Tests (Moderate volume, high confidence)**:
   - **Frontend**: Test UI components interacting with user events using Testing Library. Mock network responses via Mock Service Worker (MSW) or equivalent; avoid mocking internal component implementations.
   - **Backend**: Test API endpoints from HTTP router to database. Run tests against a fast test database (e.g. SQLite in-memory or transactional test container), rolling back transactions after each test.

3. **End-to-End & Contract Tests (Selective, critical user journeys)**:
   - Verify critical workflows: user onboarding, checkout/brewing pipeline, authentication flows.
   - Verify that API contract changes do not break frontend builds or client consumers.

---

## 3. Test Quality & Determinism

- **Zero Flakiness**: Tests must not contain arbitrary `sleep()` or timeout calls. Use async waiting matchers (e.g., `findBy*` or `waitFor()`).
- **Isolation**: Every test must set up its own state and cleanly tear down. Tests must pass regardless of execution order.
- **Mocking Policy**: Mock external 3rd-party services (payment gateways, email providers) at the network or client boundary, never domain logic.

---

## 4. Bug Fixes & Regression Defense

- For any bug fix, author a test that reproduces the failure prior to fixing the bug.
- Confirm that the test fails before applying the patch, and passes cleanly after the patch.
