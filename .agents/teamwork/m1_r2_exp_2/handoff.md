# Milestone 1 Iteration 2 Explorer Report: TelemetryThrottlingService Hardening Strategy

**Agent**: `m1_r2_exp_2` (Explorer)  
**Parent Agent**: `6986b155-3bf8-4b59-9b95-f99c2273b921`  
**Working Directory**: `c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\m1_r2_exp_2\`  
**Date**: 2026-11-07  

---

## 1. Observation

### 1.1 Direct Source Code Inspection

In `src/BrewYou.ApiService/Services/TelemetryThrottlingService.cs`:

**Line 51** (Condition 3: Significant Temperature Delta):
```csharp
50:             // Condition 3: Significant temperature delta (>= 0.5°C)
51:             if (Math.Abs(temperatureC - last.TemperatureC) >= SignificantTempDelta)
52:             {
53:                 _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
54:                 return true;
55:             }
```

**Line 107** (Condition 4g: Significant Specific Gravity Delta):
```csharp
105:             // 4g. Specific gravity change >= 0.001
106:             if (specificGravity.HasValue && last.SpecificGravity.HasValue &&
107:                 Math.Abs(specificGravity.Value - last.SpecificGravity.Value) >= SignificantGravityDelta)
108:             {
109:                 _snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);
110:                 return true;
111:             }
```

### 1.2 Empirical Failure Reproduction

Execution command:
```powershell
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~TelemetryThrottlingService_ExtremeDecimals_ShouldNotThrowOverflowException
```

**Verbatim Test Failure Output**:
```text
Test run for C:\Users\jocke\AgentWorkspaces\BrewYou\tests\BrewYou.ApiService.Tests\bin\Debug\net10.0\BrewYou.ApiService.UnitTests.dll (.NETCoreApp,Version=v10.0)
VSTest version 18.0.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
[xUnit.net 00:00:00.14]     BrewYou.ApiService.Tests.BrewLinkTelemetryAdversarialTests.TelemetryThrottlingService_ExtremeDecimals_ShouldNotThrowOverflowException [FAIL]
  Failed BrewYou.ApiService.Tests.BrewLinkTelemetryAdversarialTests.TelemetryThrottlingService_ExtremeDecimals_ShouldNotThrowOverflowException [22 ms]
  Error Message:
   Did not expect any exception because decimal delta subtraction must not throw System.OverflowException, but found System.OverflowException: Value was either too large or too small for a Decimal.
   at System.Number.ThrowOverflowException(String message)
   at System.Decimal.DecCalc.ScaleResult(Buf24* bufRes, UInt32 hiRes, Int32 scale)
   at System.Decimal.DecCalc.DecAddSub(DecCalc& d1, DecCalc& d2, Boolean sign)
   at System.Decimal.op_Subtraction(Decimal d1, Decimal d2)
   at BrewYou.ApiService.Services.TelemetryThrottlingService.ShouldPersist(Guid equipmentId, DateTime timestampUtc, Decimal temperatureC, BrewLinkCanonicalMetrics metrics, Nullable`1 specificGravity, Nullable`1 stage) in C:\Users\jocke\AgentWorkspaces\BrewYou\src\BrewYou.ApiService\Services\TelemetryThrottlingService.cs:line 51
   at BrewYou.ApiService.Tests.BrewLinkTelemetryAdversarialTests.<>c__DisplayClass24_0.<TelemetryThrottlingService_ExtremeDecimals_ShouldNotThrowOverflowException>b__0() in C:\Users\jocke\AgentWorkspaces\BrewYou\tests\BrewYou.ApiService.Tests\BrewLinkTelemetryAdversarialTests.cs:line 660
   at FluentAssertions.Specialized.FunctionAssertions`1.NotThrow(String because, Object[] becauseArgs)
  Stack Trace:
     at FluentAssertions.Specialized.FunctionAssertions`1.NotThrow(String because, Object[] becauseArgs)
   at BrewYou.ApiService.Tests.BrewLinkTelemetryAdversarialTests.TelemetryThrottlingService_ExtremeDecimals_ShouldNotThrowOverflowException() in C:\Users\jocke\AgentWorkspaces\BrewYou\tests\BrewYou.ApiService.Tests\BrewLinkTelemetryAdversarialTests.cs:line 661
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 22 ms - BrewYou.ApiService.UnitTests.dll (net10.0)
```

### 1.3 Test Code Under Inspection

In `tests/BrewYou.ApiService.Tests/BrewLinkTelemetryAdversarialTests.cs` lines 650–666:
```csharp
    [Fact]
    public void TelemetryThrottlingService_ExtremeDecimals_ShouldNotThrowOverflowException()
    {
        var throttler = new TelemetryThrottlingService(TimeSpan.FromSeconds(30));
        var equipmentId = Guid.NewGuid();
        var t0 = DateTime.UtcNow;

        throttler.ShouldPersist(equipmentId, t0, -10.0m);

        // Extreme decimal difference: decimal.MaxValue
        var act1 = () => throttler.ShouldPersist(equipmentId, t0.AddSeconds(5), decimal.MaxValue);
        act1.Should().NotThrow("decimal delta subtraction must not throw System.OverflowException");

        // Extreme decimal difference: decimal.MinValue
        var act2 = () => throttler.ShouldPersist(equipmentId, t0.AddSeconds(10), decimal.MinValue);
        act2.Should().NotThrow("decimal delta subtraction must not throw System.OverflowException");
    }
