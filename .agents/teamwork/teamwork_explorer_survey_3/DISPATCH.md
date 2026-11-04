## 2026-11-04T07:45:40Z
[Message] timestamp=2026-11-04T07:45:40Z sender=d80eeb54-d338-4b53-876e-3933dc2f1932 priority=MESSAGE_PRIORITY_HIGH content=You are teamwork_explorer instance 3 for Phase 0 Survey.
Working directory: c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\teamwork_explorer_survey_3\
Mandatory: Read ORIGINAL_REQUEST.md first at:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\ORIGINAL_REQUEST.md

Your mission:
Investigate the cascading effects through the system of deleting or renaming equipment, recipes, or ingredients with a deep focus on:
1. User Backup & Restore System, Data Serialization, Versioning, and Backwards Compatibility:
   - Examine `src/BrewYou.ApiService/Backup/` (BackupContracts, BackupService, BackupMigrationPipeline, BackupSchemaConstants, etc.) and `src/BrewYou.Web/src/lib/types/api.ts`.
   - AGENTS.md Section 3.J Mandatory User Backup & Restore Backwards Compatibility rules.
   - What happens when a user backup (.brewyou.json) contains references to equipment, recipes, or ingredients that were deleted or renamed?
   - How do conflict resolution modes (`MergeSkipExisting`, `MergeOverwriteExisting`, `ReplaceAll`) behave when entities were renamed or deleted?
   - How are foreign keys or IDs resolved during restore? (Are IDs remapped? What happens if a recipe references an EquipmentProfileId that does not exist in the backup or database?)
2. BeerXML and BeerJSON import/export:
   - Examine recipe import and export services (BeerXml, BeerJson).
   - How are equipment profiles and ingredients matched/created on import? If an ingredient was renamed or deleted, how does import handle it?
3. Existing Automated Test Coverage:
   - Examine tests in `tests/` across backend and frontend.
   - What tests currently cover deletion and renaming of equipment, recipes, and ingredients?
   - What edge cases are completely uncovered (e.g., deleting equipment with active batches, deleting ingredient used in multiple recipes, renaming recipe while batch is in progress, restoring backup with deleted references)?
4. Detail concrete failure scenarios and discrepancies with exact file paths and line numbers.
5. Formulate proposed solutions (e.g. backup schema migration if needed, dangling reference handling during restore, remapping logic, backwards compatibility tests, automated integration test suite design).

Write your detailed findings and proposed solutions to:
c:\Users\jocke\AgentWorkspaces\BrewYou\.agents\teamwork\teamwork_explorer_survey_3\handoff.md
Update progress.md in your working directory as your liveness heartbeat.
When finished, send a completion message back.
