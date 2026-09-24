/**
 * Determines whether a given navigation link is active based on current pathname
 */
export function isRouteActive(currentPath: string, targetHref: string): boolean {
	if (targetHref === '/') {
		return currentPath === '/';
	}
	if (targetHref === '/recipes') {
		return currentPath.startsWith('/recipes') && currentPath !== '/recipes/new';
	}
	return currentPath.startsWith(targetHref);
}

/**
 * Categorizes a viewport width into responsive device categories
 */
export function getDeviceCategory(width: number): 'phone' | 'tablet' | 'desktop' {
	if (width < 640) return 'phone';
	if (width < 1024) return 'tablet';
	return 'desktop';
}
