# Project: Real-Time Telemetry Streaming and Hardware Control for BrewLink Equipment

## Architecture
- **Backend Service Tier (`src/BrewYou.ApiService`)**:
  - Telemetry Ingestion: WebSocket client listener, MQTT broker topic subscriber (`brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`), HTTP REST polling fallback (`GET /api/equipment`).
  - State Normalization: `BrewLinkTelemetryParser` normalizes device payloads into canonical camelCase JSON schema stored in `Equipment.LatestMetricsJson` and updates `CurrentTemperatureC`.
  - SSE Telemetry Broadcasting: `ITelemetryBroadcastService` pushes immediate `reading` events on `/api/v1/inventory/equipment/telemetry-stream` (and batch streams).
  - DB Persistence Throttling: `EquipmentReading` records persisted only on initial reading, or $\ge 30$ seconds elapsed, or significant state/temperature deltas ($\ge 0.5^\circ\text{C}$, heat/pump state changes, timer changes, prompts).
  - Hardware Control & Sync: `IBrewLinkService` and `BrewLinkService` generate HMAC-SHA256 signed envelopes (`"{req_id}:{action}:{timestamp}"`), enforce outbound host validation via `NetworkSecurityValidator`, and route to `POST /api/v1/inventory/equipment/{id}/brewlink/control` and `POST /api/v1/inventory/equipment/{id}/brewlink/sync`.
  - Role Authorization: Owner and Brewer roles allowed; Viewer rejected with 403 Forbidden.
  - Equipment Model & Validation: `Validators.cs` allows `ConnectionType = BrewLink` on all Boiler and Fermenter subtypes. Recipe uploads remain strictly restricted to All-in-One Boilers.
  - Backup & Security: Preserves User Backup & Restore compatibility in `BackupContracts.cs` and `BackupEndpointsTests.cs`.
- **Frontend Client Tier (`src/BrewYou.Web`)**:
  - Reusable Component: `BrewLinkControlPanel.svelte` supporting live temperature vs setpoint, heater power/toggle, pump toggle, hardware timer controls, prompt acknowledgment banners, and session cancellation.
  - UI Integrations: Equipment Inventory card view, Batch Brew Day workspace, and Batch Brew Station HUD modal.
  - Modal Form: `EquipmentModal.svelte` enables BrewLink selection for all Boilers and Fermenters.
  - API Client: Typed methods in `src/lib/api/client.ts` (`sendBrewLinkControl`, `syncBrewLinkState`) and TypeScript interfaces in `src/lib/types/api.ts`.
  - Internationalization: 100% key parity across all 5 locales (`en.json`, `sv.json`, `de.json`, `fr.json`, `es.json`).
- **Testing Tier**:
  - Unit tests: `BrewLinkTelemetryParserTests.cs`, `BrewLinkControlTests.cs`, `i18n.test.ts`.
  - Integration & Regression tests: `BackupEndpointsTests.cs`, `TelemetryTests.cs`.
  - Opaque-box E2E test suite (Tiers 1-4) managed by E2E Testing Track.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | WebSocket Telemetry Streaming | Ingest live 1Hz telemetry frames from `ws://<gateway-ip>/ws?token=<api_token>` | M1 | R1 |
