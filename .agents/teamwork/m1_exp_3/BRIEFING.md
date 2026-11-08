# BRIEFING — 2026-11-07T19:30:00Z

## Mission
Investigate and design the Immediate SSE Broadcast and Throttled Database Persistence architecture for Milestone 1 (Telemetry Ingestion, Normalization & DB Throttling).

## 🔒 My Identity
- Archetype: explorer
- Roles: read-only investigation, architectural analysis, synthesis
- Working directory: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_exp_3
- Original parent: 6986b155-3bf8-4b59-9b95-f99c2273b921
- Milestone: Milestone 1: Telemetry Ingestion, Normalization & DB Throttling (Focus: SSE Broadcast & Throttled DB Persistence)

## 🔒 Key Constraints
- Read-only investigation — do NOT implement project code
- Keep all agent metadata inside .agents/teamwork/m1_exp_3/
- Follow 5-component handoff report protocol
- Verify claims against actual codebase files

## Current Parent
- Conversation ID: 6986b155-3bf8-4b59-9b95-f99c2273b921
- Updated: 2026-11-07T19:30:00Z

## Investigation State
- **Explored paths**:
  - `src/BrewYou.ApiService/Services/ITelemetryBroadcastService.cs` & `TelemetryBroadcastService.cs`
  - `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs` (`/telemetry-stream`)
  - `src/BrewYou.ApiService/Endpoints/BatchEndpoints.cs` (`/telemetry-stream`)
  - `src/BrewYou.ApiService/Services/TelemetryService.cs` & `ITelemetryService.cs`
  - `src/BrewYou.ApiService/Data/BrewYouDbContext.cs` & `EquipmentReading.cs` & `Equipment.cs`
  - `tests/BrewYou.ApiService.Tests/TelemetryTests.cs`
  - Peer dispatches: `m1_exp_1/DISPATCH.md` (parser focus) and `m1_exp_2/DISPATCH.md` (transports focus)
- **Key findings**:
  - `TelemetryBroadcastService` supports both `BroadcastEquipmentReading(userId, EquipmentTelemetryUpdateDto)` and `BroadcastReading(BatchEquipmentReadingDto)`.
  - SSE endpoints (`/api/v1/inventory/equipment/telemetry-stream` and `/api/v1/batches/{id}/telemetry-stream`) already serialize and stream camelCase JSON events.
  - Currently, `TelemetryService` unconditionally adds every frame to `_db.EquipmentReadings` and calls `await _db.SaveChangesAsync()`, then broadcasts afterwards. At 1Hz streaming this causes severe database bloat (3,600 rows/hr).
  - SSE broadcast must be decoupled and executed immediately (0ms delay), while PostgreSQL persistence is gated by `ITelemetryThrottlingService`.
  - In-memory throttling cache tracks equipment snapshots in a singleton dictionary with equipment-level synchronization to avoid DB queries during 1Hz ingestion.
  - Throttling persists only on: 1) Initial reading, 2) >= 30s elapsed, 3) |ΔT| >= 0.5°C, or 4) Discrete hardware changes (heat on/off, heat power %, pump toggle, timer start/pause, prompt required/cleared, stage change).
- **Unexplored areas**: None. Design is complete and ready for handoff.

## Key Decisions Made
- SSE broadcasting is executed immediately before or decoupled from DB write, ensuring zero-latency live UI updates.
- In-memory throttling service `ITelemetryThrottlingService` is registered as a Singleton, maintaining thread-safe `EquipmentThrottleSnapshot` records per equipment.
- Timer remaining second decrements are explicitly ignored by discrete state change check to prevent 1Hz writes during active timer countdowns.
- Detailed 4 concrete integration test specifications for `TelemetryTests.cs`.

## Artifact Index
- DISPATCH.md — record of initial dispatch message
- BRIEFING.md — working memory and state
- progress.md — progress heartbeat
- handoff.md — final comprehensive handoff report
