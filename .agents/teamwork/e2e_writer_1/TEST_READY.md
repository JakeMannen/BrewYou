# BrewLink E2E Test Suite: TEST_READY

## Test Suite Summary
- **Test File**: `tests/BrewYou.ApiService.Tests/BrewLinkE2ETests.cs`
- **Total Test Cases**: 74
- **Baseline Status**: 74 Passed, 0 Failed, 0 Skipped (100% pass)
- **Execution Duration**: ~2 seconds
- **Requirements Covered**: R1, R2, R3, R4, R5

---

## How to Run the Tests

### Complete Suite
```powershell
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --no-build --filter FullyQualifiedName~BrewLinkE2ETests
```

### By Test Tier
```powershell
# Tier 1: Feature Coverage (36 tests)
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --no-build --filter FullyQualifiedName~BrewLinkE2ETests_Tier1

# Tier 2: Boundary & Corner Cases (30 tests)
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --no-build --filter FullyQualifiedName~BrewLinkE2ETests_Tier2

# Tier 3: Cross-Feature Combinations (4 tests)
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --no-build --filter FullyQualifiedName~BrewLinkE2ETests_Tier3

# Tier 4: Real-World Scenarios (4 tests)
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --no-build --filter FullyQualifiedName~BrewLinkE2ETests_Tier4
```

> **Environment Note**:
> MSBuild requires `-m:1` (single node) to prevent inter-process build failures under Windows container sandboxing.

---

## 4-Tier Test Breakdown

| Tier | Category / Feature | Test Count | Baseline Status |
| :--- | :--- | :---: | :---: |
| **Tier 1** | **Feature Coverage** | **36** | **36 / 36 Passed** |
| Tier 1.1 | Telemetry Ingestion & Parsing (R1) | 5 | 5 Passed |
| Tier 1.2 | SSE Real-Time Streaming (R1) | 5 | 5 Passed |
| Tier 1.3 | Hardware Control Actions (12 Actions) (R2) | 6 | 6 Passed |
| Tier 1.4 | Host Security Validation (R2, R5) | 5 | 5 Passed |
| Tier 1.5 | Role-Based Authorization (Owner/Brewer/Viewer) (R2) | 5 | 5 Passed |
| Tier 1.6 | State Synchronization (R2) | 5 | 5 Passed |
| Tier 1.7 | Connection Type Expansion (Boiler/Fermenter) (R3) | 5 | 5 Passed |
| **Tier 2** | **Boundary & Corner Cases** | **30** | **30 / 30 Passed** |
| Tier 2.1 | Malformed Frames & Payloads | 5 | 5 Passed |
| Tier 2.2 | Extreme & Boundary Temperatures (-10°C to 120°C) | 5 | 5 Passed |
| Tier 2.3 | Timer Boundaries (0 to 1440 min, negative/overflow) | 5 | 5 Passed |
| Tier 2.4 | Invalid Signatures & Replay Timestamps (>300s window) | 5 | 5 Passed |
| Tier 2.5 | Offline Gateway & Network Failures (502, 409, 401, timeout) | 5 | 5 Passed |
| Tier 2.6 | Loopback SSRF & Encoding Evasion (IPv6, decimal, octal, hex) | 5 | 5 Passed |
| **Tier 3** | **Cross-Feature Combinations** | **4** | **4 / 4 Passed** |
| Tier 3.1 | Control Execution + Immediate SSE Broadcast | 1 | 1 Passed |
| Tier 3.2 | State Sync + DB Update + SSE Broadcast | 1 | 1 Passed |
| Tier 3.3 | Multi-Vessel Telemetry Isolation (no crosstalk) | 1 | 1 Passed |
| Tier 3.4 | Role Boundary Transition (Brewer -> Viewer immediate block) | 1 | 1 Passed |
| **Tier 4** | **Real-World Application Scenarios** | **4** | **4 / 4 Passed** |
| Tier 4.1 | Full Brew Day Telemetry Monitoring (Strike -> Mash -> Boil -> Chill) | 1 | 1 Passed |
| Tier 4.2 | Interactive Step Prompt Acknowledgment ("Add Grain" ack) | 1 | 1 Passed |
| Tier 4.3 | Emergency Session Cancellation (Boil-over safe shutdown) | 1 | 1 Passed |
| Tier 4.4 | Hardware Timer Progression (Start -> Pause -> Resume -> Complete) | 1 | 1 Passed |
| **TOTAL** | | **74** | **74 / 74 Passed (100%)** |

---

## Feature Checklist vs Requirements

- [x] **R1: Telemetry Parsing & Ingestion**
  - WebSocket 1Hz frame parsing into canonical schema
  - REST `GET /api/equipment` polling state normalization
  - Dynamic `CurrentTemperatureC` updating
  - Heating, pump, timer, stage, and prompt mapping into `LatestMetricsJson`
  - Throttled persistence of `EquipmentReading` (30s interval or >=0.5°C delta)
- [x] **R1: Server-Sent Events (SSE) Streaming**
  - `GET /api/v1/inventory/equipment/telemetry-stream` header handshake (`text/event-stream`, `: connected`)
  - Immediate `reading` event emission on telemetry update
  - Immediate `reading` event emission on hardware control execution
  - Immediate `reading` event emission on state sync
  - Multi-client subscription isolation
- [x] **R2: Direct Hardware Control API**
  - `POST /api/v1/inventory/equipment/{id}/brewlink/control`
  - 12 Control Actions: `set_pump`, `set_heat`, `set_target_temp`, `set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`, `press_set`, `ack_interaction`, `dismiss_alert`, `dismiss_sparge_alert`, `cancel_session`
  - HMAC-SHA256 signature algorithm (`"{req_id}:{action}:{timestamp}"`)
  - Argument validation (`power_pct: 0-100`, `duration_min: 1-1440`, `temp_c`)
- [x] **R2: Role-Based Access Control (RBAC)**
  - Owner role permitted (200 OK)
  - Brewer role permitted (200 OK)
  - Viewer role rejected with 403 Forbidden
  - Viewer role permitted to view equipment & subscribe to telemetry stream
  - Unauthenticated requests rejected with 401 Unauthorized
- [x] **R2: On-Demand State Synchronization**
  - `POST /api/v1/inventory/equipment/{id}/brewlink/sync`
  - Live state pull and `Equipment` record update
  - Gateway offline handling (502 Bad Gateway)
- [x] **R3: Equipment Connection Type Expansion**
  - Boiler AllInOne allows `ConnectionType = BrewLink`
  - Boiler BoilKettle allows `ConnectionType = BrewLink`
  - Fermenter Conical allows `ConnectionType = BrewLink`
  - Fermenter Unitank allows `ConnectionType = BrewLink`
  - Recipe upload restriction guard (allowed for All-in-One, rejected for others)
- [x] **R5: Security Guardrails**
  - `NetworkSecurityValidator` host validation
  - Loopback blocking (`127.0.0.1`, `localhost`, `[::1]`)
  - Cloud metadata blocking (`169.254.169.254`, `169.254.0.1`)
  - Encoding evasion blocking (decimal, octal, hex IP)
  - Replay attack mitigation (>300s timestamp window)