| 2 | MQTT Broker Telemetry Handler | Ingest telemetry from `brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry` | M1 | R1 |
| 3 | REST Polling Telemetry Fallback | Ingest device state via `GET /api/equipment` fallback | M1 | R1 |
| 4 | State Normalization Schema | Canonical schema normalization into `Equipment.LatestMetricsJson` | M1 | R1 |
| 5 | Immediate SSE Broadcast | Immediate zero-latency SSE broadcast on incoming telemetry | M1 | R1 |
| 6 | Throttled DB Persistence | Throttle `EquipmentReading` DB inserts to 30s or significant delta | M1 | R1 |
| 7 | Parser Test Suite | `BrewLinkTelemetryParserTests` verifying frame and payload normalization | M1 | Acceptance Criteria |
| 8 | Hardware Control API Endpoint | `POST /api/v1/inventory/equipment/{id}/brewlink/control` | M2 | R2 |
| 9 | 12 Control Actions & Arguments | `set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session` | M2 | R2 |
| 10 | HMAC-SHA256 Signing | Signed command envelopes using SHA256 of `"{req_id}:{action}:{timestamp}"` | M2 | R2 |
| 11 | Host Security Validation | Validate gateway URLs via `NetworkSecurityValidator` (block loopback/metadata) | M2 | R2, R5 |
| 12 | Role Authorization Guards | Owner/Brewer permitted; Viewer rejected with 403 Forbidden | M2 | R2, Acceptance Criteria |
| 13 | HTTP Status Code Mapping | Accurate 200, 400, 401, 403, 404, 409, 502 status responses | M2 | Acceptance Criteria |
| 14 | On-Demand State Sync Endpoint | `POST /api/v1/inventory/equipment/{id}/brewlink/sync` | M2 | R2 |
| 15 | Optimistic State Updates | Apply optimistic state update and immediate SSE broadcast on control | M2 | R2 |
| 16 | Hardware Control Test Suite | Backend unit and integration tests for control, sync, signing, and roles | M2 | Acceptance Criteria |
| 17 | Connection Type Expansion | Allow `ConnectionType = BrewLink` on all Boiler and Fermenter subtypes | M3 | R3 |
| 18 | Recipe Upload Restriction Guard | Ensure recipe upload capability remains restricted to All-in-One Boilers | M3 | R3 |
| 19 | User Backup Compatibility | Maintain backup and restore compatibility verified by `BackupEndpointsTests` | M3 | R5, AGENTS.md 3.J |
| 20 | Project Documentation | Update `README.md` with new endpoints and hardware control capabilities | M3 | R5, AGENTS.md 3.I |
| 21 | Typed API Client & DTOs | Frontend TypeScript models and methods (`sendBrewLinkControl`, `syncBrewLinkState`) | M4 | R4 |
| 22 | BrewLinkControlPanel Component | Interactive panel with temp, heater, pump, timer, prompt banners, session cancel | M4 | R4 |
| 23 | Inventory Equipment Card Integration | Render control panel & live telemetry badge on inventory equipment cards | M4 | R4 |
| 24 | Brew Day Workspace Integration | Integrate live controls for stage vessel in batch brew page | M4 | R4 |
| 25 | Brew Station HUD Modal Integration | Integrate live controls into `BrewStationHudModal.svelte` | M4 | R4 |
| 26 | Equipment Modal Form Expansion | Allow BrewLink selection for all Boilers and Fermenters in modal | M4 | R3, R4 |
| 27 | 5-Locale i18n Key Parity | 100% key parity across `en.json`, `sv.json`, `de.json`, `fr.json`, `es.json` | M4 | R5, AGENTS.md 3.H |
| 28 | Comprehensive E2E Test Suite | 4-tier requirement-driven opaque-box test suite for telemetry & control | E2E | Acceptance Criteria |
| 29 | 100% E2E Pass & Adversarial Hardening | Phase 1 full E2E pass + Phase 2 Tier 5 adversarial hardening | M5 | Acceptance Criteria |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Telemetry Ingestion, Normalization & DB Throttling | Multi-transport ingestion (WS, MQTT, REST), canonical schema, SSE broadcast, 30s DB throttling, parser tests | none | DONE |
| M2 | Hardware Control API, State Sync & Security | Control and sync endpoints, 12 actions, HMAC-SHA256 signing, NetworkSecurityValidator, role authorization, status codes, control tests | M1 | DONE |
| M3 | Equipment Model Expansion, Backup Compatibility & Documentation | Validators update for Boilers/Fermenters, recipe upload guard preservation, BackupEndpointsTests, README.md | none | IN_PROGRESS |
| M4 | Frontend Hardware Control Panel, UI Integrations & i18n | BrewLinkControlPanel.svelte, Inventory cards, Brew Day workspace & HUD, EquipmentModal, 5-locale i18n | M2, M3 | PLANNED |
| E2E | E2E Testing Track | Design and publish TEST_INFRA.md and TEST_READY.md with 4-tier test cases | none | DONE |
| M5 | Final E2E Test Pass & Adversarial Hardening | 100% pass of Tiers 1-4 tests, followed by Tier 5 adversarial hardening | M1, M2, M3, M4, E2E | PLANNED |

## Interface Contracts

