import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
	webServer: { command: 'npm run build && npx vite preview --port 4173', port: 4173 },
	testMatch: '**/*.e2e.{ts,js}',
	projects: [
		{
			name: 'desktop-chrome',
			use: {
				...devices['Desktop Chrome'],
				viewport: { width: 1440, height: 900 }
			}
		},
		{
			name: 'desktop-compact',
			use: {
				...devices['Desktop Chrome'],
				viewport: { width: 1280, height: 800 }
			}
		},
		{
			name: 'tablet-ipad',
			use: {
				...devices['Desktop Chrome'],
				viewport: { width: 810, height: 1080 },
				hasTouch: true
			}
		},
		{
			name: 'mobile-iphone-14',
			use: {
				...devices['Desktop Chrome'],
				viewport: { width: 390, height: 844 },
				hasTouch: true,
				isMobile: true
			}
		},
		{
			name: 'mobile-pixel-7',
			use: {
				...devices['Pixel 7']
			}
		},
		{
			name: 'mobile-iphone-se',
			use: {
				...devices['Desktop Chrome'],
				viewport: { width: 375, height: 667 },
				hasTouch: true,
				isMobile: true
			}
		}
	]
});
