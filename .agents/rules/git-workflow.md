# Git Workflow & GitHub Flow Guidelines

Rules and conventions for version control, branching, and pull request management in BrewYou.

---

## 1. GitHub Flow Principles

The BrewYou repository strictly follows **GitHub Flow**:

1. **`main` is Always Deployable**:
   - The `main` branch must never be broken.
   - Direct commits and pushes to `main` are strictly forbidden.
2. **Branch from `main`**:
   - Always create a new descriptive topic branch starting from the latest remote `main`.
   - Branch naming format: `<type>/<short-description>`
     - `feat/recipe-search`
     - `fix/sensor-reading-null`
     - `refactor/auth-middleware`
     - `docs/api-contracts`
3. **Commit Regularly with Conventional Format**:
   - Write clear, atomic commits using Conventional Commits: `<type>(<scope>): <summary>`
   - Scopes: `frontend`, `backend`, `api`, `db`, `arch`, `test`, `ci`
4. **Open a Pull Request Early**:
   - Open a PR as soon as work is ready for feedback or testing.
   - Fill out all sections of [.github/pull_request_template.md](file:///c:/Users/jocke/AgentWorkspaces/BrewYou/.github/pull_request_template.md).
5. **Continuous Integration Gate**:
   - All automated CI checks (linting, typechecking, tests, build) must pass before a PR can be merged.
   - Address any review comments or failing tests on the branch.
6. **Squash and Merge**:
   - Merge approved PRs using **Squash and Merge** to maintain a clean, linear git history on `main`.
   - Delete the topic branch immediately after merging.

---

## 2. Commit Hygiene & Safety

- Keep commits atomic and focused. Do not combine unrelated features or bulk formatting changes with domain logic.
- Never commit credentials, secrets, private keys, or `.env` files.
- Ensure build artifacts (`dist/`, `build/`, `node_modules/`) are strictly ignored.
