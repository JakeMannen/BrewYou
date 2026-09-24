import { describe, it, expect } from 'vitest';
import { isRouteActive, getDeviceCategory } from './navigation/navUtils';

export { isRouteActive, getDeviceCategory };

describe('Responsive Navigation & Active Route Logic', () => {
	it('correctly matches home route strictly', () => {
		expect(isRouteActive('/', '/')).toBe(true);
		expect(isRouteActive('/recipes', '/')).toBe(false);
		expect(isRouteActive('/ingredients', '/')).toBe(false);
	});

	it('correctly matches recipes list but excludes recipes/new formulator', () => {
		expect(isRouteActive('/recipes', '/recipes')).toBe(true);
		expect(isRouteActive('/recipes/detail-123', '/recipes')).toBe(true);
		expect(isRouteActive('/recipes/new', '/recipes')).toBe(false);
		expect(isRouteActive('/recipes/new', '/recipes/new')).toBe(true);
	});

	it('correctly matches ingredients route', () => {
		expect(isRouteActive('/ingredients', '/ingredients')).toBe(true);
		expect(isRouteActive('/recipes', '/ingredients')).toBe(false);
	});

	it('correctly matches batches route and nested batch paths', () => {
		expect(isRouteActive('/batches', '/batches')).toBe(true);
		expect(isRouteActive('/batches/b-1', '/batches')).toBe(true);
		expect(isRouteActive('/recipes', '/batches')).toBe(false);
		expect(isRouteActive('/', '/batches')).toBe(false);
		expect(isRouteActive('/batches', '/recipes')).toBe(false);
	});

	it('correctly matches calculations route', () => {
		expect(isRouteActive('/calculations', '/calculations')).toBe(true);
		expect(isRouteActive('/calculations#water-ph', '/calculations')).toBe(true);
		expect(isRouteActive('/recipes', '/calculations')).toBe(false);
	});

	it('categorizes viewports into phone, tablet, and desktop breakpoints', () => {
		// Phone (< 640px)
		expect(getDeviceCategory(320)).toBe('phone');
		expect(getDeviceCategory(375)).toBe('phone'); // iPhone SE
		expect(getDeviceCategory(390)).toBe('phone'); // iPhone 14
		expect(getDeviceCategory(412)).toBe('phone'); // Pixel 7
		expect(getDeviceCategory(639)).toBe('phone');

		// Tablet (640px - 1023px)
		expect(getDeviceCategory(640)).toBe('tablet');
		expect(getDeviceCategory(768)).toBe('tablet'); // iPad portrait / md breakpoint
		expect(getDeviceCategory(810)).toBe('tablet');
		expect(getDeviceCategory(1023)).toBe('tablet');

		// Desktop (>= 1024px)
		expect(getDeviceCategory(1024)).toBe('desktop');
		expect(getDeviceCategory(1280)).toBe('desktop');
		expect(getDeviceCategory(1440)).toBe('desktop');
		expect(getDeviceCategory(1920)).toBe('desktop');
	});
});
