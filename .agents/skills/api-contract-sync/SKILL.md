---
name: api-contract-sync
description: Validate and synchronize API schemas, request/response models, and TypeScript types between backend endpoints and frontend clients in BrewYou.
---

# API Contract Synchronization Skill

Use this skill to inspect, validate, and synchronize API contracts across backend endpoints and frontend client consumers.

---

## Workflow Steps

1. **Extract Backend Schemas**:
   - Inspect backend route handlers, controllers, and validation schemas (e.g. Zod, OpenAPI decorators, Pydantic).
   - Identify endpoint URIs, expected request parameters/body schemas, and returned data envelopes.

2. **Inspect Frontend Consumers**:
   - Locate client API service calls, data fetching hooks (e.g. `useQuery`), and TypeScript type interfaces.
   - Cross-check field names, nullability, optional properties, and enum values against backend definitions.

3. **Detect Discrepancies & Breaking Changes**:
   - Identify missing fields, type mismatches (e.g., string vs. number vs. Date), or outdated routes.
   - Check if response status codes or error envelope shapes diverge.

4. **Synchronize & Generate Types**:
   - Update client TypeScript interfaces or run type-generation tools (e.g., `openapi-typescript`, `trpc`, or shared schema packages).
   - Update mocked API fixtures (MSW handlers) to reflect schema changes.

5. **Verify Fullstack Parity**:
   - Run frontend type checking (`tsc --noEmit`) to confirm that all client code compiles cleanly against the synchronized contracts.
