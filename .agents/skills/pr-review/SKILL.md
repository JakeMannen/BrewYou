---
name: pr-review
description: Perform automated self-review and peer code review on GitHub pull requests in BrewYou, auditing diffs, test coverage, tier boundaries, and security.
---

# Pull Request Review Skill

Use this skill to conduct a thorough code review on a pull request or local topic branch before merging into `main`.

---

## Review Workflow

### 1. Inspect Changes & Diff
- Check changed files and inspect diff against target branch (`main`):
  ```bash
  git diff main...HEAD
  # Or for a remote PR:
  gh pr diff <pr-number>
  ```

### 2. Automated Health & Format Check
Run project verification commands to ensure zero compiler warnings, lint failures, format deviations, or test regressions:
```bash
# Verify .editorconfig formatting & static linting
dotnet format --verify-no-changes
npm run lint

# Verify tests pass
dotnet test
npm test
```

### 3. Review Dimensions

Evaluate the PR against the following dimensions:

1. **Mandatory Linting & .editorconfig Enforcement**:
   - Do all added and modified files strictly conform to [.editorconfig](../../../.editorconfig) formatting (indentation, line endings, trimmed whitespace, final newlines)?
   - Are there zero compiler or linter warnings?
2. **Architectural Separation**:
   - Are UI components free of direct database calls, server secrets, and raw business logic?
   - Do backend changes respect controller -> service -> repository boundaries?
3. **API & Contract Parity**:
   - If endpoints changed, are frontend DTOs/types and mock handlers updated?
   - Is the uniform JSON envelope (`{ success, data }` / `{ success, error }`) maintained?
4. **Security & Secrets**:
   - Are any passwords, tokens, API keys, or `.env` files present in the diff?
   - Are user inputs sanitized and parameterized?
5. **Test Coverage**:
   - Is every new feature covered by automated unit and/or integration tests?
   - Do all tests pass deterministically?
6. **Git Hygiene**:
   - Are commits conventional and cleanly scoped (`feat(frontend): ...`, `fix(backend): ...`)?

### 4. Provide Feedback or Approval
- If issues are detected, post constructive, actionable comments pointing directly to file and line numbers.
- If all checks pass:
  ```bash
  gh pr review --approve --body "LGTM! Verified tests pass, .editorconfig/linting clean, tier boundaries respected, and contracts match."
  ```
