import { env } from '$env/dynamic/public';
import { resolveRuntimeConfig } from '$lib/config/runtime';
import type { LayoutServerLoad } from './$types';

export const load: LayoutServerLoad = () => {
	return {
		runtimeConfig: resolveRuntimeConfig(
			env,
			typeof process !== 'undefined' ? process.env : undefined
		)
	};
};
