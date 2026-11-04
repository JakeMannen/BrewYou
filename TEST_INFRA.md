# TEST_INFRA.md — Fullstack Cascading Effects E2E Test Infrastructure

## 1. Test Philosophy

The BrewYou Cascading Effects test infrastructure operates on an **opaque-box, requirement-driven verification philosophy** derived directly from [ORIGINAL_REQUEST.md](.agents/teamwork/ORIGINAL_REQUEST.md) and [PROJECT.md](PROJECT.md).

### Core Principles
1. **Opaque-Box Specification Conformance**: Tests interact exclusively through external system boundaries (HTTP REST APIs, EF Core persistence, serialized backup payloads, and UI component contracts). Tests make assertions against observable system responses (HTTP status codes, standard JSON envelopes, database integrity invariants, and state consistency) rather than internal implementation mechanics.
2. **Authoritative Expected Output Derivation**: Every test case derives its expected behavior from documented specifications:
   - Foreign key integrity and decoupling behaviors specified in `PROJECT.md § Interface Contracts` and `investigation_report.md`.
   - Explicit HTTP status codes (`200 OK`, `201 Created`, `400 Bad Request`, `404 Not Found`, `409 Conflict`).
   - Standard envelope contracts (`ApiResponse`, `ApiResponse<T>`, `ApiErrorDetail`).
   - Round-trip backup schema invariant guarantees (AGENTS.md § 3.J).
3. **Strict Isolation & State Independence**: Every test generates unique test fixtures (distinct registered users, brewery setups, equipment, recipes, ingredients, and batches). Tests execute in complete isolation without dependencies on ordering or residual state from prior runs.
4. **Adversarial Edge Hardening**: Beyond nominal paths, tests aggressively exercise adversarial conditions: dangling Guids, null references, missing parent entities, foreign key constraints under concurrent deletion, and legacy schema restoration.

---

## 2. 4-Tier Test Methodology

The testing strategy is structured into four ascending tiers of verification rigor:

```
+-------------------------------------------------------------------------+
|                  Tier 4: Real-World Brewing Scenarios                  |
|     (Full-lifecycle brew day workflows, equipment recycling, backups)   |
+-------------------------------------------------------------------------+
                                    ^
+-------------------------------------------------------------------------+
|                Tier 3: Cross-Feature Pairwise Interactions              |
|        (Equipment <-> Batch <-> Recipe <-> Ingredient <-> Backup)       |
+-------------------------------------------------------------------------+
                                    ^
+-------------------------------------------------------------------------+
|                Tier 2: Boundary & Corner Cases (>=5 / feature)          |
|    (Deleted entities, dangling Guids, null fallbacks, FK restrict)      |
+-------------------------------------------------------------------------+
                                    ^
+-------------------------------------------------------------------------+
|                 Tier 1: Feature Coverage (>=5 / feature)                |
|      (Nominal CRUD, contract verification, state transitions, APIs)     |
+-------------------------------------------------------------------------+
```

### Tier 1: Feature Coverage
- **Objective**: Complete baseline verification of all 20 features defined in `PROJECT.md § Feature Inventory`.
- **Requirement**: Each feature has $\ge 5$ explicit test assertions verifying happy paths, parameter validations, contract shapes, and state transitions.
- **Scope**:
  - Equipment CRUD, sensor assignment, tap assignment, keg assignment, active batch queries.
  - Recipe creation, calculation, deletion, batch linkage, and recipe name resolution.
  - Ingredient creation, updating/renaming (`PUT /api/v1/ingredients/{id}`), usage inspection, deletion guards.
  - Backup export, restore preview, conflict resolution (`MergeSkipExisting`, `MergeOverwriteExisting`, `ReplaceAll`).

### Tier 2: Boundary & Corner Cases
- **Objective**: Resilience testing under abnormal, edge, and failure conditions.
- **Requirement**: Each feature has $\ge 5$ edge-case assertions verifying error codes, restriction enforcement, dangling reference handling, and fallback defaults.
- **Scope**:
  - Deleting equipment assigned to sensors (eliminating FK 23503 `DbUpdateException`).
  - Deleting vessels assigned as HLT, Mash Tun, Boiler, Fermenter, or Packaging Vessel.
  - Deleting kegs linked to active taps.
  - Deleting recipes with active batches (`409 Conflict`) vs completed batches (persisting `RecipeName` snapshot).
  - Deleting ingredients used in recipes (`409 Conflict`) or batches.
  - Renaming ingredients with duplicate names, invalid vitals, or cross-tenant interference.
  - Restoring backups with non-existent ingredient Guids (sanitized to `null`).

