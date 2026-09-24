# Contributing to BrewYou

Thank you for your interest in contributing to **BrewYou**! We welcome community contributions to help build the best 100% free and open-source craft brewing platform.

---

## Code of Conduct & Open Source Values

BrewYou is strictly open-source software built for homebrewers and craft breweries worldwide.

- **100% Free & Open**: We do not accept features, pull requests, or configurations that introduce paywalls, subscriptions, paid tiers, or feature gating. All capabilities must remain universally available to every brewer.
- **Respectful Collaboration**: Please treat fellow contributors with respect, kindness, and constructive communication.

---

## Contributor Licensing Terms (Inbound = Outbound)

By contributing to BrewYou, you agree that your contributions (including code, documentation, translations, tests, and assets) will be licensed under the terms of the project's [MIT License](LICENSE).

You represent that:
1. You have the legal right to submit the contribution.
2. The contribution is your original creation or provided under a compatible permissive open-source license.
3. You agree to grant the project and its users all rights conveyed by the MIT License without additional restrictions.

---

## Development Workflow

We follow standard **GitHub Flow**:

1. **Fork or Branch**: Create a feature or bugfix branch from `main`:
   ```bash
   git checkout -b feature/your-feature-name
   ```
2. **Commit Conventions**: Use conventional, descriptive commit messages:
   - `feat: add pre-boil water calculator adjustment`
   - `fix: correct Tinseth hop boil time curve parameter`
   - `docs: update API endpoint documentation`
3. **Automated Verification**: Ensure all linters, formatting checks, and automated tests pass with zero warnings and zero errors prior to opening a PR:
   ```bash
   # Backend formatting and tests
   dotnet format --verify-no-changes
   dotnet test

   # Frontend formatting, linting, and checks
   cd src/BrewYou.Web
   npm run lint
   npm run check
   npm test
   ```
4. **Open a Pull Request**: Submit your pull request against the `main` branch with a clear summary of changes, rationale, and testing evidence.

---

## Architectural Principles & Tier Boundaries

Contributors must uphold our architectural boundaries:

- **Frontend (`src/BrewYou.Web`)**: SvelteKit 2 and Svelte 5 (runes-based). Responsible for UI state, interactive controls, and client-side presentation. Never embed server secrets or direct database access.
- **Backend (`src/BrewYou.ApiService`)**: ASP.NET Core Minimal APIs with EF Core / PostgreSQL. Enforces domain invariants, database persistence, and authorization rules.
- **Internationalization (i18n)**: Zero hardcoded strings in UI components. Every new UI string must be registered in both `src/BrewYou.Web/src/lib/i18n/locales/en.json` and `sv.json`.
- **Test Coverage**: All new business logic, calculators, and API endpoints must include automated test coverage.

---

## Questions and Support

Feel free to open an issue or start a discussion on the repository if you have questions or want to discuss feature designs before submitting a PR. Cheers and happy brewing!
