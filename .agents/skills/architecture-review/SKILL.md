---
name: architecture-review
description: Conduct an architectural audit evaluating tier boundaries, separation of concerns, scalability risks, and test readiness in BrewYou.
---

# Architecture Review Skill

Use this skill when auditing a proposed design, assessing system scalability, evaluating new dependencies, or checking test readiness prior to major releases.

---

## Workflow Steps

1. **Boundary & Coupling Audit**:
   - Verify that presentation logic is strictly decoupled from business transactions.
   - Confirm that server endpoints do not leak internal database schemas directly to the client without DTO transformation.
   - Detect any circular dependencies between modules or packages.

2. **API & Data Contract Evaluation**:
   - Check that all newly introduced endpoints follow RESTful conventions and uniform JSON response envelopes.
   - Verify error representation consistency and HTTP status code correctness.

3. **Performance & Scalability Assessment**:
   - Inspect database query patterns for potential N+1 query traps or missing indexes on filtered/sorted columns.
   - Check client bundle footprint, code-splitting, and memoization of expensive computations.

4. **Test Pyramid & QA Readiness Audit**:
   - Assess whether domain logic has corresponding unit test coverage.
   - Verify that integration tests cover happy path and error paths.
   - Confirm that tests are deterministic and isolated.

5. **Deliver Findings**:
   - Produce a concise summary of strengths, architectural risks, and prioritized remediation actions.