```

---

## 2. Logic Chain

1. **Root Cause Mechanism**:
   - In .NET, `System.Decimal` represents numbers using a sign bit, a 96-bit integer, and a scaling power of 10 ($10^{-28}$ to $10^0$).
   - `decimal.MaxValue` is `+79,228,162,514,264,337,593,543,950,335m` ($\approx 7.9228 \times 10^{28}$).
   - `decimal.MinValue` is `-79,228,162,514,264,337,593,543,950,335m`.
   - By language design in C#, decimal arithmetic operators (`+`, `-`, `*`, `/`) **always** throw `System.OverflowException` whenever an operation overflows, regardless of whether execution is in a `checked` or `unchecked` context.
   - When `last.TemperatureC = -10.0m` and `temperatureC = decimal.MaxValue`, evaluation of `temperatureC - last.TemperatureC` executes `decimal.MaxValue - (-10.0m) = decimal.MaxValue + 10.0m`. Because the mathematical sum exceeds `decimal.MaxValue`, the CLR immediately throws `System.OverflowException`.
   - Similarly, if `last.SpecificGravity = 1.050m` and `specificGravity = decimal.MinValue`, `Math.Abs(specificGravity.Value - last.SpecificGravity.Value)` throws `System.OverflowException` at line 107.

2. **Domain Implication of Subtraction Overflow**:
   - An arithmetic overflow during `current - previous` mathematically means that $|current - previous| > decimal.MaxValue \approx 7.9228 \times 10^{28}$.
   - The threshold for persistence is $0.5^\circ\text{C}$ (`SignificantTempDelta`) or $0.001$ (`SignificantGravityDelta`).
   - Because $7.9228 \times 10^{28} \gg 0.5$ and $7.9228 \times 10^{28} \gg 0.001$, any difference that causes an overflow is **unquestionably significant**.
   - Therefore, treating an overflow as triggering persistence (`return true`) is mathematically and functionally correct.

3. **Snapshot Integrity and Throttling Invariant**:
   - When an extreme delta causes persistence, the snapshot MUST be updated with the incoming reading:
     `_snapshots[equipmentId] = CreateSnapshot(timestampUtc, temperatureC, metrics, specificGravity, stage);`
   - If the snapshot were NOT updated (e.g., if the method caught the exception and returned `true` without updating `_snapshots[equipmentId]`), the baseline would remain at `-10.0m`.
   - Consequently, every subsequent reading carrying `decimal.MaxValue` would continue to overflow and persist on every single tick (e.g. 1Hz), completely breaking the 30-second throttling guarantee and flooding the database.
   - With snapshot updating in place, the second reading with `decimal.MaxValue` will compute `decimal.MaxValue - decimal.MaxValue = 0m`. Because $0 < 0.5$, the second reading is properly throttled, preserving database protection.

4. **Thread-Safety Invariant**:
   - `TelemetryThrottlingService` uses per-equipment synchronization locks:
     ```csharp
     var syncLock = _syncLocks.GetOrAdd(equipmentId, _ => new object());
     lock (syncLock) { ... }
     ```
   - Mutual exclusion is guaranteed across concurrent ingestion threads for the same equipment ID.
   - `EquipmentThrottleSnapshot` is a C# positional `record`, which generates immutable `{ get; init; }` properties. Reading from `_snapshots` or `GetLastPersisted` is safe under concurrency.
   - Exception handling inside a private helper (`HasSignificantDelta`) keeps control flow within the `lock (syncLock)` block. The lock is cleanly maintained and released, preventing lock poisoning or abandoned monitors.

---

## 3. Caveats

1. **Upstream Ingestion Guardrail vs Direct Callers**:
   - `TelemetryService.IngestTelemetryAsync` enforces brewing range validation (`finalTempC < MinTemperatureC || finalTempC > MaxTemperatureC` where bounds are `-20°C` and `120°C`), rejecting out-of-range temperatures with `TelemetryIngestStatus.OutOfRange` before calling `_throttler.ShouldPersist`.
   - However, `ITelemetryThrottlingService` is an independent registered singleton service that is directly exposed to unit tests, stress benchmarks, and potential custom equipment adapters.
   - Furthermore, `specificGravity` is not constrained to $[-20, 120]$, making line 107 independently vulnerable to unhandled `OverflowException` if raw sensor gravity feeds emit extreme values.
   - Hardening `TelemetryThrottlingService` ensures resilience regardless of the upstream caller.

2. **`_syncLocks` Memory Footprint**:
   - `_syncLocks` retains a 24-byte lock object per unique `equipmentId`. In multi-tenant deployments with tens of thousands of equipment entries, this consumes a few hundred kilobytes, which is completely negligible.

---

## 4. Conclusion & Proposed Implementation

### 4.1 Recommended Solution: Strategy 1 (Idiomatic Safe Delta Helper)

Introduce a private static helper `HasSignificantDelta` in `TelemetryThrottlingService`:

```csharp
    private static bool HasSignificantDelta(decimal current, decimal previous, decimal threshold)
    {
        try
        {
            return Math.Abs(current - previous) >= threshold;
        }
        catch (OverflowException)
        {
            // Mathematical difference exceeds decimal.MaxValue, vastly exceeding any threshold.
            return true;
        }
    }
