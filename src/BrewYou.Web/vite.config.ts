import { defineConfig } from 'vitest/config';
import tailwindcss from '@tailwindcss/vite';
import adapter from '@sveltejs/adapter-auto';
import { sveltekit } from '@sveltejs/kit/vite';

export default defineConfig({
	plugins: [
		tailwindcss(),
		sveltekit({
			compilerOptions: {
				// Force runes mode for the project, except for libraries. Can be removed in svelte 6.
				runes: ({ filename }) =>
					filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},

			// adapter-auto only supports some environments, see https://svelte.dev/docs/kit/adapter-auto for a list.
			// If your environment is not supported, or you settled on a specific environment, switch out the adapter.
			// See https://svelte.dev/docs/kit/adapters for more information about adapters.
			adapter: adapter()
		})
	],
	envPrefix: ['PUBLIC_', 'VITE_'],
	server: {
		port: process.env.PORT ? parseInt(process.env.PORT, 10) : 3000,
		strictPort: true
	},
	preview: {
		port: 3000,
		strictPort: true
	},
	test: {
		expect: { requireAssertions: true },
		coverage: {
			provider: 'v8',
			reporter: ['text', 'json-summary', 'html'],
			reportsDirectory: './coverage',
			include: ['src/lib/**/*.{js,ts}'],
			exclude: [
				'src/lib/types/**',
				'src/lib/**/types.ts',
				'src/lib/index.ts',
				'src/lib/exporters/index.ts',
				'src/lib/auth/gis.ts',
				'src/lib/**/*.d.ts',
				'src/lib/**/*.{test,spec}.{js,ts}',
				'src/lib/vitest-examples/**'
			],
			thresholds: {
				lines: 80,
				statements: 80,
				functions: 80,
				branches: 65
			}
		},
		projects: [
			{
				extends: './vite.config.ts',
				test: {
					name: 'server',
					environment: 'node',
					include: ['src/**/*.{test,spec}.{js,ts}'],
					exclude: ['src/**/*.svelte.{test,spec}.{js,ts}']
				}
			}
		]
	}
});
