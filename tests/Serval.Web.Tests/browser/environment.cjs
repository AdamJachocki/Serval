/* Run through EnvironmentPagesTests with SERVAL_BROWSER_TESTS=1 and Playwright on NODE_PATH.
 * Never record screenshots, traces, DOM dumps or assertion values while a row is revealed. */
const { chromium } = require('playwright');
const assert = (condition, category) => { if (!condition) throw new Error(category); };
let stage = 'launch';
(async () => {
    const browser = await chromium.launch({ headless: true, executablePath: process.env.SERVAL_BROWSER_EXECUTABLE || undefined });
    try {
        const context = await browser.newContext({
            viewport: { width: 1280, height: 800 },
            ignoreHTTPSErrors: true
        });
        await context.addCookies([{ name: '__Host-ServalSession', value: 'opaque-token',
            url: new URL(process.argv[2]).origin, secure: true, httpOnly: true, sameSite: 'Strict' }]);
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', () => errors.push('runtime'));
        page.on('console', message => { if (message.type() === 'error') errors.push('console'); });
        let reveals = 0;
        let metadataStatus = 0;
        page.on('response', response => { if (response.url().includes('handler=Environment')) metadataStatus = response.status(); });
        page.on('request', request => { if (request.url().includes('handler=Reveal')) reveals++; });
        const url = new URL('/Services/Details?service=worker.service', process.argv[2]).href;
        stage = 'navigation';
        await page.goto(url);
        stage = 'metadata';
        try {
            await page.getByRole('button', { name: 'Pokaż', exact: true }).first().waitFor({ timeout: 10000 });
        } catch {
            console.error('state=section-' + await page.locator('#service-environment').count() + '-http-' + metadataStatus + '-errors-' + errors.length);
            throw new Error('metadata');
        }
        assert((await page.title()).includes('Service details'), 'page-identity');
        const values = page.locator('#environment-rows pre');
        const masked = async () => values.evaluateAll(elements => elements.length === 2 && elements.every(e => e.textContent === '••••••••'));
        assert(await masked() && reveals === 0, 'initial-masks');
        stage = 'reveal';
        await page.getByRole('button', { name: 'Pokaż', exact: true }).first().focus();
        await page.keyboard.press('Enter');
        await page.waitForFunction(() => document.querySelector('#environment-rows pre').textContent !== '••••••••');
        assert(await values.nth(1).textContent() === '••••••••', 'single-row');
        assert(await page.locator('#environment-rows inert').count() === 0, 'text-only');
        stage = 'timer';
        await page.clock.install();
        await page.clock.fastForward(24 * 60 * 60 * 1000);
        assert(await values.first().textContent() !== '••••••••', 'no-timer');
        await page.getByRole('button', { name: 'Ukryj', exact: true }).click();
        assert(await masked(), 'manual-hide');
        await page.getByRole('button', { name: 'Pokaż', exact: true }).first().click();
        await page.waitForFunction(() => document.querySelector('#environment-rows pre').textContent !== '••••••••');
        assert(reveals === 2, 'fresh-request');
        stage = 'refresh';
        await page.reload();
        await page.getByRole('button', { name: 'Pokaż', exact: true }).first().waitFor();
        assert(await masked(), 'refresh-reset');
        stage = 'late-response';
        let release;
        await page.route('**/*handler=Reveal*', async route => {
            await new Promise(resolve => { release = resolve; });
            await route.continue().catch(() => {});
        });
        await page.getByRole('button', { name: 'Pokaż', exact: true }).first().click();
        await page.getByRole('button', { name: 'Ukryj', exact: true }).click();
        if (release) release();
        await page.unrouteAll({ behavior: 'wait' });
        assert(await masked(), 'late-response');
        await page.getByRole('button', { name: 'Pokaż', exact: true }).first().click();
        await page.waitForFunction(() => document.querySelector('#environment-rows pre').textContent !== '••••••••');
        stage = 'history';
        await page.goto(new URL('/Privacy', url).href);
        await page.goBack();
        await page.getByRole('button', { name: 'Pokaż', exact: true }).first().waitFor();
        assert(await masked(), 'history-reset');
        stage = 'layout';
        await page.setViewportSize({ width: 375, height: 812 });
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), 'narrow-layout');
        assert(await page.evaluate(() => localStorage.length === 0 && sessionStorage.length === 0), 'no-browser-storage');
        assert(errors.length === 0, 'console-health');
        stage = 'empty-value';
        await page.getByRole('button', { name: 'Pokaż', exact: true }).nth(1).click();
        await page.getByText('Pusta wartość', { exact: true }).waitFor();
        assert(await values.nth(1).textContent() === '', 'empty-disclosure');
        await page.getByRole('button', { name: 'Ukryj', exact: true }).click();
        stage = 'metadata-states';
        let metadataFixture = { service: 'worker.service', scope: 0, sources: [], variables: [], canReveal: false };
        let metadataCode = 200;
        await page.route('**/*handler=Environment*', route => route.fulfill({ status: metadataCode, contentType: 'application/json', body: JSON.stringify(metadataFixture) }));
        await page.reload();
        await page.getByText('Brak deklaracji środowiska.', { exact: true }).waitFor();
        metadataFixture = { service: 'worker.service', scope: 0,
            sources: [{ id: 0, kind: 0, isOptional: false, isMissing: false }],
            variables: [{ name: 'CONFIG', winningSourceId: 0 }], canReveal: false };
        await page.reload();
        await page.getByText('Wartości są ukryte.', { exact: true }).waitFor();
        assert(await page.getByRole('button', { name: 'Pokaż', exact: true }).count() === 0, 'metadata-only');
        metadataCode = 503;
        metadataFixture = { error: 'Ta konfiguracja nie jest obsługiwana.' };
        await page.reload();
        await page.getByText('Ta konfiguracja nie jest obsługiwana.', { exact: true }).waitFor();
        assert(await page.locator('#environment-rows > div').count() === 0, 'no-partial-rows');
        assert(await page.getByRole('button', { name: 'Spróbuj ponownie', exact: true }).isVisible(), 'retry');
        metadataCode = 404;
        await page.getByRole('button', { name: 'Spróbuj ponownie', exact: true }).click();
        await page.getByText('Brak dostępu do środowiska usługi.', { exact: true }).waitFor();
        assert(await page.getByRole('heading', { name: 'Service details', exact: true }).isVisible(), 'independent-details');
        console.log('Environment browser checks passed.');
    } finally { await browser.close(); }
})().catch(error => { console.error('stage=' + stage); console.error('network=' + ((error.message || '').match(/net::[A-Z_]+/) || ['unknown'])[0]); process.exitCode = 1; });
