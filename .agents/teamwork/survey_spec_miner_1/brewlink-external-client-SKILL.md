# BrewLink External Client Integration Skill (Local Copy)
See original: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\skills\brewlink-external-client\SKILL.md

Refer to full specification in original skill for:
- Broadcast discovery (UDP 255.255.255.255:53540, MQTT brewlink/discovery/<gateway_id>)
- Push-button pairing (POST /api/pair/claim)
- Equipment discovery (GET /api/equipment)
- Recipe upload (POST /api/equipment/recipe)
- Hardware control (POST /api/equipment/control)
- WebSockets protocol (ws://<ip>/ws, event: "telemetry", event: "cmd_ack")
- MQTT protocol and HMAC-SHA256 signature format ("{req_id}:{action}:{timestamp}")
