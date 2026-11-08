# Handoff Report: Requirements R3, R4, and R5 Exploration & Architectural Survey

## 1. Observation

### 1.1. Requirement R3: Equipment Connection Type Expansion
- **Current Constraint in Backend Validators**:
  In `src/BrewYou.ApiService/Validation/Validators.cs`:
  - `CreateEquipmentRequestValidator` (lines 545–550):
    ```csharp
    RuleFor(x => x)
        .Must(x => x.Type == EquipmentType.Boiler && (x.Subtype == null || x.Subtype == EquipmentSubtype.AllInOne))
        .When(x => x.ConnectionType == EquipmentConnectionType.BrewLink)
        .WithMessage("BrewLink connectivity is only supported on All-in-One Boilers.")
        .WithName("ConnectionType");
    ```
  - `UpdateEquipmentRequestValidator` (lines 767–772):
    ```csharp
    RuleFor(x => x)
        .Must(x => x.Type == EquipmentType.Boiler && (x.Subtype == null || x.Subtype == EquipmentSubtype.AllInOne))
        .When(x => x.ConnectionType == EquipmentConnectionType.BrewLink)
        .WithMessage("BrewLink connectivity is only supported on All-in-One Boilers.")
        .WithName("ConnectionType");
    ```
  Both validators strictly reject `ConnectionType = BrewLink` on any vessel where `Type != EquipmentType.Boiler` or where `Subtype` is not `AllInOne`.
- **Equipment Entity Model**:
  In `src/BrewYou.ApiService/Data/Entities/Equipment.cs` (lines 6-14, 16-52, 62-69, 168-184):
  - `EquipmentType` includes `Boiler`, `Fermenter`, `Keg`, `Sensor`, `Tap`, `Other`.
  - `EquipmentSubtype` for Boiler includes: `AllInOne`, `Pan`, `Hlt`, `HermsRims`, `MashTun`, `LauterTun`.
  - `EquipmentSubtype` for Fermenter includes: `Bucket`, `ConicalFermenter`, `Carboy`, `PressureFermenter`, `StainlessBucket`.
  - `EquipmentConnectionType` includes: `None`, `HttpPush`, `HttpPoll`, `Mqtt`, `BrewLink`.
  - `LatestMetricsJson` (`string?`) and `ConnectionConfigJson` (`string?`) are already general properties on `Equipment`.
- **Gateway Binding Validation in Service Layer**:
  In `src/BrewYou.ApiService/Services/EquipmentService.cs`:
  - `CreateEquipmentAsync` (lines 243–249):
    ```csharp
    var gwIdStr = node?["brewLinkGatewayId"]?.GetValue<string>();
    var macStr = node?["targetMac"]?.GetValue<string>();
    if (!Guid.TryParse(gwIdStr, out var parsedGwId) || string.IsNullOrWhiteSpace(macStr))
    {
        return (EquipmentAccessResult.Conflict, null, "BrewLink configuration must include a valid brewLinkGatewayId and targetMac.");
    }
    ```
  - `UpdateEquipmentAsync` (lines 462–468): Identical check.
  - In `Validators.cs` (lines 558–590), `HaveValidBrewLinkConfig` permits `targetMac`, `slot` (`1..3`), or `targetName`.
- **Recipe Upload Scope Restriction**:
  In `src/BrewYou.ApiService/Services/BrewLinkService.cs` (lines 521–526):
  ```csharp
  if (equipment.Type != EquipmentType.Boiler ||
      equipment.Subtype != EquipmentSubtype.AllInOne ||
      equipment.ConnectionType != EquipmentConnectionType.BrewLink)
  {
      return (BrewLinkSendResult.BadRequest, null, "BrewLink recipe upload is only supported on All-in-One Boilers configured with BrewLink.");
  }
  ```
  This guard already guarantees that recipe uploads remain strictly prohibited on non-AllInOne boilers and all fermenters at the backend service boundary.