```

#### Why Strategy 1 is Preferred:
- **Zero Overhead on Happy Path**: Modern CoreCLR (x64) utilizes table-driven Zero-Cost Exception Handling (SEH). In normal execution when no exception is thrown, entering and exiting a `try` block costs 0 CPU instructions.
- **Concise & Readable**: 8 lines of clear, self-documenting code.
- **Comprehensive**: Handles any sign combination, scale factors, and boundary values without branching bugs.

### 4.2 Alternative Solution: Strategy 2 (Pure Boundary Range Check)

If zero exceptions thrown under all conditions is desired:

```csharp
    private static bool HasSignificantDelta(decimal current, decimal previous, decimal threshold)
    {
        // When signs differ, subtraction is an addition of magnitudes that can overflow decimal boundaries
        if (current >= 0 && previous < 0 && current > decimal.MaxValue + previous)
        {
            return true;
        }

        if (current < 0 && previous > 0 && current < decimal.MinValue + previous)
        {
            return true;
        }

        return Math.Abs(current - previous) >= threshold;
    }
```

*Note*: Note that `decimal.MaxValue + previous` does not overflow because `previous < 0` (so it subtracts magnitude). However, Strategy 1 is simpler and less prone to edge-case regression.

---

### 4.3 Exact File Diff for `m1_impl_1`

Target file: `src/BrewYou.ApiService/Services/TelemetryThrottlingService.cs`

```diff
--- a/src/BrewYou.ApiService/Services/TelemetryThrottlingService.cs
+++ b/src/BrewYou.ApiService/Services/TelemetryThrottlingService.cs
@@ -50,3 +50,3 @@ namespace BrewYou.ApiService.Services;
             // Condition 3: Significant temperature delta (>= 0.5°C)
-            if (Math.Abs(temperatureC - last.TemperatureC) >= SignificantTempDelta)
+            if (HasSignificantDelta(temperatureC, last.TemperatureC, SignificantTempDelta))
             {
@@ -106,3 +106,3 @@ namespace BrewYou.ApiService.Services;
             if (specificGravity.HasValue && last.SpecificGravity.HasValue &&
-                Math.Abs(specificGravity.Value - last.SpecificGravity.Value) >= SignificantGravityDelta)
+                HasSignificantDelta(specificGravity.Value, last.SpecificGravity.Value, SignificantGravityDelta))
             {
@@ -149,2 +149,15 @@ namespace BrewYou.ApiService.Services;
         );
     }
+
+    private static bool HasSignificantDelta(decimal current, decimal previous, decimal threshold)
+    {
+        try
+        {
+            return Math.Abs(current - previous) >= threshold;
+        }
+        catch (OverflowException)
+        {
+            // Mathematical difference exceeds decimal.MaxValue, vastly exceeding any threshold.
+            return true;
+        }
+    }
 }
```

---

## 5. Verification Method

Once the implementation worker applies this change:

### Step 1: Run the Extreme Decimals Test (Targeted Verification)
```powershell
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~TelemetryThrottlingService_ExtremeDecimals_ShouldNotThrowOverflowException
```
*Expected Result*: **1 Passed, 0 Failed, 0 Skipped** in < 1 second.

### Step 2: Run All Throttling Verification Tests
```powershell
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~TelemetryThrottlingService
```
*Expected Result*: All 5 unit tests pass:
- `TelemetryThrottlingService_CountdownJitter_IsThrottled`
- `TelemetryThrottlingService_TimerBoundary_ActiveStateToggle_PersistsImmediately`
- `TelemetryThrottlingService_ReplayTimestamp_HandledGracefully`
- `TelemetryThrottlingService_TemperatureDeltaBoundary_TestsExactThresholds`
- `TelemetryThrottlingService_ExtremeDecimals_ShouldNotThrowOverflowException`

### Step 3: Run Full Stress and Concurrency Suite
```powershell
dotnet test tests/BrewYou.ApiService.Tests/BrewYou.ApiService.Tests.csproj -m:1 --filter FullyQualifiedName~MultiGateway_ConcurrentStreams_ThreadSafeAndAccuratelyIsolated
```
*Expected Result*: Passes with 20 parallel gateways and 10,000 frames.

### Step 4: Verify Formatting
```powershell
dotnet format --verify-no-changes
```
*Expected Result*: Zero violations.
