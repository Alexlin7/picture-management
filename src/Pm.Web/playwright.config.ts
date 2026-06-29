import { defineConfig, devices } from '@playwright/test';

// E2E runner 設定(對齊 docs/design/2026-06-29-e2e-test-hardening.md §三)。
// 重點:baseURL 收斂 URL、testIdAttribute 對齊鐵則 #3 的 getByTestId、
//       webServer 自動起 .NET app(取代手動 dotnet run)、trace 只在重試時開。
export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  retries: process.env.CI ? 2 : 0,
  use: {
    baseURL: process.env.PM_E2E_BASE ?? 'http://localhost:5180',
    testIdAttribute: 'data-testid',
    trace: 'on-first-retry',
  },
  // runner 自動起 app、serve 已 build 的前端;本機若已手動起 app 則沿用既有 server。
  webServer: {
    command: 'dotnet run --project ../../src/Pm.Api',
    url: 'http://localhost:5180',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
