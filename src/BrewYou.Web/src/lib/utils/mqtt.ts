/**
 * Slugifies an equipment name into an MQTT topic segment.
 * Converts to lowercase, strips non-alphanumeric chars (replaces with hyphens), collapses multiple hyphens, and trims leading/trailing hyphens.
 */
export function slugifyEquipmentName(name: string): string {
	if (!name) return '';
	return name
		.toLowerCase()
		.trim()
		.replace(/[^a-z0-9]+/g, '-')
		.replace(/^-+|-+$/g, '');
}

/**
 * Generates the suggested MQTT topic for an equipment device.
 * Format: {basePrefix}/{slug}/telemetry
 */
export function generateEquipmentTopic(prefix: string | null | undefined, name: string): string {
	const cleanPrefix = (prefix || 'brewyou/equipment').trim().replace(/^\/+|\/+$/g, '');
	const slug = slugifyEquipmentName(name);
	return slug ? `${cleanPrefix}/${slug}/telemetry` : '';
}
