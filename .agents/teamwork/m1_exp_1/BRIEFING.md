# BRIEFING — 2026-11-07T19:21:00Z

## Mission
Investigate and design exact implementation for BrewLinkTelemetryParser.cs and BrewLinkTelemetryParserTests.cs for Milestone 1 (Telemetry Ingestion, Normalization & DB Throttling).

## 🔒 My Identity
- Archetype: explorer
- Roles: investigation, telemetry normalization, test specification
- Working directory: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_exp_1
- Original parent: 6986b155-3bf8-4b59-9b95-f99c2273b921
- Milestone: Milestone 1 - Telemetry Ingestion, Normalization & DB Throttling

## 🔒 Key Constraints
- Read-only investigation — do NOT implement in production source code
- Files for content delivery, Messages for coordination
- Handoff report with 5 components (Observation, Logic Chain, Caveats, Conclusion, Verification Method)
- Output exact C# class structures, parsing methods, and test case plans

## Current Parent
- Conversation ID: 6986b155-3bf8-4b59-9b95-f99c2273b921
- Updated: 2026-11-07T19:21:00Z

## Investigation State
- **Explored paths**:
  - `ORIGINAL_REQUEST.md`, `PROJECT.md`, `survey_explorer_1/handoff.md`
  - `.agents/skills/brewlink-external-client/SKILL.md`
  - `src/BrewYou.ApiService/Services/Telemetry/` (`ITelemetryParser.cs`, `TelemetryParsers.cs`)
  - `src/BrewYou.ApiService/Services/TelemetryService.cs`, `BrewLinkService.cs`, `MqttService.cs`
  - `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte`, `equipment-telemetry-attributes.test.ts`
  - `tests/BrewYou.ApiService.Tests/TelemetryParserTests.cs`
- **Key findings**:
  - `ITelemetryParser` priority hierarchy: `BrewLinkTelemetryParser` needs `Priority => 5` to run ahead of universal parser (10).
  - WebSockets emits nested structure (`state.temperature.current_c`, `state.heating`, `state.pump`, `state.timer`, `state.stage`, `state.interaction`), REST emits flat structure (`current_temp_c`, `heat_active`, `pump_active`, etc.), legacy emits shorthand (`temp_c`, `heat_pct`, `pump_on`).
  - ESP32 gateway `timestamp_ms: 1459200` represents boot uptime milliseconds, NOT a Unix epoch; requires uptime detection threshold (<100,000,000) falling back to `DateTime.UtcNow`.
  - MAC addresses vary between delimiter-less `C44F331A8B22` and colon-delimited `C4:4F:33:1A:8B:22`; canonical schema expects `C4:4F:33:1A:8B:22`.
  - Unit conversion must handle Fahrenheit inputs (`unit: "F"`, `current_temp_f`) using `(F - 32) * 5 / 9`.
  - Frontend parses `LatestMetricsJson` dynamically as stringified badge key-values and in `BrewLinkControlPanel.svelte`.
- **Unexplored areas**: Milestone 2 hardware control signing & execution (separate milestone).

## Key Decisions Made
- Designed `BrewLinkCanonicalMetrics`, `BrewLinkTimerMetrics`, `BrewLinkStageMetrics`, `BrewLinkInteractionMetrics` records.
- Implemented `ITelemetryParser` with `Priority = 5` and static parsing utilities `ParseCanonical` and `ToMetricsJson`.
- Created proposed implementation `proposed_BrewLinkTelemetryParser.cs` and test suite `proposed_BrewLinkTelemetryParserTests.cs` with 18 distinct test scenarios.

## Artifact Index
- `DISPATCH.md` — Task recording
- `BRIEFING.md` — Persistent context & state
- `progress.md` — Liveness heartbeat & progress log
- `proposed_BrewLinkTelemetryParser.cs` — Drop-in proposed parser implementation
- `proposed_BrewLinkTelemetryParserTests.cs` — Drop-in proposed unit test suite
- `handoff.md` — 5-component handoff report for parent agent and implementer
