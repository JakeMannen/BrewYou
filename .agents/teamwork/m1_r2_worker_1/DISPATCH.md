## 2026-11-07T20:30:49Z
You are a Teamwork Worker agent for Milestone 1 Iteration 2 in BrewYou.

## Working Directory
`c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_r2_worker_1\`

## Authoritative User Request
Read `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md` before starting work.

## MANDATORY INTEGRITY WARNING
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

## Objective
Apply the thoroughly researched and hardened implementations of `BrewLinkTelemetryParser.cs` and `TelemetryThrottlingService.cs` to eliminate edge-case crashes identified by the Challengers (System.Text.Json token type mismatches, timestamp overflow beyond Year 9999, decimal subtraction overflow, and missing shorthand property).

## Write Ownership
You have exclusive write ownership of these two files:
1. `src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs`
2. `src/BrewYou.ApiService/Services/TelemetryThrottlingService.cs`
You MUST NOT edit any test files or other source files.

## Source Materials & Blueprint
1. Pre-authored hardened parser:
   `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_r2_exp_1\proposed_BrewLinkTelemetryParser.cs`
2. Pre-authored hardened throttler:
   `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_r2_exp_1\proposed_TelemetryThrottlingService.cs`
3. Verification Blueprint & root-cause details:
   `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_r2_exp_3\handoff.md`
4. Milestone 1 Architecture in `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\orchestrator_1\PROJECT.md`

## Instructions
1. Inspect the pre-authored files in `m1_r2_exp_1/` and the synthesis report in `m1_r2_exp_3/handoff.md`.
2. Replace `src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs` with the complete hardened implementation.
3. Replace `src/BrewYou.ApiService/Services/TelemetryThrottlingService.cs` with the complete hardened implementation.
4. Verify code formatting and linting:
   `dotnet format --verify-no-changes`
5. Run the full suite of telemetry, stress, adversarial, and E2E tests:
   `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkTelemetryParserTests`
   `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~TelemetryTests`
   `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkEmpiricalStressTests`
   `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~BrewLinkTelemetryAdversarialTests`
   `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --no-build --filter FullyQualifiedName~BrewLinkE2ETests`
6. Verify that ALL 215 tests pass across all 5 test files (18 + 49 + 24 + 50 + 74 = 215).
7. Write your handoff report to `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_r2_worker_1\handoff.md` including build/test logs and file changes.
8. Send a concise completion message back to the orchestrator.
