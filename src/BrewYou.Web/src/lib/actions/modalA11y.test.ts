import { afterEach, describe, expect, it, vi } from 'vitest';
import { modalA11y } from './modalA11y';

function createStubElement(id: string) {
	return {
		id,
		isConnected: true,
		attrs: new Map<string, string>(),
		focus: vi.fn(),
		hasAttribute(name: string) {
			return this.attrs.has(name);
		},
		getAttribute(name: string) {
			return this.attrs.get(name) ?? null;
		},
		setAttribute(name: string, val: string) {
			this.attrs.set(name, val);
		}
	};
}

describe('modalA11y action', () => {
	afterEach(() => {
		vi.unstubAllGlobals();
	});

	it('traps Tab focus, handles Escape, and restores focus on destroy', () => {
		const listeners = new Map<string, (e: KeyboardEvent) => void>();
		const opener = createStubElement('opener-btn');
		const firstInput = createStubElement('first-input');
		const lastButton = createStubElement('last-btn');

		let activeEl: unknown = opener;
		opener.focus.mockImplementation(() => {
			activeEl = opener;
		});
		firstInput.focus.mockImplementation(() => {
			activeEl = firstInput;
		});
		lastButton.focus.mockImplementation(() => {
			activeEl = lastButton;
		});

		const dialogNode = {
			contains: (el: unknown) => el === firstInput || el === lastButton,
			querySelectorAll: () => [firstInput, lastButton],
			hasAttribute: () => false,
			setAttribute: vi.fn(),
			focus: vi.fn()
		} as unknown as HTMLElement;

		vi.stubGlobal('HTMLElement', Object);
		vi.stubGlobal('queueMicrotask', (fn: () => void) => fn());
		vi.stubGlobal('document', {
			get activeElement() {
				return activeEl;
			},
			addEventListener: (event: string, fn: (e: KeyboardEvent) => void) => {
				listeners.set(event, fn);
			},
			removeEventListener: (event: string) => {
				listeners.delete(event);
			}
		});

		const onClose = vi.fn();
		const handle = modalA11y(dialogNode, { onClose, disabled: false });

		// Initial focus moved into modal
		expect(firstInput.focus).toHaveBeenCalledTimes(1);

		const keydown = listeners.get('keydown')!;
		expect(keydown).toBeDefined();

		// Shift+Tab from first wraps to last
		const shiftTabPrevent = vi.fn();
		keydown({
			key: 'Tab',
			shiftKey: true,
			preventDefault: shiftTabPrevent
		} as unknown as KeyboardEvent);
		expect(shiftTabPrevent).toHaveBeenCalledTimes(1);
		expect(lastButton.focus).toHaveBeenCalledTimes(1);

		// Tab from last wraps to first
		const tabPrevent = vi.fn();
		keydown({
			key: 'Tab',
			shiftKey: false,
			preventDefault: tabPrevent
		} as unknown as KeyboardEvent);
		expect(tabPrevent).toHaveBeenCalledTimes(1);
		expect(firstInput.focus).toHaveBeenCalledTimes(2);

		// Escape closes modal when enabled
		const escPrevent = vi.fn();
		keydown({ key: 'Escape', preventDefault: escPrevent } as unknown as KeyboardEvent);
		expect(escPrevent).toHaveBeenCalledTimes(1);
		expect(onClose).toHaveBeenCalledTimes(1);

		// Escape ignored when disabled (e.g. while saving)
		handle.update({ onClose, disabled: true });
		keydown({ key: 'Escape', preventDefault: vi.fn() } as unknown as KeyboardEvent);
		expect(onClose).toHaveBeenCalledTimes(1);

		// Destroy removes listener and restores focus to opener
		handle.destroy();
		expect(listeners.size).toBe(0);
		expect(opener.focus).toHaveBeenCalledTimes(1);
	});
});