### Tier 3: Cross-Feature Combinations
- **Objective**: Multi-entity interaction testing across subsystem boundaries.
- **Requirement**: Pairwise and combinatorial scenarios evaluating inter-domain cascading:
  - *Equipment $\times$ Batch*: Deleting active fermentation vessel while batch is in `Fermenting` stage.
  - *Recipe $\times$ Batch*: Deleting recipe, creating new batch from snapshot, verifying recipe name continuity.
  - *Ingredient $\times$ Recipe $\times$ Batch*: Deleting custom hop, verifying stock deduction fallback to name matching.
  - *BrewerySetup $\times$ Equipment $\times$ Sensor*: Deleting brewery setup containing multiple sensor-assigned vessels.
  - *Backup $\times$ Recipe $\times$ Ingredient*: Exporting backup, purging live catalog, restoring in `ReplaceAll` mode.

### Tier 4: Real-World Application Scenarios
- **Objective**: End-to-end craft brewing operations simulating actual brewery workflows.
- **Requirement**: Lifelike, end-to-end multi-step scenarios:
  - **Scenario A: Complete Grain-to-Glass Lifecycle with Mid-Process Equipment Upgrades**:
    1. Brewer sets up 3-vessel brewhouse (HLT, Mash Tun, Boiler) with tilt hydrometer sensor.
    2. Formulates NEIPA recipe with proprietary custom hops.
    3. Initiates active batch, logs mash and boil telemetry via assigned sensor.
    4. Upgrades/replaces Boiler mid-cycle; verifies decoupling and sensor re-assignment without data corruption.
    5. Transfers to keg and mounts on tap; decommissions empty keg; verifies tap state cleanses gracefully.
  - **Scenario B: Recipe Versioning & Historical Archive Continuity**:
    1. Brewer creates seasonal stout recipe and brews 3 batches over 6 months.
    2. Brewer retires/deletes seasonal recipe to clean inventory.
    3. Validates historical batch reports, bottle labels, and export backups retain original recipe name and metrics.
  - **Scenario C: Multi-Device Disaster Recovery with Orphaned Community References**:
    1. Brewer backs up profile with community recipe references and custom grains.
    2. Target server has deleted the community recipe and custom grains.
    3. Restores backup; verifies community recipe is cloned into personal library and dangling grain references are sanitized.

---

## 3. Complete Feature Mapping Table

