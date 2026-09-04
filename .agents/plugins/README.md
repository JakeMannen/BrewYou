# Antigravity 2.0 Plugins Directory

This directory holds shareable, namespaced bundles of **Skills**, **Rules**, **Hooks**, and **MCP Server Configurations**.

---

## Plugin Directory Structure

Each plugin should reside in its own subdirectory under `plugins/`:

```text
plugins/<plugin_name>/
├── plugin.json       # Required: Manifest file
├── mcp_config.json   # Optional: MCP servers exposed by the plugin
├── hooks.json        # Optional: Lifecycle hooks run by the plugin
├── rules/            # Optional: Rules applied when plugin is active
│   └── AGENTS.md
└── skills/           # Optional: Skills exposed by the plugin
    └── <skill_name>/
        └── SKILL.md
```

## Example `plugin.json`

```json
{
  "name": "my-plugin",
  "description": "Optional description of plugin functionality",
  "disabled": false
}
```
