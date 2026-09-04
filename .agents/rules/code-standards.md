# Coding Standards & Best Practices

Guidelines for writing clean, maintainable frontend and backend code in the BrewYou project.

---

## 1. General Principles

- Write self-documenting code with clear, descriptive identifiers.
- Keep functions and methods small and focused on a single responsibility.
- Favor early returns over nested `if/else` structures.
- Preserve existing comments, docstrings, and type annotations.

---

## 2. Frontend Standards

- **Component Design**:
  - Keep components modular, focused, and small.
  - Separate presentational/stateless components from container/stateful components.
  - Co-locate component styles, unit tests, and types in the component folder.
- **State Management**:
  - Distinguish between local UI state (modal open/closed, form input) and server state (cached API responses).
  - Use dedicated data fetching/caching libraries (e.g., TanStack Query, SWR) instead of manual `useEffect` fetching.
  - Ensure immutability when updating state objects or arrays.
- **Accessibility & Responsive Layout**:
  - Use semantic HTML elements (`<main>`, `<nav>`, `<article>`, `<button>`).
  - Ensure WCAG 2.1 AA compliance (keyboard focusable controls, appropriate ARIA roles, label associations).
  - Design mobile-first with responsive breakpoints.

---

## 3. Backend Standards

- **Layered Architecture**:
  - Maintain strict separation: **Controller** (HTTP routing & response codes) -> **Service** (business logic) -> **Repository** (data access).
  - Never execute raw database queries or ORM calls directly from controllers.
- **Input Validation & Sanitization**:
  - Validate all incoming payloads at the boundary using schema validators (e.g. Zod, Joi, Pydantic).
  - Never trust user input; reject malformed requests with `400 Bad Request` and detailed field errors.
- **Asynchronous & Error Handling**:
  - Always handle asynchronous rejections/exceptions; never leave unhandled promise rejections.
  - Use centralized error handling middleware to catch uncaught exceptions and format consistent responses.

---

## 4. Dependencies & Secrets

- Do not introduce new dependencies without checking bundle size (frontend) or security audit (backend).
- Secrets, API keys, and database passwords must strictly reside in environment variables (`.env`) and never be committed.