- **Frontend Equipment Form Constraints**:
  In `src/BrewYou.Web/src/lib/components/inventory/EquipmentModal.svelte`:
  - Reset on category change (lines 466–468):
    ```typescript
    if (connectionType === 'BrewLink' && !(newType === 'Boiler' && defaultSub === 'AllInOne')) {
        connectionType = 'None';
    }
    ```
  - Reset on subtype select (lines 501–503):
    ```typescript
    if (connectionType === 'BrewLink' && !(type === 'Boiler' && selectedSub === 'AllInOne')) {
        connectionType = 'None';
    }
    ```
  - Connection dropdown option visibility (lines 1506–1508):
    ```svelte
    {#if type === 'Boiler' && subtype === 'AllInOne'}
        <option value="BrewLink">{t('equipment.conn_brewlink')}</option>
    {/if}
    ```
- **Frontend Recipe Upload Filtering**:
  - `src/BrewYou.Web/src/routes/recipes/[id]/+page.svelte` (lines 93–95):
    ```typescript
    brewLinkBoilers = (loadedEquipment ?? []).filter(
        (e) => e.type === 'Boiler' && e.subtype === 'AllInOne' && e.connectionType === 'BrewLink'
    );
    ```
  - `src/BrewYou.Web/src/routes/batches/new/+page.svelte` (lines 620, 1242–1252):
    Checkbox for sending recipe is only enabled for boiler selection.

---

### 1.2. Requirement R4: Frontend Telemetry Display and Hardware Control Panel
- **Telemetry Streaming & Parsing Architecture**:
  - SSE Endpoint: `GET /api/v1/inventory/equipment/telemetry-stream` (`EquipmentEndpoints.cs:27–107`).
  - Client stream consumer: `api.equipment.streamTelemetry` in `src/BrewYou.Web/src/lib/api/client.ts` (lines 515–576).
  - Event contract: `EquipmentTelemetryUpdateDto` in `src/BrewYou.Web/src/lib/types/api.ts` (lines 867–883):
    Contains `equipmentId`, `equipmentName`, `temperatureC`, `timestamp`, `metricsJson`, `pressureBar`, `specificGravity`, `batteryPercent`.
  - Batch stream consumer: `api.batches.streamEquipmentReadings` in `src/BrewYou.Web/src/routes/batches/[id]/+page.svelte` (lines 359–380) receives `BatchEquipmentReadingDto` which also contains `metricsJson`.
- **Hardware Control API Requirements (from R2 & External Client Skill)**:
  - Skill file reference: `.agents/skills/brewlink-external-client/SKILL.md` (lines 232–264):
    - `set_pump`: `params: { "state": boolean }`
    - `set_heat`: `params: { "state": boolean, "power_pct": number }` (0–100)
    - `set_target_temp`: `params: { "temp_c": number }`
    - `set_timer`: `params: { "duration_min": number }`
    - `pause_timer`, `resume_timer`, `cancel_timer`
    - `press_set`: press physical SET button on G30
    - `ack_interaction`: acknowledge prompts (e.g. "Add Grain")
    - `dismiss_alert`: dismiss boil timer / hop addition alerts
    - `dismiss_sparge_alert`: dismiss sparge prompt
    - `cancel_session`: abort active automated recipe
  - Endpoints to invoke from client:
    - `POST /api/v1/inventory/equipment/{id}/brewlink/control`
    - `POST /api/v1/inventory/equipment/{id}/brewlink/sync`
- **Integration Touchpoints for `BrewLinkControlPanel.svelte`**:
  1. **Equipment Inventory Card View** (`src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte:880–1015`):
     - Renders equipment cards with badges (`equipment-brewlink-tag`).
     - Needs embedded/expandable control panel for items with `connectionType === 'BrewLink'`.
  2. **Batch Brew Day Workspace** (`src/BrewYou.Web/src/routes/batches/[id]/+page.svelte:1730–2030`):
     - Resolves `currentStageEquipment` (lines 214–294) from mash tun, boiler, or fermenter.
     - When `currentStageEquipment` has `connectionType === 'BrewLink'`, embed the control panel next to or below `StepTemperatureChart`.
  3. **Batch Brew Day HUD** (`src/BrewYou.Web/src/lib/components/batches/BrewStationHudModal.svelte:255–410`):
     - Modal provides fullscreen brew day view with giant countdown timer and tactile buttons.
     - Requires hardware control integration (setpoint adjustment, heater toggle, pump toggle, hardware timer controls, interactive prompt acknowledgment banner).

