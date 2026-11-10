// Smoke test for the dev environment. Flags: --start-api (run the API in background), --check-ready-503.
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { composeArgs, startApi } from './dev.mjs';
import { parseEnv } from './lib/env-file.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const env = parseEnv(readFileSync(join(root, 'deploy', '.env'), 'utf8'));
const api = `http://127.0.0.1:${env.API_PORT || 5080}`;
const gateway = `http://127.0.0.1:${env.GATEWAY_PORT || 8080}`;
const flags = new Set(process.argv.slice(2));
const results = [];

const record = (name, ok, detail = '') => results.push({ name, ok, detail });
const sleep = (ms) => new Promise((resolve) => { setTimeout(resolve, ms); });

async function status(url, init) {
  try {
    return (await fetch(url, { ...init, signal: AbortSignal.timeout(5000) })).status;
  } catch {
    return 0;
  }
}

async function waitFor(url, expected, seconds) {
  const deadline = Date.now() + seconds * 1000;
  let last = 0;
  while (Date.now() < deadline) {
    last = await status(url);
    if (last === expected) return true;
    await sleep(1000);
  }
  return last === expected;
}

function compose(...args) {
  return spawnSync('docker', [...composeArgs, ...args], { cwd: root, encoding: 'utf8' });
}

function stopProcessTree(child) {
  if (!child || child.exitCode !== null) return;
  if (process.platform === 'win32') spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
  else child.kill('SIGTERM');
}

let apiProcess;
try {
  const ps = compose('ps', '--format', 'json');
  const services = ps.stdout.trim().split(/\r?\n/).filter(Boolean).flatMap((line) => [].concat(JSON.parse(line)));
  for (const name of ['postgres', 'redis', 'rabbitmq', 'traefik']) {
    const service = services.find((item) => item.Service === name);
    record(`compose ${name} healthy`, service?.Health === 'healthy', service ? `${service.State}/${service.Health}` : 'không chạy');
  }

  if (flags.has('--start-api')) apiProcess = startApi('ignore');
  record('API /health/live 200', await waitFor(`${api}/health/live`, 200, flags.has('--start-api') ? 180 : 10));
  record('API /health/ready 200', await waitFor(`${api}/health/ready`, 200, 30));

  const notFound = await fetch(`${gateway}/api/v1/__smoke`, { signal: AbortSignal.timeout(5000) });
  const body = await notFound.text();
  record('Traefik /api 404 problem+json', notFound.status === 404
    && notFound.headers.get('content-type')?.startsWith('application/problem+json') && body.includes('correlationId'),
  `${notFound.status} ${notFound.headers.get('content-type')}`);
  record('nosniff', notFound.headers.get('x-content-type-options') === 'nosniff');
  record('X-Frame-Options DENY', notFound.headers.get('x-frame-options') === 'DENY');
  record('Referrer-Policy', notFound.headers.get('referrer-policy') === 'strict-origin-when-cross-origin');
  record('không có CORS', !notFound.headers.has('access-control-allow-origin'));

  const branches = await fetch(`${gateway}/api/v1/branches`, { signal: AbortSignal.timeout(5000) });
  const list = branches.ok ? await branches.json() : null;
  record('Traefik /api/v1/branches 200, mảng 6 khóa', Array.isArray(list)
    && list.every((item) => Object.keys(item).sort().join(',') === 'address,closeTime,id,name,openTime,phone'),
  `${branches.status}, ${Array.isArray(list) ? list.length : '?'} cơ sở`);

  const tooLarge = await status(`${gateway}/api/v1/__smoke`, {
    method: 'POST', body: Buffer.alloc(2 * 1024 * 1024, 97), headers: { 'content-type': 'application/octet-stream' },
  });
  record('body 2 MB -> 413', tooLarge === 413, String(tooLarge));

  const raw = await fetch(`${gateway}/api/rawdata`, { signal: AbortSignal.timeout(5000) });
  record('/api/rawdata do API trả (không lộ Traefik API)',
    raw.headers.get('content-type')?.startsWith('application/problem+json') === true, String(raw.status));

  if (flags.has('--check-ready-503')) {
    compose('stop', 'postgres');
    try {
      record('ready 503 khi tắt Postgres', await waitFor(`${api}/health/ready`, 503, 15));
    } finally {
      compose('start', 'postgres');
    }
    record('ready 200 khi bật lại Postgres', await waitFor(`${api}/health/ready`, 200, 30));
  }
} catch (error) {
  record('smoke chạy hết', false, error.message);
} finally {
  stopProcessTree(apiProcess);
}

for (const { name, ok, detail } of results) console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? `  (${detail})` : ''}`);
process.exitCode = results.every((result) => result.ok) ? 0 : 1;