| Feature # | Feature Name | Description | Test Tier | Test Class / Method | Verification Status |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **F-01** | Equipment Sensor Decoupling | Decouple `BatchSensorAssignment` on equipment delete | T1, T2, T3 | `Tier1_EquipmentSensorDecoupling_Tests`<br>`Tier2_EquipmentSensorDecoupling_Boundary_Tests` | Verified / Ready |
| **F-02** | Complete Batch Vessel Decoupling | Decouple HLT, Mash Tun, Boiler, Fermenter, Packaging Vessel | T1, T2, T3 | `Tier1_VesselDecoupling_Tests`<br>`Tier2_VesselDecoupling_Boundary_Tests` | Verified / Ready |
| **F-03** | Tap-to-Keg Decoupling | Clear `AssignedKegId` on Taps on keg deletion | T1, T2, T3 | `Tier1_TapKegDecoupling_Tests`<br>`Tier2_TapKegDecoupling_Boundary_Tests` | Verified / Ready |
| **F-04** | BrewerySetup Deletion Guard | Pre-clean `BatchSensorAssignments` on setup delete | T1, T2, T3 | `Tier1_BrewerySetupDeletion_Tests`<br>`Tier2_BrewerySetupDeletion_Boundary_Tests` | Verified / Ready |
| **F-05** | Active Batch Equipment Detection | Check all 5 vessels and sensors in `GetActiveBatchesForEquipmentAsync` | T1, T2 | `Tier1_ActiveBatchDetection_Tests`<br>`Tier2_ActiveBatchDetection_Boundary_Tests` | Verified / Ready |
| **F-06** | Batch Workspace Vessel Fallback | Handle null vessel without arbitrary index fallback | T1, T2 | `Tier1_BatchVesselFallback_Tests`<br>`Tier2_BatchVesselFallback_Boundary_Tests` | Verified / Ready |
| **F-07** | Packaging Vessel Select Null Safety | Gracefully handle deleted packaging vessel in DTOs | T1, T2 | `Tier1_PackagingVesselSafety_Tests`<br>`Tier2_PackagingVesselSafety_Boundary_Tests` | Verified / Ready |
| **F-08** | Batch RecipeName Persistence | Snapshot `RecipeName` on `Batch`; preserve on recipe delete | T1, T2, T3 | `Tier1_BatchRecipeNamePersistence_Tests`<br>`Tier2_BatchRecipeNamePersistence_Boundary_Tests` | Verified / Ready |
| **F-09** | Recipe Deletion Active Batch Guard | Prevent recipe deletion if active batches are in progress | T1, T2 | `Tier1_RecipeDeletionActiveBatchGuard_Tests`<br>`Tier2_RecipeDeletionActiveBatchGuard_Boundary_Tests` | Verified / Ready |
| **F-10** | Printable Bottle Label Fallback | Render fallback label when recipeId is null | T1, T2 | `Tier1_PrintableLabelFallback_Tests`<br>`Tier2_PrintableLabelFallback_Boundary_Tests` | Verified / Ready |
| **F-11** | Batch Wizard Deleted Recipe Handling | Graceful error/fallback when recipe deleted | T1, T2 | `Tier1_BatchWizardRecipeHandling_Tests`<br>`Tier2_BatchWizardRecipeHandling_Boundary_Tests` | Verified / Ready |
| **F-12** | Custom Ingredient Update API | `PUT /api/v1/ingredients/{id}` and `UpdateIngredientAsync` | T1, T2 | `Tier1_IngredientUpdateApi_Tests`<br>`Tier2_IngredientUpdateApi_Boundary_Tests` | Verified / Ready |
| **F-13** | Ingredient Deletion Recipe Guard | Restrict deletion of ingredients in use by recipes | T1, T2, T3 | `Tier1_IngredientDeletionRecipeGuard_Tests`<br>`Tier2_IngredientDeletionRecipeGuard_Boundary_Tests` | Verified / Ready |
| **F-14** | Ingredient Batch Usage Visibility | Track and display batch usages in `GetIngredientUsageAsync` | T1, T2 | `Tier1_IngredientBatchUsageVisibility_Tests`<br>`Tier2_IngredientBatchUsageVisibility_Boundary_Tests` | Verified / Ready |
| **F-15** | Stock Deduction Name Fallback | Fallback to name matching when `SourceIngredientId` deleted | T1, T2, T3 | `Tier1_StockDeductionNameFallback_Tests`<br>`Tier2_StockDeductionNameFallback_Boundary_Tests` | Verified / Ready |
| **F-16** | Recipe Editor Missing Ingredient UI | Placeholder handling for deleted/missing ingredient IDs | T1, T2 | `Tier1_RecipeMissingIngredient_Tests`<br>`Tier2_RecipeMissingIngredient_Boundary_Tests` | Verified / Ready |
| **F-17** | Backup Dangling Guid Sanitization | Set dangling `SourceIngredientId` to null on restore | T1, T2, T3 | `Tier1_BackupDanglingGuidSanitization_Tests`<br>`Tier2_BackupDanglingGuidSanitization_Boundary_Tests` | Verified / Ready |
| **F-18** | Backup Community Recipe Cloning | Clone deleted community recipes on restore | T1, T2, T4 | `Tier1_BackupCommunityRecipeCloning_Tests`<br>`Tier2_BackupCommunityRecipeCloning_Boundary_Tests` | Verified / Ready |
| **F-19** | Backup Backwards Compatibility | Ensure schema v0, v1, v2 and `RecipeName` compatibility | T1, T2, T4 | `Tier1_BackupBackwardsCompatibility_Tests`<br>`Tier2_BackupBackwardsCompatibility_Boundary_Tests` | Verified / Ready |
| **F-20** | Full E2E Cascading Test Suite | Comprehensive automated tests across all cascading scenarios | T1..T4 | `CascadingE2ETests` (Tiers 1, 2, 3, 4 suites) | Verified / Ready |

---

## 4. Test Runner Commands & CI Matrix

### Backend C# Test Execution
```powershell
# Run the complete cascading test suite
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~CascadingE2ETests"

# Run by specific tier
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~Tier1"
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~Tier2"
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~Tier3"
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~Tier4"

# Run all backend unit and integration tests
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj
```

### Frontend Client Test Execution
```bash
# Run SvelteKit unit tests
npm run test:unit

# Run full project type check and lint
npm run check
npm run lint
```
