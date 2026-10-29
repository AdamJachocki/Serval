/* Disposable full browser -> Web -> Unix socket -> Agent -> systemd check.
 * Requires Docker Linux containers, PowerShell, .NET, Playwright and Chromium.
 * No screenshots, traces, credentials, environment values or raw process output are retained. */
const { chromium } = require('playwright');
const { spawnSync } = require('node:child_process');
const { randomBytes } = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const root = path.resolve(__dirname, '../../..');
const name = 'serval-environment-e2e-' + randomBytes(6).toString('hex');
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), name));
let imageBuilt = false, created = false, browser;
let phase = 'prepare';
function check(condition) { if (!condition) throw new Error('check'); }
function run(executable, args, input) {
    const result = spawnSync(executable, args, { cwd: root, input, encoding: 'utf8', timeout: 600000, windowsHide: true, maxBuffer: 8 * 1024 * 1024 });
    if (result.status !== 0 || result.error) throw new Error('process');
    return result.stdout.trim();
}
const docker = (...args) => run('docker', args);
const exec = (...args) => docker('exec', name, ...args);
(async () => {
    try {
        const architecture = docker('info', '--format', '{{.Architecture}}');
        const runtime = ['amd64', 'x86_64'].includes(architecture) ? 'linux-x64' : 'linux-arm64';
        check(['amd64', 'x86_64', 'arm64', 'aarch64'].includes(architecture));
        run('pwsh', ['-NoProfile', '-File', 'eng/visual-studio-compose/prepare-context.ps1', '-Runtime', runtime]);
        phase = 'build';
        docker('build', '-t', name, 'artifacts/docker-visual-studio/context');
        imageBuilt = true;
        phase = 'start';
        docker('run', '-d', '-t', '--name', name, '--privileged', '--cgroupns=private', '--ipc=private',
            '--tmpfs', '/run:rw,exec,mode=755', '--tmpfs', '/run/lock:rw,mode=755',
            '-p', '127.0.0.1::8080', '--env', 'ASPNETCORE_ENVIRONMENT=Development',
            '--env', 'ASPNETCORE_URLS=http://0.0.0.0:8080', name);
        created = true;
        exec('timeout', '90s', '/opt/serval/development/start-agent.sh');
        const first = randomBytes(32).toString('hex');
        const second = randomBytes(32).toString('hex');
        const directory = '/run/systemd/system/serval-demo-running.service.d';
        const source = directory + '/95-environment-e2e.conf';
        exec('mkdir', '-m', '0755', directory);
        run('docker', ['exec', '-i', name, 'tee', source], `[Service]\nEnvironment=E2E_ALPHA=${first}\nEnvironment=E2E_SECOND=${second}\n`);
        exec('timeout', '5s', 'systemctl', 'daemon-reload');
        docker('exec', '-d', name, '/opt/serval/development/start-web.sh');
        const binding = docker('port', name, '8080/tcp');
        check(/^127\.0\.0\.1:\d+$/.test(binding));
        const base = 'http://localhost:' + binding.split(':')[1];
        let ready = false;
        for (let attempt = 0; attempt < 30; attempt++) {
            try { exec('/opt/serval/development/verify-development-host.sh'); ready = true; break; }
            catch { await new Promise(resolve => setTimeout(resolve, 1000)); }
        }
        check(ready);
        const properties = '--property=ActiveState,SubState,InvocationID,StateChangeTimestampMonotonic,ExecMainStartTimestampMonotonic,ExecMainExitTimestampMonotonic,NeedDaemonReload';
        const beforeState = exec('systemctl', 'show', 'serval-demo-running.service', properties);
        const beforeSource = exec('sha256sum', source);
        phase = 'browser';
        browser = await chromium.launch({ headless: true, executablePath: process.env.SERVAL_BROWSER_EXECUTABLE || undefined });
        const context = await browser.newContext();
        const page = await context.newPage();
        const anonymous = await context.request.get(base + '/Services/Details?handler=Environment&service=serval-demo-running.service');
        check(anonymous.status() === 401);
        phase = 'login';
        await page.goto(base + '/Login');
        await page.locator('input[name=Username]').fill('serval-dev');
        // Public disposable account explicitly defined by CONTRIBUTING.md.
        await page.locator('input[name=Password]').fill('serval');
        const [loginResponse] = await Promise.all([
            page.waitForResponse(response => new URL(response.url()).pathname === '/Login' && response.request().method() === 'POST'),
            page.getByRole('button', { name: 'Zaloguj się', exact: true }).click()
        ]);
        phase = 'login-http-' + loginResponse.status();
        check(loginResponse.status() === 302);
        await page.waitForURL(url => !url.pathname.includes('Login'));
        phase = 'details';
        await page.goto(base + '/Services/Details?service=serval-demo-running.service');
        await page.getByRole('button', { name: 'Pokaż', exact: true }).first().waitFor();
        phase = 'metadata';
        const metadata = await context.request.get(base + '/Services/Details?handler=Environment&service=serval-demo-running.service');
        const text = await metadata.text();
        check(metadata.ok() && !text.includes(first) && !text.includes(second));
        check(metadata.headers()['cache-control'].includes('no-store'));
        phase = 'denials';
        const noCsrf = await context.request.post(base + '/Services/Details?handler=Reveal', { form: { service: 'serval-demo-running.service', name: 'E2E_ALPHA' } });
        check(noCsrf.status() === 400);
        for (const service of ['serval-agent.service', 'ssh.service', '../bad.service']) {
            const denied = await context.request.get(base + '/Services/Details?handler=Environment&service=' + encodeURIComponent(service));
            check(denied.status() === 404);
        }
        const token = await page.locator('#environment-token input[name=__RequestVerificationToken]').inputValue();
        const protectedReveal = await context.request.post(base + '/Services/Details?handler=Reveal', {
            form: { service: 'serval-agent.service', name: 'E2E_ALPHA', __RequestVerificationToken: token }
        });
        check(protectedReveal.status() === 404);
        phase = 'reveal';
        const row = page.locator('#environment-rows > div').filter({ has: page.locator('strong', { hasText: 'E2E_ALPHA' }) });
        const other = page.locator('#environment-rows > div').filter({ has: page.locator('strong', { hasText: 'E2E_SECOND' }) });
        await row.getByRole('button', { name: 'Pokaż', exact: true }).click();
        await page.waitForFunction(() => [...document.querySelectorAll('#environment-rows > div')].some(row => row.querySelector('strong').textContent === 'E2E_ALPHA' && row.querySelector('pre').textContent !== '••••••••'));
        check(await row.locator('pre').textContent() === first);
        check(await other.locator('pre').textContent() === '••••••••');
        await row.getByRole('button', { name: 'Ukryj', exact: true }).click();
        check(await row.locator('pre').textContent() === '••••••••');
        phase = 'observations';
        check(exec('systemctl', 'show', 'serval-demo-running.service', properties) === beforeState);
        check(exec('sha256sum', source) === beforeSource);
        docker('cp', name + ':/var/lib/serval/policy.db', path.join(temporary, 'policy.db'));
        const database = fs.readFileSync(path.join(temporary, 'policy.db'));
        check(!database.includes(Buffer.from(first)) && !database.includes(Buffer.from(second)));
        const { DatabaseSync } = require('node:sqlite');
        const db = new DatabaseSync(path.join(temporary, 'policy.db'), { readOnly: true });
        try {
            check(db.prepare("SELECT COUNT(*) AS n FROM audit WHERE operation='RevealEnvironmentVariable' AND variable_name='E2E_ALPHA' AND outcome='Success'").get().n === 1);
        } finally { db.close(); database.fill(0); }
        console.log('Full browser-to-Agent systemd flow passed; values omitted.');
    } finally {
        if (browser) await browser.close().catch(() => {});
        if (created) docker('rm', '--force', name);
        if (imageBuilt) docker('image', 'rm', '--force', name);
        if (path.dirname(temporary) !== os.tmpdir() || !path.basename(temporary).startsWith(name)) throw new Error('cleanup');
        fs.rmSync(temporary, { recursive: true });
    }
})().catch(() => { console.error('Full-flow failure at phase=' + phase + ' (private diagnostics suppressed).'); process.exitCode = 1; });
