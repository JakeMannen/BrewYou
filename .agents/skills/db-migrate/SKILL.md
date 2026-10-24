---
name: db-migrate
description: Safely create, run, rollback, and verify database schema migrations and seed scripts in BrewYou.
---

# Database Migration Skill

Use this skill when modifying database schemas, creating new tables or columns, executing migrations, or seeding development data.

---

## Workflow Steps

1. **Pre-Migration Inspection**:
   - Inspect existing migration history and current database status.
   - Review proposed schema modifications for non-breaking principles (e.g. adding columns as nullable or with defaults before making them required).

2. **Generate Migration**:
   - Create migration script using the project's migration tool (e.g. Prisma `migrate dev --create-only`, Drizzle `generate`, Alembic `revision`).
   - Inspect generated SQL/migration code to ensure it matches expectations and includes appropriate indexes and foreign keys.

3. **Verify Forward and Backward Execution**:
   - Apply migration against a local/test database.
   - Test rollback functionality where supported to ensure reversibility.
   - Re-apply forward migration.

4. **Update ORM / Schema Artifacts**:
   - Regenerate client types or schema definitions (e.g. `prisma generate`).
   - Notify the **Backend Developer** and **API Contract Reviewer** of newly available models and fields.

5. **Synchronize User Backup & Restore Backwards Compatibility**:
   - If the schema change adds, renames, or alters any user-scoped entity, child collection, user preference, or collaboration model, update `BackupContracts.cs`, `BackupService.cs`, and frontend `api.ts`.
   - Keep new backup DTO properties optional/nullable with safe defaults so older `.brewyou.json` backups remain restorable.
   - If the change alters or restructures existing backup fields, increment `BackupSchemaConstants.CurrentSchemaVersion` and add a sequential `IBackupSchemaMigration` upcaster step in `BackupMigrationPipeline.cs`, verified by tests in `BackupEndpointsTests.cs`.
