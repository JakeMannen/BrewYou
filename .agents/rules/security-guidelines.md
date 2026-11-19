# Security Guidelines & Web Application Best Practices

Security standards for frontend and backend development in BrewYou, curated by the **Cybersecurity Expert**.

---

## 1. Authentication & Authorization

- **Token Storage**: Never store sensitive JWT tokens or refresh tokens in insecure `localStorage`. Store authentication tokens in `HttpOnly`, `Secure`, `SameSite=Strict` cookies or short-lived memory with secure refresh token rotation.
- **Role-Based Access Control (RBAC)**: Enforce authorization checks at the backend service/controller level. Never rely exclusively on frontend UI gating for security.

---

## 2. Injection & Input Sanitization

- **SQL / NoSQL Injection**: Always use parameterized queries or trusted ORM/query builder abstractions. Never concatenate user strings into SQL queries.
- **Cross-Site Scripting (XSS)**: Rely on framework escaping (e.g. React JSX auto-escaping). Never use `dangerouslySetInnerHTML` or raw HTML interpolation without sanitization (e.g. DOMPurify).
- **Cross-Site Request Forgery (CSRF)**: Enforce anti-CSRF tokens or `SameSite=Lax/Strict` cookies for state-changing endpoints.

---

## 3. Network & Transport Security

- **CORS**: Configure strict Cross-Origin Resource Sharing headers. Allow only designated frontend origins; never set `Access-Control-Allow-Origin: *` when credentials are included.
- **Rate Limiting**: Apply IP-based and user-based rate limiting on sensitive endpoints (e.g., login, registration, password reset, batch mutations via `"batch-write"`). Place `app.UseRateLimiter()` **after** `app.UseAuthentication()` and `app.UseAuthorization()` in `Program.cs` so user-partitioned policies see `ClaimTypes.NameIdentifier` instead of collapsing into a shared IP bucket.
- **Security Headers & SvelteKit CSP**: Enforce strict `Content-Security-Policy` (via SvelteKit `csp: { mode: 'auto' }` with no inline scripts in `app.html`), `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, and `Referrer-Policy` across both ASP.NET Core and SvelteKit (`hooks.server.ts`).
- **SSRF & Outbound HTTP Pinning**: Never rely solely on pre-flight DNS checks before `HttpClient.SendAsync` (vulnerable to DNS-rebinding TOCTOU). Enforce IP validation inside `SocketsHttpHandler.ConnectCallback` (`NetworkSecurityValidator.CreateSsrfSafeConnectCallback`) and block IPv6 special ranges (`::/128`, `::/96`, `64:ff9b::/96`, `2002::/16`, `fec0::/10`) alongside IPv4 loopback/private/metadata ranges. Never return upstream HTTP status codes, response body snippets, or raw DNS/socket exception messages to callers.

---

## 4. Multi-Tenant, Collaborative & IoT Invariants

- **Current Setup Membership Over Creator ID**: Authorize setup-scoped entities (e.g., equipment, batches) via current `BrewerySetup` membership and role (`EquipmentAccessHelper`), never treating creator `Entity.UserId` as a permanent access or SSE broadcast grant after member removal/departure. Reassign creator `UserId` when members leave or are removed, and re-validate or clear external gateway bindings when moving equipment across setups.
- **Anti-Oracle Ordering**: Validate caller membership **before** checking entity type, subtype, or state, and return `404 Not Found` to non-members (never `403`/`400`/`409` existence or type oracles).
- **IoT & Webhook Secret Lifecycle**: Store device ingestion tokens as cryptographic hashes (e.g., SHA-256), returning plaintext only on creation or regeneration; clear tokens when `ConnectionType` changes and verify `ConnectionType` on ingest. Never key rate-limiter partitions on user-supplied route tokens (partition on client IP or authenticated user ID). Never preserve encrypted config passwords when the target host, port, or URL changes.
- **Side-Effect-Free Reads & Query Safety**: Keep `GET` endpoints free of database writes, and escape LIKE wildcards (`\`, `%`, `_`) in `EF.Functions.ILike(..., @"\")` search filters (with a `!_db.Database.IsInMemory()` branch to `Contains(..., OrdinalIgnoreCase)` for EF InMemory test parity).
- **Tenant-Scoped Background Consumers & Backups**: Scope background workers, WebSocket managers, MQTT consumers, and backup restore/export pipelines strictly to the gateway/setup owner, neutralizing unmapped or foreign gateway IDs during restore.

---

## 5. Batch Lifecycle, Occupancy & Resource Invariants

- **Tenant & Ingredient Scoping**: Scope `RecipeService.GetActiveBatchesForRecipeAsync` and `DeleteRecipeAsync` active-batch guards with `b.UserId == userId` so public recipe owners cannot inspect or be blocked by other users' batches. Scope batch ingredient/potential-gravity lookups and stock deductions/refunds strictly to `i.IsCatalogItem || i.CreatedByUserId == userId`.
- **Stage-Aware Collaborator Equipment Occupancy & Volume Guard**: On live batches (`!IsPastBatch(brewDate)`), return `409 Conflict` when an explicitly assigned vessel (`AssignedBatchId != null && != batch.Id`) or another user's active stage vessel (`b.UserId != userId`) is already occupied, while allowing single-owner multi-batch planning (`restrictToOtherUsersThan: userId`) unless `AssignedBatchId`-locked. Before zeroing vessel volumes on stage change, completion, archival, or deletion, verify no other active live batch across any user still occupies the vessel (`TryResetEquipmentVolumeIfOwnedAsync`).
- **Reading Logging Purity & Telemetry Bounds**: Never fall back to arbitrary `GetAccessibleEquipment(userId)` or auto-create `BrewerySetup`/`Equipment` inside temperature/gravity logging; allow nullable `EquipmentReading.EquipmentId` (`Guid.Empty` / `"Manual"` in DTOs and backups). Reject `Tap` and `BrewLink` targets (`400`), and update live equipment telemetry only when the equipment is linked/assigned to the batch and unoccupied by another active batch.
- **Inventory Deduction Accounting**: Track exact `DeductedAmount` / `DeductedUnit` (clamped at zero stock) on `BatchIngredient` and refund only `DeductedAmount`, keeping new backup DTO fields nullable for backwards compatibility (`BackupContracts`, `BackupService`).
- **Quota, Pagination, Validation & SSE Bounds**: Enforce `MaxActiveBatchesPerUser` on `Completed -> active` batch reopening as well as creation; cap per-batch readings (`MaxReadingsPerBatch`, `MaxEquipmentReadingsPerBatch`), `CustomIngredients` (`<= 100`), and paginate reading lists; enforce atomic per-user SSE subscriber caps and clamp SSE stream lifetime to `min(15m, exp - utcNow)` (`SseStreamHelper`). Match FluentValidation bounds to EF column precision (`numeric(6,2)` <= `9_999.99m`), validate `CheckBatchStockRequest` and `<>`, treat `DateTimeKind.Unspecified` as UTC, clamp reading timestamps to `[BrewDate - 1d, UtcNow + 5min]`, and never return raw `ex.Message` in client warnings (`boilerSyncWarning`).
- **Memory-Only Access Tokens**: Keep JWT access tokens strictly in memory on the SvelteKit client, scrub any legacy `localStorage` token keys (`brewyou_token`) on init/logout, and rehydrate sessions solely via `HttpOnly` refresh cookies.
