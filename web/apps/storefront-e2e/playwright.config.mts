import { defineConfig, devices } from '@playwright/test';
import { nxE2EPreset } from '@nx/playwright/preset';
import { workspaceRoot } from '@nx/devkit';

// For CI, you may want to set BASE_URL to the deployed application.
const baseURL = process.env['BASE_URL'] || 'http://localhost:4200';

/**
 * Read environment variables from file.
 * https://github.com/motdotla/dotenv
 */
// import 'dotenv/config';

/**
 * See https://playwright.dev/docs/test-configuration.
 *
 * Generated as a .mts file so Node forces ESM regardless of workspace
 * `type`. Playwright routes `.mts` through its ESM loader (dynamic import,
 * bypassing the pirates CJS-compile path), and Nx's native TS strip loads
 * `.mts` directly. Playwright's configLoader auto-discovers
 * `playwright.config.mts` via its extension list
 * (.ts/.js/.mts/.mjs/.cts/.cjs).
 */
export default defineConfig({
  ...nxE2EPreset(import.meta.dirname, { testDir: './src' }),
  /* Shared settings for all the projects below. See https://playwright.dev/docs/api/class-testoptions. */
  use: {
    baseURL,
    /* Collect trace when retrying the failed test. See https://playwright.dev/docs/trace-viewer */
    trace: 'on-first-retry',
  },
  /*
   * Two servers: the storefront, and the real API it renders against. The API must have the
   * sample catalogue imported (Catalog:SeedFile in Development), because the assertions count
   * products from tools/data/catalog.json. It needs a migrated database, so it is reused when
   * already running rather than being something this config can always start cold.
   */
  webServer: [
    {
      command: 'dotnet run --project ../src/UPBazaar.Api --urls http://localhost:5199',
      url: 'http://localhost:5199/api/v1/catalog/categories',
      reuseExistingServer: true,
      timeout: 180_000,
      cwd: workspaceRoot,
    },
    {
      command: 'npm run start:storefront',
      url: 'http://localhost:4200',
      reuseExistingServer: true,
      timeout: 180_000,
      cwd: workspaceRoot,
    },
  ],
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
