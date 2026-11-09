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
   - Evaluate cascading foreign key relationships and ripple effects on ancestral and dependent entities (e.g., how deleting, renaming, or moving an equipment instance affects parent equipment setups, vessels, taps, kegs, and batch sensors).

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

6. **Validate Cascading & Ancestral Ripple Effects**:
   - Verify that all entity mutations (deletions, renamings, moves, and reassignments) correctly propagate or decouple across ancestral objects (e.g. multi-vessel setups, vessel role slots, tap-keg linkages, and sensor assignments).
   - Ensure application service logic explicitly handles relational decoupling rather than relying solely on database triggers or cascades.
   - Author regression and integration tests verifying that ancestral and dependent entities maintain relational integrity with zero orphaned records or foreign key exceptions.
