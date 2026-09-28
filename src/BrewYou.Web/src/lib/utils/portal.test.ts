import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { portal } from './portal';

describe('portal action', () => {
	let mockNode: {
		parentNode: { removeChild: ReturnType<typeof vi.fn> } | null;
	};
	let mockBody: {
		appendChild: ReturnType<typeof vi.fn>;
	};
	let mockContainer: {
		appendChild: ReturnType<typeof vi.fn>;
	};

	beforeEach(() => {
		const removeChildMock = vi.fn();
		mockContainer = {
			appendChild: vi.fn()
		};
		mockNode = {
			parentNode: {
				removeChild: removeChildMock
			}
		};
		mockBody = {
			appendChild: vi.fn()
		};
	});

	afterEach(() => {
		vi.unstubAllGlobals();
		vi.restoreAllMocks();
	});

	it('returns safely without errors when running on the server (document undefined)', () => {
		vi.stubGlobal('document', undefined);

		const actionInstance = portal(mockNode as unknown as HTMLElement);
		expect(actionInstance).toBeDefined();

		// Calling update and destroy in SSR should not throw
		expect(() => actionInstance?.update?.('body')).not.toThrow();
		expect(() => actionInstance?.destroy?.()).not.toThrow();
	});

	it('mounts node to document.body by default in browser environment', () => {
		vi.stubGlobal('document', {
			body: mockBody,
			querySelector: vi.fn()
		});

		const actionInstance = portal(mockNode as unknown as HTMLElement);
		expect(mockBody.appendChild).toHaveBeenCalledWith(mockNode);

		actionInstance?.destroy?.();
		expect(mockNode.parentNode?.removeChild).toHaveBeenCalledWith(mockNode);
	});

	it('mounts node to element specified by CSS selector string', () => {
		const querySelectorMock = vi.fn().mockReturnValue(mockContainer);
		vi.stubGlobal('document', {
			body: mockBody,
			querySelector: querySelectorMock
		});

		const actionInstance = portal(mockNode as unknown as HTMLElement, '#custom-modal-root');
		expect(querySelectorMock).toHaveBeenCalledWith('#custom-modal-root');
		expect(mockContainer.appendChild).toHaveBeenCalledWith(mockNode);

		actionInstance?.destroy?.();
		expect(mockNode.parentNode?.removeChild).toHaveBeenCalledWith(mockNode);
	});

	it('supports dynamic update to a new target', () => {
		const target1 = { appendChild: vi.fn() };
		const target2 = { appendChild: vi.fn() };

		vi.stubGlobal('document', {
			body: mockBody,
			querySelector: vi.fn((sel) => (sel === '#t1' ? target1 : target2))
		});

		const actionInstance = portal(mockNode as unknown as HTMLElement, '#t1');
		expect(target1.appendChild).toHaveBeenCalledWith(mockNode);

		// Switch target
		actionInstance?.update?.('#t2');
		expect(target2.appendChild).toHaveBeenCalledWith(mockNode);

		actionInstance?.destroy?.();
		expect(mockNode.parentNode?.removeChild).toHaveBeenCalledWith(mockNode);
	});
});
