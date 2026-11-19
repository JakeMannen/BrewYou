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
- **Rate Limiting**: Apply IP-based and user-based rate limiting on sensitive endpoints (e.g., login, registration, password reset).
- **Security Headers**: Use Helmet or equivalent to set `Content-Security-Policy`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`.
- **SSRF & Outbound HTTP Pinning**: Never rely solely on pre-flight DNS checks before `HttpClient.SendAsync` (vulnerable to DNS-rebinding TOCTOU). Enforce IP validation inside `SocketsHttpHandler.ConnectCallback` (`NetworkSecurityValidator.CreateSsrfSafeConnectCallback`) and block IPv6 special ranges (`::/128`, `::/96`, `64:ff9b::/96`, `2002::/16`, `fec0::/10`) alongside IPv4 loopback/private/metadata ranges. Never return upstream HTTP status codes, response body snippets, or raw DNS/socket exception messages to callers.

---

## 4. Multi-Tenant, Collaborative & IoT Invariants

- **Current Setup Membership Over Creator ID**: Authorize setup-scoped entities (e.g., equipment, batches) via current `BrewerySetup` membership and role (`EquipmentAccessHelper`), never treating creator `Entity.UserId` as a permanent access or SSE broadcast grant after member removal/departure. Reassign creator `UserId` when members leave or are removed, and re-validate or clear external gateway bindings when moving equipment across setups.
- **Anti-Oracle Ordering**: Validate caller membership **before** checking entity type, subtype, or state, and return `404 Not Found` to non-members (never `403`/`400`/`409` existence or type oracles).
- **IoT & Webhook Secret Lifecycle**: Store device ingestion tokens as cryptographic hashes (e.g., SHA-256), returning plaintext only on creation or regeneration; clear tokens when `ConnectionType` changes and verify `ConnectionType` on ingest. Never key rate-limiter partitions on user-supplied route tokens (partition on client IP or authenticated user ID). Never preserve encrypted config passwords when the target host, port, or URL changes.
- **Side-Effect-Free Reads & Query Safety**: Keep `GET` endpoints free of database writes, and escape LIKE wildcards (`\`, `%`, `_`) in `EF.Functions.ILike` search filters.
- **Tenant-Scoped Background Consumers & Backups**: Scope background workers, WebSocket managers, MQTT consumers, and backup restore/export pipelines strictly to the gateway/setup owner, neutralizing unmapped or foreign gateway IDs during restore.
