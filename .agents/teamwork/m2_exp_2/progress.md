# Progress — Milestone 2 Explorer 2 (Gateway Transport & HMAC Signing)

- Last visited: 2026-11-07T22:08:00Z
- Status: Investigation complete. Drafting handoff report and step-by-step implementation plan for Worker.
- Key findings cataloged:
  1. HMAC-SHA256 signing algorithm: hex lowercase HMAC-SHA256 over `"{req_id}:{action}:{timestamp}"` with raw UTF-8 `api_token`.
  2. Envelope layout: `req_id`, `action`, `equipment_type`, `slot`, `target_name`, `target_mac`, `timestamp`, `sig`, `params`.
  3. Gateway endpoints & fallback: POST `/api/equipment/control` with fallback to `/api/equipment/cmd` on 404; sync endpoint GET `/api/equipment`.
  4. Outbound security: Host validation via `NetworkSecurityValidator` blocking loopback, cloud metadata, and zero address while permitting RFC 1918 private networks.
  5. Command set: All 12 actions verified with parameter aliases and boundary validations.
  6. Optimistic state & SSE: Mutation of `equipment.LatestMetricsJson` using `BrewLinkTelemetryParser` and broadcast via `ITelemetryBroadcastService`.
