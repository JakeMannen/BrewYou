# Coding Standards & Best Practices

Guidelines for writing clean, maintainable frontend and backend code in the BrewYou project.

---

## 1. Mandatory Linting & .editorconfig Enforcement

- **Strict Mandate**: **All files in the workspace MUST strictly adhere to [.editorconfig](../../.editorconfig) rules and pass all static linters with zero errors and zero warnings.**
- **Formatting Requirements**:
  - **Indentation**: Follow configured `indent_style` and `indent_size` per file type.
  - **Line Endings**: Maintain `end_of_line` consistency as defined in `.editorconfig`.
  - **Whitespace**: Trailing whitespace must be trimmed (`trim_trailing_whitespace = true`).
  - **Final Newline**: Adhere to `insert_final_newline` settings.
  - **C# / .NET Conventions**: Adhere to all Roslyn analyzer rules, modifier ordering, naming conventions, and expression preferences defined in `.editorconfig`.
- **Mandatory Verification Gates**:
  - **Backend / .NET**: Run formatting and linting verification (e.g. `dotnet format --verify-no-changes`).
  - **Frontend / JavaScript / TypeScript**: Run static linting (e.g. `npm run lint`, `eslint`) and typechecking (`tsc --noEmit`).
  - No file with unresolved lint warnings or formatting discrepancies may be committed or merged.

---

## 2. General Principles

- Write self-documenting code with clear, descriptive identifiers.
- Keep functions and methods small and focused on a single responsibility.
- Favor early returns over nested `if/else` structures.
- Preserve existing comments, docstrings, and type annotations.

---

## 3. Frontend Standards

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
- **Mandatory Internationalization & Localization (i18n)**:
  - Never hardcode user-visible strings in templates or components; use the translation helper `t(...)` from `$lib/i18n`.
  - Maintain 100% dictionary key parity between all defined languages (`en.json` and `sv.json`).
  - Use token interpolation (`{param}`) for dynamic variables instead of ad-hoc string concatenation.

---

## 4. Backend Standards

- **Layered Architecture**:
  - Maintain strict separation: **Controller** (HTTP routing & response codes) -> **Service** (business logic) -> **Repository** (data access).
  - Never execute raw database queries or ORM calls directly from controllers.
- **Input Validation & Sanitization**:
  - Validate all incoming payloads at the boundary using schema validators (e.g. Zod, Joi, Pydantic, FluentValidation).
  - Never trust user input; reject malformed requests with `400 Bad Request` and detailed field errors.
- **Asynchronous & Error Handling**:
  - Always handle asynchronous rejections/exceptions; never leave unhandled promise rejections or unobserved tasks.
  - Use centralized error handling middleware to catch uncaught exceptions and format consistent responses.

---

## 5. Dependencies & Secrets

- Do not introduce new dependencies without checking bundle size (frontend) or security audit (backend).
- Secrets, API keys, and database passwords must strictly reside in environment variables (`.env` or secret managers) and never be committed.
