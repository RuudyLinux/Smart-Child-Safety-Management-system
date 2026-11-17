import { defineConfig, devices } from '@playwright/test';
import { URLS } from './support/stack';

// The whole suite runs against an isolated stack (support/stack.ts): its own throwaway
// database, API (:5375), Admin Web (:5180) and a stub face service (:8101).
// The developer's running system and childguard_db are never touched.
const ADMIN_STATE = '.e2e/admin.json';

export default defineConfig({
  testDir: './specs',
  globalSetup: './support/global-setup.ts',
  globalTeardown: './support/global-teardown.ts',
  timeout: 45_000,
  expect: { timeout: 8_000 },
  fullyParallel: false,
  workers: 3,
  retries: 0,
  reporter: [
    ['list'],
    ['html', { open: 'never', outputFolder: 'playwright-report' }],
    ['json', { outputFile: 'test-results/results.json' }],
  ],
  use: {
    baseURL: URLS.web,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    actionTimeout: 10_000,
  },
  projects: [
    // 'profile' changes the only admin's password, so it runs after everything else.
    { name: 'setup', testMatch: /auth\.setup\.ts/, teardown: 'profile' },
    {
      name: 'profile',
      testMatch: /profile\.spec\.ts/,
      use: { ...devices['Desktop Chrome'], viewport: { width: 1366, height: 768 }, storageState: ADMIN_STATE },
    },
    {
      name: 'chromium',
      dependencies: ['setup'],
      testIgnore: /responsive\.spec\.ts|profile\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1366, height: 768 },
        storageState: ADMIN_STATE,
        permissions: ['camera'],
        launchOptions: { args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream'] },
      },
    },
    { name: 'firefox', dependencies: ['setup'], grep: /@smoke/, testIgnore: /profile\.spec\.ts/, use: { ...devices['Desktop Firefox'], storageState: ADMIN_STATE } },
    { name: 'webkit', dependencies: ['setup'], grep: /@smoke/, testIgnore: /profile\.spec\.ts/, use: { ...devices['Desktop Safari'], storageState: ADMIN_STATE } },
    { name: 'responsive', dependencies: ['setup'], testMatch: /responsive\.spec\.ts/, use: { ...devices['Desktop Chrome'], storageState: ADMIN_STATE } },
  ],
});
