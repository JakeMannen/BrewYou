import { describe, it, expect, vi } from 'vitest';
import { scanBrewLinkGateways } from './brewlink';
import type { BrewLinkDiscoveredGatewayDto } from '$lib/types/api';

function makeGateway(id = 'C44F331A8B20'): BrewLinkDiscoveredGatewayDto {
	return {
		gatewayId: id,
		name: 'BrewLink Gateway',
		ip: '192.168.1.50',
		httpPort: 80,
		host: '192.168.1.50',
		claimUrl: 'http://192.168.1.50/api/pair/claim',
		wsUrl: 'ws://192.168.1.50/ws',
		remainingSec: 55,
		discoveredAtUtc: '2026-11-26T19:00:00Z',
		alreadyPaired: false
	};
}

describe('scanBrewLinkGateways', () => {
	it('returns immediately on the first poll when a beacon is already available', async () => {
		const gateway = makeGateway();
		const fetchGateways = vi.fn().mockResolvedValue([gateway]);
		const delayFn = vi.fn().mockResolvedValue(undefined);
		const onUpdate = vi.fn();

		const result = await scanBrewLinkGateways(fetchGateways, {
			maxAttempts: 5,
			intervalMs: 1500,
			onUpdate,
			delayFn
		});

		expect(result).toEqual([gateway]);
		expect(fetchGateways).toHaveBeenCalledTimes(1);
		expect(delayFn).not.toHaveBeenCalled();
		expect(onUpdate).toHaveBeenCalledWith([gateway]);
	});

	it('polls across broadcast intervals until a gateway appears', async () => {
		const gateway = makeGateway('AABBCCDDEEFF');
		const fetchGateways = vi
			.fn()
			.mockResolvedValueOnce([])
			.mockResolvedValueOnce([])
			.mockResolvedValueOnce([gateway]);
		const delayFn = vi.fn().mockResolvedValue(undefined);
		const onUpdate = vi.fn();

		const result = await scanBrewLinkGateways(fetchGateways, {
			maxAttempts: 5,
			intervalMs: 1500,
			onUpdate,
			delayFn
		});

		expect(result).toEqual([gateway]);
		expect(fetchGateways).toHaveBeenCalledTimes(3);
		expect(delayFn).toHaveBeenCalledTimes(2);
		expect(onUpdate).toHaveBeenLastCalledWith([gateway]);
	});

	it('stops after maxAttempts when no beacons are broadcast', async () => {
		const fetchGateways = vi.fn().mockResolvedValue([]);
		const delayFn = vi.fn().mockResolvedValue(undefined);

		const result = await scanBrewLinkGateways(fetchGateways, {
			maxAttempts: 4,
			intervalMs: 1500,
			delayFn
		});

		expect(result).toEqual([]);
		expect(fetchGateways).toHaveBeenCalledTimes(4);
		expect(delayFn).toHaveBeenCalledTimes(3);
	});

	it('stops cleanly when aborted mid-scan', async () => {
		const controller = new AbortController();
		const fetchGateways = vi.fn().mockResolvedValue([]);
		const delayFn = vi.fn().mockImplementation(async () => {
			controller.abort();
		});

		const result = await scanBrewLinkGateways(fetchGateways, {
			maxAttempts: 5,
			intervalMs: 1500,
			signal: controller.signal,
			delayFn
		});

		expect(result).toEqual([]);
		expect(fetchGateways).toHaveBeenCalledTimes(1);
	});
});