### Backend ↔ Gateway Contracts
- **Control Request**: `POST /api/equipment/control` (or legacy `POST /api/equipment/cmd`)
  - Headers: `Authorization: Bearer <api_token>`, `X-BrewLink-Token: <api_token>`
  - Body: `{ req_id, action, equipment_type, slot, target_name, target_mac, timestamp, sig, params }`
  - Signature: lowercase hex HMAC-SHA256 on `"{req_id}:{action}:{timestamp}"` using gateway `api_token`.
- **Sync Request**: `GET /api/equipment`
  - Headers: `Authorization: Bearer <api_token>`
  - Body: JSON array of equipment records with status and telemetry state.

### Backend ↔ Frontend Contracts
- `POST /api/v1/inventory/equipment/{id}/brewlink/control`
  - Input: `{ action: string, params: object? }`
  - Output: `200 OK` (`ApiResponse<BrewLinkCommandResultDto>`), `400 Bad Request`, `401 Unauthorized`, `403 Forbidden`, `404 Not Found`, `409 Conflict`, `502 Bad Gateway`.
- `POST /api/v1/inventory/equipment/{id}/brewlink/sync`
  - Output: `200 OK` (`ApiResponse<BrewLinkSyncResultDto>`), status error codes matching control.
- `GET /api/v1/inventory/equipment/telemetry-stream`
  - Server-Sent Events stream: `event: reading`, payload: `EquipmentTelemetryUpdateDto` (includes `metricsJson`).

### Canonical Telemetry Schema (`LatestMetricsJson`)
```json
{
  "currentTempC": 65.0,
  "targetTempC": 65.0,
  "boilTempC": 100.0,
  "heatActive": true,
  "heatPowerPct": 100,
  "rampActive": false,
  "pumpActive": true,
  "autoMode": true,
  "timer": {
    "active": true,
    "paused": false,
    "remainingSeconds": 2340,
    "totalSeconds": 3600
  },
  "stage": {
    "stepIndex": 1,
    "stepName": "Mash In",
    "autoMode": true
  },
  "interaction": {
    "required": false,
    "code": 0,
    "message": "Normal operation"
  },
  "mac": "C4:4F:33:1A:8B:22",
  "rssi": -60,
  "updatedAt": "2026-11-07T19:00:00Z"
}
```

## Code Layout
- `src/BrewYou.ApiService/Services/BrewLinkTelemetryParser.cs` - Telemetry frame & REST state parser
- `src/BrewYou.ApiService/Services/IBrewLinkService.cs` - Hardware control & state sync contracts
- `src/BrewYou.ApiService/Services/BrewLinkService.cs` - Hardware control, signing, sync, and gateway HTTP calls
- `src/BrewYou.ApiService/Services/TelemetryBroadcastService.cs` - SSE broadcasting
- `src/BrewYou.ApiService/Validation/Validators.cs` - ConnectionType expansion for Boilers & Fermenters
- `src/BrewYou.ApiService/Endpoints/EquipmentEndpoints.cs` - `/control` and `/sync` API endpoints
- `tests/BrewYou.ApiService.Tests/BrewLinkTelemetryParserTests.cs` - Unit tests for normalization
- `tests/BrewYou.ApiService.Tests/BrewLinkControlTests.cs` - Tests for control actions, signing, role security
- `tests/BrewYou.ApiService.Tests/BackupEndpointsTests.cs` - Backup & restore compatibility tests
- `src/BrewYou.Web/src/lib/types/api.ts` - TypeScript interfaces
- `src/BrewYou.Web/src/lib/api/client.ts` - Frontend API client methods
- `src/BrewYou.Web/src/lib/components/equipment/BrewLinkControlPanel.svelte` - Control panel component
- `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte` - Inventory card integration
- `src/BrewYou.Web/src/routes/batches/[id]/+page.svelte` - Brew Day workspace integration
- `src/BrewYou.Web/src/lib/components/batches/BrewStationHudModal.svelte` - Brew Station HUD integration
- `src/BrewYou.Web/src/lib/components/inventory/EquipmentModal.svelte` - Modal connection type selector
- `src/BrewYou.Web/src/lib/i18n/locales/*.json` - Localization files (en, sv, de, fr, es)
- `README.md` - Documentation of capabilities and API endpoints
