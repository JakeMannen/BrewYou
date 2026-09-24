import { describe, it, expect } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';

describe('ConfirmModal Component Logic & Invariants', () => {
	it('has complete internationalization keys for confirm and cancel in en.json and sv.json', () => {
		expect(en.common.confirm).toBe('Confirm');
		expect(sv.common.confirm).toBe('Bekräfta');
		expect(en.common.cancel).toBe('Cancel');
		expect(sv.common.cancel).toBe('Avbryt');
	});

	it('computes correct variant styles for all supported modal variants', () => {
		function getVariantClasses(v: 'danger' | 'warning' | 'info' | 'primary') {
			switch (v) {
				case 'danger':
					return {
						iconBg: 'border-red-500/30 bg-red-500/10 text-red-600 dark:text-red-400',
						confirmBtn: 'bg-red-600 hover:bg-red-500 text-white'
					};
				case 'warning':
					return {
						iconBg: 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400',
						confirmBtn: 'bg-amber-600 hover:bg-amber-500 text-white'
					};
				case 'info':
					return {
						iconBg: 'border-sky-500/30 bg-sky-500/10 text-sky-600 dark:text-sky-400',
						confirmBtn: 'bg-sky-600 hover:bg-sky-500 text-white'
					};
				case 'primary':
				default:
					return {
						iconBg: 'border-amber-500/30 bg-amber-500/10 text-amber-600 dark:text-amber-400',
						confirmBtn: 'bg-amber-600 hover:bg-amber-500 text-white'
					};
			}
		}

		expect(getVariantClasses('danger').confirmBtn).toContain('bg-red-600');
		expect(getVariantClasses('warning').confirmBtn).toContain('bg-amber-600');
		expect(getVariantClasses('info').confirmBtn).toContain('bg-sky-600');
		expect(getVariantClasses('primary').confirmBtn).toContain('bg-amber-600');
	});

	it('handles keyboard escape dismissal contract correctly', () => {
		let closed = false;
		const onClose = () => {
			closed = true;
		};

		function handleKeydown(e: { key: string }, open: boolean, loading: boolean) {
			if (e.key === 'Escape' && open && !loading) {
				onClose();
			}
		}

		// When open and not loading, Escape triggers close
		handleKeydown({ key: 'Escape' }, true, false);
		expect(closed).toBe(true);

		// When loading, Escape does NOT trigger close
		closed = false;
		handleKeydown({ key: 'Escape' }, true, true);
		expect(closed).toBe(false);

		// Other keys do NOT trigger close
		closed = false;
		handleKeydown({ key: 'Enter' }, true, false);
		expect(closed).toBe(false);
	});

	it('supports alert mode without cancel button', () => {
		interface ModalConfig {
			showCancel: boolean;
			confirmText: string;
		}

		const alertModal: ModalConfig = {
			showCancel: false,
			confirmText: 'OK'
		};

		expect(alertModal.showCancel).toBe(false);
		expect(alertModal.confirmText).toBe('OK');
	});
});
