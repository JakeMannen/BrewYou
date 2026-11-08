# Dispatch Log

## 2026-11-07T18:57:18Z
Implement real-time telemetry streaming and hardware control for BrewLink-connected equipment in BrewYou as a production feature.

Integrity mode: development

## Requirements

### R1. Multi-Transport Telemetry Ingestion and State Normalization
Implement real-time telemetry ingestion from BrewLink gateways across WebSocket streaming (`ws://<gateway-ip>/ws?token=<api_token>`), MQTT broker topics (`brewlink/{topicKey}/{gatewayId}/equipment/+/telemetry`), and HTTP REST polling fallback (`GET /api/equipment`). Parse and normalize incoming device state (temperature, heating, pump, timer, stage, and user interaction alerts) into `Equipment.LatestMetricsJson`, broadcast live updates over SSE immediately, and throttle database persistence of `EquipmentReading` records to avoid database bloat.

### R2. Direct Hardware Control API and State Synchronization
Add hardware control support allowing authorized brewery members (Owner and Brewer roles) to issue signed HMAC-SHA256 control commands (`set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`) to BrewLink equipment via `POST /api/v1/inventory/equipment/{id}/brewlink/control` and pull live state on demand via `POST /api/v1/inventory/equipment/{id}/brewlink/sync`. Command execution must validate gateway hosts, update equipment state optimistically, and broadcast immediate SSE telemetry updates.

### R3. Equipment Connection Type Expansion
Update equipment configuration to allow `ConnectionType = BrewLink` on all Boiler and Fermenter subtypes, enabling telemetry and hardware control across all supported vessel types while keeping recipe uploads restricted to equipment with recipe upload capabilities.

### R4. Frontend Telemetry Display and Hardware Control Panel
Implement a reusable `BrewLinkControlPanel.svelte` component displaying live temperature against target setpoint, heater status/power controls, pump toggle, hardware timer countdown/controls, and interactive prompt acknowledgment banners. Integrate this panel into the Equipment Inventory card view and the Batch Brew Day workspace and HUD so brewers can monitor telemetry and operate equipment directly.

### R5. Security, Compatibility, and Documentation Guardrails
Enforce host validation via `NetworkSecurityValidator` on all outbound connections, maintain User Backup & Restore backwards compatibility with test verification in `BackupEndpointsTests`, ensure complete localization key parity across English and Swedish locales, and update `README.md` with the new endpoints and capabilities.

## Acceptance Criteria

### Telemetry Ingestion & Parsing
- [ ] `BrewLinkTelemetryParserTests` verifies normalization of WebSocket frames and REST state payloads into canonical `LatestMetricsJson` and temperature readings.
- [ ] Telemetry ingestion broadcasts immediate SSE events on state changes and throttles `EquipmentReading` database persistence to 30-second intervals or significant state/temperature deltas.

### Hardware Control API
- [ ] `POST /api/v1/inventory/equipment/{id}/brewlink/control` validates actions and arguments, transmits HMAC-SHA256 signed envelopes to the gateway, and returns appropriate status codes (200, 400, 401, 403, 404, 409, 502).
- [ ] Viewer roles are rejected with 403 Forbidden when attempting hardware control commands.
- [ ] `POST /api/v1/inventory/equipment/{id}/brewlink/sync` fetches live gateway state and updates equipment records.

### Frontend & UI
- [ ] `BrewLinkControlPanel.svelte` provides interactive controls for heater, pump, temperature setpoint, hardware timer, and prompt acknowledgment.
- [ ] Equipment cards in Inventory and Brew Day workspace/HUD display live telemetry badges and control panels for BrewLink-connected equipment.
- [ ] All new user-facing strings have 100% key parity in `en.json` and `sv.json`.

### Quality & Test Suite Compliance
- [ ] Backend test suite passes (`dotnet test`).
- [ ] Backend code formatting and static analysis pass with zero warnings and zero errors (`dotnet format --verify-no-changes`).
- [ ] Frontend type check and lint pass with zero warnings and zero errors (`npm run check` and `npm run lint`).
- [ ] `BackupEndpointsTests` passes verifying backup and restore compatibility.
- [ ] `README.md` documents the new endpoints and BrewLink hardware control capabilities.
