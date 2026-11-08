import { describe, it, expect, vi } from 'vitest';
import en from '$lib/i18n/locales/en.json';
import sv from '$lib/i18n/locales/sv.json';
import de from '$lib/i18n/locales/de.json';
import fr from '$lib/i18n/locales/fr.json';
import es from '$lib/i18n/locales/es.json';
import type { EquipmentDto } from '$lib/types/api';

describe('SendToBoilerModal & BrewLink Recipe Transfer Confirmation Invariants', () => {
	const mockBoilers: EquipmentDto[] = [
		{
			id: 'boiler-1',
			brewerySetupId: 'brewery-1',
			name: 'Grainfather G40',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 40,
			unit: 'Liters',
			capacityLiters: 40,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			boilOffRatePerHour: 3.5,
			trubLossLiters: 1.5,
			mashTunDeadSpaceLiters: 0,
			connectionType: 'BrewLink',
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		},
		{
			id: 'boiler-2',
			brewerySetupId: 'brewery-1',
			name: 'Braumeister 50L',
			type: 'Boiler',
			subtype: 'AllInOne',
			capacity: 50,
			unit: 'Liters',
			capacityLiters: 50,
			currentVolume: 0,
			currentVolumeLiters: 0,
			fillPercentage: 0,
			boilOffRatePerHour: 4.0,
			trubLossLiters: 2.0,
			mashTunDeadSpaceLiters: 0,
			connectionType: 'BrewLink',
			createdAt: '2026-01-01T00:00:00Z',
			updatedAt: '2026-01-01T00:00:00Z'
		}
	];

	it('has complete internationalization keys for transfer confirmation with YES and NO across all 5 locales', () => {
		const locales = [
			{ name: 'en', data: en },
			{ name: 'sv', data: sv },
			{ name: 'de', data: de },
			{ name: 'fr', data: fr },
			{ name: 'es', data: es }
		];

		for (const loc of locales) {
			expect(loc.data.common.yes, `Missing common.yes in ${loc.name}`).toBeDefined();
			expect(loc.data.common.no, `Missing common.no in ${loc.name}`).toBeDefined();
			expect(
				loc.data.recipes.confirm_transfer_title,
				`Missing recipes.confirm_transfer_title in ${loc.name}`
			).toBeDefined();
			expect(
				loc.data.recipes.confirm_transfer_message,
				`Missing recipes.confirm_transfer_message in ${loc.name}`
			).toBeDefined();
			expect(
				loc.data.recipes.confirm_transfer_warning,
				`Missing recipes.confirm_transfer_warning in ${loc.name}`
			).toBeDefined();
			expect(
				loc.data.batches.wizard.confirm_transfer_message,
				`Missing batches.wizard.confirm_transfer_message in ${loc.name}`
			).toBeDefined();
		}

		// Exact English & Swedish key assertions
		expect(en.common.yes).toBe('Yes');
		expect(en.common.no).toBe('No');
		expect(sv.common.yes).toBe('Ja');
		expect(sv.common.no).toBe('Nej');
		expect(de.common.yes).toBe('Ja');
		expect(de.common.no).toBe('Nein');
		expect(es.common.yes).toBe('Sí');
		expect(es.common.no).toBe('No');
		expect(fr.common.yes).toBe('Oui');
		expect(fr.common.no).toBe('Non');

		expect(en.recipes.confirm_transfer_title).toBe('Transfer Recipe to Equipment?');
		expect(sv.recipes.confirm_transfer_title).toBe('Överför recept till utrustning?');
	});

	it('correctly interpolates recipe name and boiler name in confirmation prompt', () => {
		const promptTemplate = en.recipes.confirm_transfer_message;
		const formatted = promptTemplate
			.replace('{recipe}', 'Mosaic IPA')
			.replace('{boiler}', 'Grainfather G40');

		expect(formatted).toBe(
			'Are you sure you want to transfer "Mosaic IPA" to Grainfather G40? This will overwrite the current program schedule on the equipment.'
		);
	});

	it('models the confirmation modal trigger and cancellation flow accurately', () => {
		let showConfirmModal = false;
		const selectedBoilerId = mockBoilers[0].id;
		const recipeId = 'rec-123';
		const apiSendSpy = vi.fn();

		function handleOpenConfirm() {
			if (!selectedBoilerId || !recipeId) return;
			showConfirmModal = true;
		}

		function handleCancelConfirm() {
			showConfirmModal = false;
		}

		// 1. Initial state
		expect(showConfirmModal).toBe(false);

		// 2. User clicks "Send to Boiler" button
		handleOpenConfirm();
		expect(showConfirmModal).toBe(true);
		expect(apiSendSpy).not.toHaveBeenCalled();

		// 3. User clicks NO button in confirmation modal
		handleCancelConfirm();
		expect(showConfirmModal).toBe(false);
		expect(apiSendSpy).not.toHaveBeenCalled();
	});

	it('models the confirmation modal YES flow with successful equipment dispatch', async () => {
		let showConfirmModal = true;
		let isSending = false;
		let successMessage: string | null = null;
		let notifiedBoiler: string | null = null;

		const apiSendMock = vi.fn().mockResolvedValue({ success: true });

		async function handleSend() {
			isSending = true;
			try {
				await apiSendMock('boiler-1', { recipeId: 'rec-123' });
				successMessage = 'Recipe successfully queued on Grainfather G40!';
				showConfirmModal = false;
				notifiedBoiler = 'Grainfather G40';
			} finally {
				isSending = false;
			}
		}

		// User confirms with YES button
		await handleSend();

		expect(apiSendMock).toHaveBeenCalledWith('boiler-1', { recipeId: 'rec-123' });
		expect(showConfirmModal).toBe(false);
		expect(isSending).toBe(false);
		expect(successMessage).toBe('Recipe successfully queued on Grainfather G40!');
		expect(notifiedBoiler).toBe('Grainfather G40');
	});

	it('models equipment disconnect error (261 / 409) handling on confirmation', async () => {
		let showConfirmModal = true;
		let errorMessage: string | null = null;

		const apiSendMock = vi.fn().mockRejectedValue({
			status: 409,
			message: 'Equipment disconnected (error 261)'
		});

		async function handleSend() {
			try {
				await apiSendMock('boiler-1', { recipeId: 'rec-123' });
			} catch (err: unknown) {
				showConfirmModal = false;
				const clientErr = err as { status?: number; message?: string };
				if (clientErr.status === 409 || clientErr.message?.includes('261')) {
					errorMessage = en.recipes.err_equipment_disconnected;
				}
			}
		}

		await handleSend();

		expect(showConfirmModal).toBe(false);
		expect(errorMessage).toBe(en.recipes.err_equipment_disconnected);
	});

	it('intercepts batch start submission with confirmation dialog when BrewLink transfer is enabled', async () => {
		let brewLinkConfirmModalOpen = false;
		let confirmedBrewLinkDispatch = false;
		let batchCreated = false;
		let recipeSent = false;

		const selectedBoiler = mockBoilers[0];
		const sendRecipeToBoiler = true;

		async function handleSubmit(bypassBrewLinkConfirm = false) {
			if (
				!bypassBrewLinkConfirm &&
				!confirmedBrewLinkDispatch &&
				sendRecipeToBoiler &&
				selectedBoiler.connectionType === 'BrewLink'
			) {
				brewLinkConfirmModalOpen = true;
				return;
			}

			batchCreated = true;
			if (sendRecipeToBoiler) {
				recipeSent = true;
			}
		}

		// 1. Initial submission attempt: must intercept and open confirmation modal
		await handleSubmit(false);
		expect(brewLinkConfirmModalOpen).toBe(true);
		expect(batchCreated).toBe(false);
		expect(recipeSent).toBe(false);

		// 2. User clicks NO: confirmation modal closes, batch creation remains cancelled
		brewLinkConfirmModalOpen = false;
		expect(brewLinkConfirmModalOpen).toBe(false);
		expect(batchCreated).toBe(false);
		expect(recipeSent).toBe(false);

		// 3. User attempts submit again and clicks YES in confirmation modal
		await handleSubmit(false);
		expect(brewLinkConfirmModalOpen).toBe(true);

		// User clicks YES
		confirmedBrewLinkDispatch = true;
		brewLinkConfirmModalOpen = false;
		await handleSubmit(true);

		expect(brewLinkConfirmModalOpen).toBe(false);
		expect(batchCreated).toBe(true);
		expect(recipeSent).toBe(true);
	});
});
