# Progress — m1_chal_2

Last visited: 2026-11-07T20:17:30Z

## Status
- Created and executed comprehensive empirical adversarial test suite (`BrewLinkTelemetryAdversarialTests.cs`)
- Tested 6 categories:
  1. Malformed / truncated JSON payloads
  2. Unexpected data types (arrays, numbers, nulls in place of strings/objects)
  3. Out-of-range temperatures (-50°C, 250°C, extreme decimals)
  4. Shorthand vs nested schema combinations
  5. Edge case MAC formats (lowercase, colons, hyphens, non-hex, unformatted)
  6. Replay timestamps, boundary countdown values, and concurrent throttling
- Confirmed critical unhandled exceptions / crash vulnerabilities in production code:
  - `InvalidOperationException` in `BrewLinkTelemetryParser.CanParse` when `event` is numeric/non-string (crashes public ingestion with HTTP 500)
  - `InvalidOperationException` in `BrewLinkTelemetryParser.ParseCanonical` and `ResolveTargetDevice` when `mac`, `name`, `unit`, `step_name`, or `message` is numeric/non-string
  - `ArgumentOutOfRangeException` in `BrewLinkTelemetryParser.ExtractTimestamp` on extreme `timestamp_ms` or `timestamp` values
  - `OverflowException` in `TelemetryThrottlingService.ShouldPersist` on extreme decimal subtraction deltas
  - Inconsistency: flat shorthand `"target"` omitted from `flatKeys`
- Formatting verified with `dotnet format`
- Preparing verdict: REQUEST_CHANGES
