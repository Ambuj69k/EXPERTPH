import { defineConfig } from '@playwright/test';

// End-to-end UI tests against the running dev servers (API :5101 + Vite :5173).
// Start them first (see README), then: npx playwright test
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  retries: 0,
  workers: 1,
  reporter: [['list']],
  outputDir: 'D:/EXPERTPH/.scratch/playwright-results',
  use: {
    baseURL: 'http://localhost:5173',
    headless: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
});
