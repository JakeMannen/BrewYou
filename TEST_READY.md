# TEST_READY.md — Cascading Effects E2E Test Suite Readiness & Baseline Report

## 1. Overview
The fullstack cascading effects E2E test suite for **BrewYou** has been created, verified, and published. The test infrastructure establishes an opaque-box, requirement-driven verification harness across all 20 features from `PROJECT.md § Feature Inventory` and `investigation_report.md`.

- **Test Suite Source**: `tests/BrewYou.ApiService.Tests/CascadingE2ETests.cs`
- **Test Infrastructure Document**: `TEST_INFRA.md`
- **Framework**: xUnit, Microsoft.AspNetCore.Mvc.Testing (WebApplicationFactory), FluentAssertions

---

## 2. Test Execution Command

To run the complete cascading test suite:
```powershell
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~CascadingE2ETests" --no-build
```

To run individual tiers:
```powershell
# Tier 1: Feature Coverage (FS-01 through FS-20)
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~CascadingE2ETests.T1" --no-build

# Tier 2: Boundary & Corner Cases
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~CascadingE2ETests.T2" --no-build

# Tier 3: Cross-Feature Combinations
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~CascadingE2ETests.T3" --no-build

# Tier 4: Real-World Application Scenarios
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj --filter "FullyQualifiedName~CascadingE2ETests.T4" --no-build
```

---

## 3. Test Coverage & Baseline Results Summary

| Tier | Test Category | Total Tests | Passing | Failing (Defect Escalate) |
| :--- | :--- | :--- | :--- | :--- |
| **Tier 1** | Feature Coverage (FS-01 to FS-20) | 20 | 14 | 6 (`T1_02`, `T1_03`, `T1_05`, `T1_08`, `T1_09`, `T1_12`, `T1_13`) |
| **Tier 2** | Boundary & Corner Cases | 10 | 8 | 2 (`T2_07`, `T2_08`) |
| **Tier 3** | Cross-Feature Combinations | 5 | 3 | 2 (`T3_01`, `T3_03`) |
| **Tier 4** | Real-World Application Scenarios | 3 | 2 | 1 (`T4_02`) |
| **Total** | **Comprehensive Suite** | **38** | **26** | **12** |

---

## 4. Discovered Implementation Defects & Escalations

The 12 failing tests directly exercise and pinpoint the unhandled cascading failure modes identified during the architecture investigation. These failures are escalated to the implementing developers for Milestones M1, M2, M3, and M4:

1. **FS-02 (Equipment / M1)**: `DeleteEquipmentAsync` omits decoupling of `HltId` and `MashTunId` on batches (`T1_02`, `T3_03`).
2. **FS-03 (Equipment / M1)**: `DeleteEquipmentAsync` omits clearing `AssignedKegId` on referencing taps (`T1_03`).
3. **FS-05 (Equipment / M1)**: `GetActiveBatchesForEquipmentAsync` omits checking HLT, Mash Tun, and sensors (`T1_05`).
4. **FS-08 (Recipe / M2)**: `Batch` entity does not persist `RecipeName`, causing permanent data loss on recipe deletion (`T1_08`, `T3_01`, `T4_02`).
5. **FS-09 (Recipe / M2)**: `DeleteRecipeAsync` has no active batch guard and allows deleting recipes linked to active batches (`T1_09`).
6. **FS-12 (Ingredient / M3)**: `DeleteIngredientAsync` silently deletes recipe ingredients and recalculates metrics rather than returning `409 Conflict` (`T1_13`).
7. **FS-16 (Ingredient / M3)**: Missing `PUT /api/v1/ingredients/{id}` endpoint prevents renaming/editing custom ingredients (`T1_12`, `T2_07`, `T2_08`).

---

## 5. Verification Sign-Off
- [x] All 20 features mapped and covered across 4 tiers.
- [x] Test suite compiles with zero errors under `net10.0`.
- [x] Test suite executes cleanly and deterministically via `dotnet test`.
- [x] Implementation defects accurately isolated for developer handoff.
