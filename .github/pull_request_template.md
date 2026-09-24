## Description
<!-- Brief summary of the changes and motivation behind this PR -->

## Type of Change
- [ ] `feat`: New feature or user capability
- [ ] `fix`: Bug fix
- [ ] `refactor`: Code change that neither fixes a bug nor adds a feature
- [ ] `test`: Adding missing tests or correcting existing tests
- [ ] `docs`: Documentation updates
- [ ] `chore`: Build system, dependencies, or tooling updates

## Tiers Affected
- [ ] **Frontend**: UI components, state management, client routing, styling
- [ ] **Backend**: API routes, services, database models, migrations
- [ ] **Contracts / Shared**: OpenAPI specs, TypeScript types, DTOs
- [ ] **DevOps / CI**: GitHub Actions, environment configuration

## Related Issues
<!-- Link to issue: e.g. Closes #123 -->

## Verification & Testing
<!-- Describe the tests you ran to verify your changes -->
- [ ] Every new feature is covered by unit and/or integration tests (mandatory)
- [ ] Linting & .editorconfig rules enforced with zero warnings/errors (mandatory)
- [ ] Automated tests passing locally (`dotnet test`, `npm test`, or equivalent)
- [ ] Manual smoke test performed

## Pre-Merge Checklist
- [ ] All involved/relevant subagents were consulted during feature development
- [ ] Code adheres to [.editorconfig](.editorconfig) formatting standards and [code standards](.agents/rules/code-standards.md)
- [ ] I have maintained separation of concerns between frontend and backend
- [ ] API changes maintain backwards compatibility or synchronize shared types
- [ ] No secrets, credentials, or environment files are included in this PR
- [ ] Branch is up to date with `main`
