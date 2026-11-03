/**
 * Sorts equipment items alphabetically by name.
 * Uses natural numerical sorting (e.g. 'Tap 1', 'Tap 2', 'Tap 10')
 * with case-insensitivity. Falls back to id comparison if names are identical.
 */
export function sortEquipmentByName<T extends { name: string; id?: string }>(items: T[]): T[] {
	return [...items].sort((a, b) => {
		const cmp = a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' });
		if (cmp !== 0) return cmp;
		return (a.id ?? '').localeCompare(b.id ?? '');
	});
}
