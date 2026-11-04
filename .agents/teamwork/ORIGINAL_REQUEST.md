# Original User Request

## Initial Request — 2026-11-04T07:42:52Z

Investigate the cascading effects through the system of deleting or renaming equipment, recipes, or ingredients, produce an investigation report with proposed solutions, and implement the solutions to resolve any issues found.

Integrity mode: development

## Requirements

### R1. System Cascading Impact Investigation
Investigate and document the cascading effects through the system when equipment, recipes, or ingredients are deleted or renamed. The investigation must identify all failure modes, broken references, data integrity violations, unhandled UI states, or backup/restore discrepancies across the full stack.

### R2. Solution Proposals and Documentation
Produce a comprehensive report documenting all findings, including failure scenarios, root causes, and proposed solutions (such as cascade rules, soft-delete, restrict deletion, reference updates, UI warnings, and backup/restore mitigations).

### R3. Implementation of Solutions
Implement the proposed solutions for all identified issues to ensure deleting and renaming operations on equipment, recipes, and ingredients maintain system-wide data integrity and consistent user experience.

## Acceptance Criteria

### Impact Investigation & Report
- [ ] A report is produced documenting the behavior and failure modes of deleting equipment, recipes, and ingredients across database models/constraints, backend domain services, frontend UI/client state, and user backup/restore.
- [ ] The report documents the behavior and failure modes of renaming equipment, recipes, and ingredients across database models/constraints, backend domain services, frontend UI/client state, and user backup/restore.
- [ ] The report details concrete proposed solutions for every identified failure mode or data inconsistency.

### Implementation and Data Integrity
- [ ] Deleting an equipment, recipe, or ingredient does not leave orphaned records, broken foreign keys, unhandled server exceptions, or corrupt backup/restore files.
- [ ] Renaming an equipment, recipe, or ingredient updates all referencing views, records, and calculations consistently without broken state.
- [ ] User Backup & Restore maintains backwards compatibility and round-trip consistency with the updated deletion/renaming behaviors.

### Verification
- [ ] Automated tests cover deletion and renaming edge cases for equipment, recipes, and ingredients, asserting expected cascade or restriction behaviors and data integrity.
- [ ] All existing and new automated test suites pass with zero failures.