---

### 1.3. Requirement R5: Security, Compatibility, and Documentation Guardrails
- **User Backup & Restore Backwards Compatibility**:
  - `src/BrewYou.ApiService/Backup/Models/BackupContracts.cs` (lines 157–195):
    - `EquipmentBackupDto` already includes:
      - `EquipmentConnectionType ConnectionType = EquipmentConnectionType.None`
      - `string? ConnectionConfigJson = null`
      - `string? LatestMetricsJson = null`
    - `CurrentSchemaVersion` is `2` (`BackupContracts.cs:34`).
    - Adding `BrewLink` to Fermenter entities does NOT change any DTO schema fields, types, or names.
  - `src/BrewYou.ApiService/Services/BackupService.cs` (lines 1174–1188):
    - Automatically remaps `brewLinkGatewayId` for restored equipment when `restoredConnectionType == EquipmentConnectionType.BrewLink`.
  - Verified test execution:
    `dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BackupAndRestore_RoundTrip_PreservesBrewLinkGatewaysAndRemapsEquipmentGatewayId"` passed (Duration: 1 s, 1 passed).
- **Internationalization (i18n) Enforcement**:
  - `src/BrewYou.Web/src/lib/i18n/i18n.test.ts` (lines 31–42):
    ```typescript
    it('has 100% key parity across English, Swedish, German, French, and Spanish dictionaries', () => {
        const enKeys = getAllKeys(en);
        const svKeys = getAllKeys(sv);
        const deKeys = getAllKeys(de);
        const frKeys = getAllKeys(fr);
        const esKeys = getAllKeys(es);

        expect(svKeys).toEqual(enKeys);
        expect(deKeys).toEqual(enKeys);
        expect(frKeys).toEqual(enKeys);
        expect(esKeys).toEqual(enKeys);
    });
    ```
    Verified test execution: `npm --prefix src/BrewYou.Web run test:unit -- src/lib/i18n/i18n.test.ts --run` passed (16 tests passed).
    **Key Finding**: Key parity is enforced across **all 5 dictionaries** (`en.json`, `sv.json`, `de.json`, `fr.json`, `es.json`), not just English and Swedish!
- **Security & Anti-SSRF Validation**:
  - In `src/BrewYou.ApiService/Services/BrewLinkService.cs` (lines 798–809):
    Outbound gateway URLs are validated via `NetworkSecurityValidator.ValidateUrlAsync(candidateUrl, allowPrivateNetworks: true, cancellationToken: ct)` to block loopback (`127.0.0.1`, `::1`), link-local, and cloud metadata IPs while allowing RFC 1918 LAN gateways.
- **Documentation Status in `README.md`**:
  - Lines 238–258 currently describe BrewLink solely as "Smart Boiler Dispatch" for All-in-One electric boilers.
  - Lines 557–561 list existing endpoints: `/gateways`, `/gateways/discover`, `/gateways/pair-start`, `/gateways/{id}/equipment`, and `.../brewlink/send-recipe`.
  - Missing documentation: hardware control API (`/brewlink/control`, `/brewlink/sync`), multi-vessel telemetry across Boilers and Fermenters, real-time WebSocket/MQTT streaming, and the frontend control panel.

---

## 2. Logic Chain

