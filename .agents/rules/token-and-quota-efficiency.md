# Token and Quota Efficiency Guidelines

Universal operational guidelines to minimize token consumption and preserve Antigravity quota while maintaining high code quality.

---

## 1. Tool Output & Command Filtering

- **Targeted Test Execution**:
  - Never run full monolithic test suites repeatedly during development.
  - Backend: Use quiet verbosity and filters: `dotnet test --verbosity quiet --filter "FullyQualifiedName~<TestClassName>"`.
  - Frontend: Target specific test specs with quiet/silent flags: `npm test -- <SpecName> --silent`.
- **Piped & Truncated Shell Outputs**:
  - Limit directory listings and broad queries using PowerShell filters (e.g. `Select-Object -First 20`).
  - Avoid dumping entire logs or build outputs into the console; grep or pipe to specific failure messages.

---

## 2. Targeted File Inspection

- **Windowed File Reads**:
  - When inspecting large files (>100 lines), always provide `StartLine` and `EndLine` to read only the relevant function, class, or block.
  - Avoid re-reading unchanged files within the same conversational turn if already inspected.

---

## 3. Subagent & Teamwork Restraint

- **Subagent Model Tiering**:
  - When calling `invoke_subagent`, prefer `Model: "flash_lite"` or `Model: "flash"` for code search, file audits, or exploratory research.
  - Reserve `Model: "inherit"` (Pro) strictly for deep architectural or security audits.
- **Single-Agent First**:
  - Execute standard tasks directly in the main conversation rather than delegating trivial edits to subagents.
  - Reserve `/teamwork` for large, asynchronous multi-repository or multi-domain refactors.

---

## 4. Turn & Context Management

- **Batching Operations**:
  - Combine related file edits and planning into cohesive steps rather than single-line ping-pong turns.
- **Context Compaction**:
  - When a task reaches a logical milestone or the conversation exceeds 20 turns, recommend or trigger `/compact`.
  - Recommend starting a fresh conversation session when switching to a completely new feature or user story.
