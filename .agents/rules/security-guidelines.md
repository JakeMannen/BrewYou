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