### 2.1. R3 Equipment Connection Expansion Logic
1. **Observation**: `Validators.cs:546, 768` checks `x.Type == EquipmentType.Boiler && (x.Subtype == null || x.Subtype == EquipmentSubtype.AllInOne)`.
2. **Inference**: Changing this predicate to `(x.Type == EquipmentType.Boiler || x.Type == EquipmentType.Fermenter)` enables `BrewLink` on all Boiler and Fermenter subtypes (AllInOne, Pan, Hlt, HermsRims, MashTun, LauterTun, Bucket, ConicalFermenter, Carboy, PressureFermenter, StainlessBucket).
3. **Observation**: `EquipmentService.cs:246, 465` checks `!Guid.TryParse(gwIdStr, out var parsedGwId) || string.IsNullOrWhiteSpace(macStr)`. However, `Validators.cs:572-585` allows targeting by `targetMac`, `slot`, or `targetName`.
4. **Inference**: In `EquipmentService.cs`, gateway validation should verify that at least one target identifier (`targetMac`, `slot`, or `targetName`) is present along with a valid `brewLinkGatewayId`, aligning with `Validators.cs`.
5. **Observation**: `BrewLinkService.cs:521-526` explicitly rejects recipe uploads if `equipment.Type != EquipmentType.Boiler || equipment.Subtype != EquipmentSubtype.AllInOne`.
6. **Inference**: Recipe uploads will stay safely restricted to All-in-One Boilers on the backend without any change to `BrewLinkService.SendRecipeToEquipmentAsync`.
7. **Observation**: `EquipmentModal.svelte` lines 466, 501, 1506 restricts the dropdown option and resets `connectionType` to `'None'` on type or subtype change unless `type === 'Boiler' && subtype === 'AllInOne'`.
8. **Inference**: Updating `EquipmentModal.svelte` to allow `type === 'Boiler' || type === 'Fermenter'` and relaxing subtype change resets will allow users to configure BrewLink on any boiler or fermenter.

### 2.2. R4 Frontend Telemetry & Control Panel Design Logic
1. **Observation**: BrewLink gateway telemetry payloads (observed in `SKILL.md:339-354` and `survey_explorer_1`) carry temperature (`current_c`, `target_c`), heating (`active`, `power_pct`), pump (`active`), timer (`active`, `paused`, `remaining_seconds`, `total_seconds`), stage (`step_index`, `step_name`, `auto_mode`), and interaction alerts (`required`, `code`, `message`).
2. **Inference**: `BrewLinkControlPanel.svelte` must parse `equipment.latestMetricsJson` (or receive it as a prop/store) and present:
   - Temperature gauge: live vs setpoint, with increment/decrement buttons or input to trigger `set_target_temp`.
   - Heating section: flame badge, power % slider/presets, and toggle button to trigger `set_heat`.
   - Pump section: rotating pump icon badge and toggle button to trigger `set_pump`.
   - Hardware timer section: formatted countdown (`formatHudTime`), duration setter, and start/pause/resume/cancel controls (`set_timer`, `pause_timer`, `resume_timer`, `cancel_timer`).
   - Interactive prompt alert banner: appears when `interaction.required` is true, displaying `interaction.message` and buttons for `ack_interaction`, `press_set`, `dismiss_alert`, and `dismiss_sparge_alert`.
   - Session abort: `cancel_session` button with confirmation.
   - Manual sync: `sync` button calling `POST /api/v1/inventory/equipment/{id}/brewlink/sync`.
3. **Observation**: R2 acceptance criteria require that Viewers receive `403 Forbidden` for hardware control.
4. **Inference**: In the UI, `brewery.userRole === 'Viewer'` (or `isReadOnlyBatch`) must disable control action buttons with a tooltip explaining that control requires Brewer or Owner permissions.
5. **Observation**: In `inventory/equipment/+page.svelte:115-161`, `api.equipment.streamTelemetry` already receives SSE updates and updates `equipmentList[index].latestMetricsJson`.
6. **Inference**: Integrating `<BrewLinkControlPanel {equipment} />` in the inventory card will reactively receive and display live telemetry as soon as `latestMetricsJson` updates.
7. **Observation**: In `batches/[id]/+page.svelte:214-294`, `currentStageEquipment` identifies the equipment for the active stage. In `BrewStationHudModal.svelte:255-410`, tactile controls are displayed for brew day timers.
8. **Inference**: Embedding `BrewLinkControlPanel` in `batches/[id]/+page.svelte` (for the current stage's vessel) and adding a dedicated hardware control panel/drawer or telemetry tiles in `BrewStationHudModal.svelte` gives the brewer direct hardware control during brew day execution.

### 2.3. R5 Security, Compatibility, and Documentation Logic
1. **Observation**: `EquipmentBackupDto` in `BackupContracts.cs` already models all equipment connection fields (`ConnectionType`, `ConnectionConfigJson`, `LatestMetricsJson`). The current backup schema version is `2`.
2. **Inference**: Expanding `ConnectionType` usage to Fermenters does not break serialization or deserialization of older or newer backups. No migration upcaster or schema version bump is required.
3. **Observation**: `BackupEndpointsTests.cs:1220-1320` currently tests an All-in-One Boiler with BrewLink.
4. **Inference**: Adding a test case to `BackupEndpointsTests.cs` that verifies a Fermenter (e.g. `EquipmentSubtype.ConicalFermenter`) with `ConnectionType = EquipmentConnectionType.BrewLink` round-trips correctly and remaps `brewLinkGatewayId` provides bulletproof regression prevention.
5. **Observation**: `i18n.test.ts:31-42` tests strict equality between `getAllKeys(en)` and all other locale files (`sv`, `de`, `fr`, `es`).
6. **Inference**: To satisfy project quality gates, any new translation key for BrewLink telemetry and hardware control must be added to all 5 files: `en.json`, `sv.json`, `de.json`, `fr.json`, and `es.json`.
7. **Observation**: `README.md` documents BrewLink solely as a recipe dispatcher for boilers.
8. **Inference**: `README.md` must be updated to document multi-vessel telemetry ingestion, direct hardware control commands, the new endpoints (`/brewlink/control`, `/brewlink/sync`), and the `BrewLinkControlPanel`.

---

## 3. Caveats
1. **Hardware Driver Differences**: Different brewing equipment models connected to BrewLink (e.g. Grainfather G30 vs G40 vs Brewtools vs Tilt BLE) have varying capability flags (`pump_control`, `heat_control`, `recipe_upload`). The frontend control panel should gracefully hide or disable controls that the peripheral does not support (based on `capabilities` reported in gateway equipment list).
2. **Gateway Response Delays**: Hardware control commands travel over Wi-Fi/HTTP from BrewYou to the ESP32 gateway, then over BLE Central to the physical equipment. Responses may take 200–800ms; UI buttons must reflect an in-flight loading/disabled state to prevent double-submitting.
3. **Batch Telemetry Stream Re-broadcasting**: If a batch is open, batch SSE subscribers (`/batches/{id}/telemetry-stream`) must receive the updated `MetricsJson` whenever BrewLink telemetry is ingested so the batch workspace reflects live state without requiring a separate equipment SSE subscription.

---

## 4. Conclusion
1. **R3 Equipment Connection Type Expansion**:
   - Update `CreateEquipmentRequestValidator` and `UpdateEquipmentRequestValidator` in `Validators.cs` to allow `x.Type == EquipmentType.Boiler || x.Type == EquipmentType.Fermenter` when `x.ConnectionType == EquipmentConnectionType.BrewLink`.
   - Update `EquipmentService.cs` (lines 246, 465) to permit matching by `targetMac`, `slot`, or `targetName`.
   - Update `EquipmentModal.svelte` (lines 466, 501, 1506) to show the BrewLink option for both Boilers and Fermenters and preserve it across subtype selection.
   - Keep recipe upload restricted to `Type == Boiler && Subtype == AllInOne` in `BrewLinkService.cs:521-526`, `recipes/[id]/+page.svelte:93-95`, and `batches/new/+page.svelte:1242`.
2. **R4 Frontend Telemetry Display and Hardware Control Panel**:
   - Create `src/BrewYou.Web/src/lib/components/inventory/BrewLinkControlPanel.svelte` providing:
     - Target setpoint adjustment & live temperature display.
     - Heater power control (0–100%) and on/off toggle.
     - Pump on/off toggle.
     - Hardware timer display and start/pause/resume/cancel controls.
     - Interactive prompt acknowledgment banner (`ack_interaction`, `press_set`, `dismiss_alert`, `dismiss_sparge_alert`).
     - Session abort (`cancel_session`) and state synchronization (`sync`).
     - Role-based disabled state for Viewer users.
   - Add API client methods in `client.ts`: `api.equipment.sendBrewLinkControl` and `api.equipment.syncBrewLinkState`.
   - Integrate `BrewLinkControlPanel.svelte` into:
     - `src/BrewYou.Web/src/routes/inventory/equipment/+page.svelte` (card view).
     - `src/BrewYou.Web/src/routes/batches/[id]/+page.svelte` (brew day workspace for active stage vessel).
     - `src/BrewYou.Web/src/lib/components/batches/BrewStationHudModal.svelte` (HUD modal).
3. **R5 Security, Compatibility, and Documentation Guardrails**:
   - In `BackupEndpointsTests.cs`, add test `BackupAndRestore_RoundTrip_PreservesBrewLinkFermenterAndRemapsGatewayId` to verify Fermenter BrewLink backwards compatibility.
   - Add new translation keys for BrewLink control panel across **all 5 locale files** (`en.json`, `sv.json`, `de.json`, `fr.json`, `es.json`) to satisfy `i18n.test.ts`.
   - Ensure all outbound requests in `BrewLinkService.cs` continue validating gateway URLs via `NetworkSecurityValidator`.
   - Update `README.md` to document the new hardware control endpoints, multi-vessel telemetry, and the control panel.

---

## 5. Verification Method

### 5.1. Backend Automated Verification
Run the following commands from the repository root:
1. **Targeted Backup Compatibility Test**:
   ```powershell
   dotnet test tests\BrewYou.ApiService.Tests\BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BackupEndpointsTests"
   ```
2. **Targeted BrewLink Endpoints Test**:
   ```powershell
   dotnet test tests\BrewYou.ApiService.Tests\BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~BrewLinkEndpointsTests"
   ```
3. **Full Backend Test Suite**:
   ```powershell
   dotnet test
   ```
4. **Backend Code Formatting Verification**:
   ```powershell
   dotnet format --verify-no-changes
   ```

### 5.2. Frontend Automated Verification
Run the following commands from `src/BrewYou.Web`:
1. **i18n 5-Dictionary Parity Test**:
   ```powershell
   npm --prefix src/BrewYou.Web run test:unit -- src/lib/i18n/i18n.test.ts --run
   ```
2. **Equipment & BrewLink Unit Tests**:
   ```powershell
   npm --prefix src/BrewYou.Web run test:unit -- src/lib/components/inventory/ --run
   ```
3. **Batch & HUD Unit Tests**:
   ```powershell
   npm --prefix src/BrewYou.Web run test:unit -- src/lib/components/batches/BrewStationHudModal.test.ts --run
   ```
4. **Typecheck & Linter**:
   ```powershell
   npm --prefix src/BrewYou.Web run check
   npm --prefix src/BrewYou.Web run lint
   ```

### 5.3. Invalidation Conditions
- If `Validators.cs` permits `ConnectionType.BrewLink` on non-Boiler/non-Fermenter types (e.g. `Tap` or `Keg`), validation boundaries are violated.
- If `BrewLinkService.SendRecipeToEquipmentAsync` allows recipe uploads to `Fermenter` or `Pan`, recipe safety invariants are violated.
- If translation keys are only added to `en.json` and `sv.json`, `i18n.test.ts` will fail on lines 39–41 due to missing keys in `de.json`, `fr.json`, or `es.json`.
- If `NetworkSecurityValidator` is bypassed for hardware control calls, SSRF protection invariants are violated.
