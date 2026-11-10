// Runs the built api/worker images on the compose network against the seeded dev database.
// Needs: npm run dev:up, dev:db -- migrate/seed, npm run docker:build.
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildApiEnv, parseEnv } from './lib/env-file.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const network = 'bepthaymao-dev_default';
const base = 'http://127.0.0.1:5081';
const containers = { api: 'bepthaymao-smoke-api', worker: 'bepthaymao-smoke-worker' };
const results = [];
const record = (name, ok, detail = '') => results.push({ name, ok, detail });
const sleep = (ms) => new Promise((resolve) => { setTimeout(resolve, ms); });
const docker = (...args) => spawnSync('docker', args, { encoding: 'utf8' });

async function waitStatus(url, expected, seconds) {
  for (let elapsed = 0; elapsed < seconds; elapsed += 1) {
    try {
      if ((await fetch(url, { signal: AbortSignal.timeout(3000) })).status === expected) return true;
    } catch { /* not up yet */ }
    await sleep(1000);
  }
  return false;
}

// Secrets go through a 0600 env file, never the command line.
const secretsDirectory = mkdtempSync(join(tmpdir(), 'bep-docker-smoke-'));
try {
  const env = buildApiEnv(parseEnv(readFileSync(join(root, 'deploy', '.env'), 'utf8')), { inCompose: true });
  const envFile = join(secretsDirectory, 'api.env');
  writeFileSync(envFile, Object.entries(env).map(([key, value]) => `${key}=${value}`).join('\n'), { mode: 0o600 });

  for (const image of ['bepthaymao-api:dev', 'bepthaymao-worker:dev']) {
    const user = docker('image', 'inspect', '--format', '{{.Config.User}}', image).stdout.trim();
    record(`${image} không chạy root`, user !== '' && user !== 'root' && user !== '0', `user=${user || '(rỗng)'}`);
  }

  const api = docker('run', '-d', '--rm', '--name', containers.api, '--network', network, '--env-file', envFile,
    '-p', '127.0.0.1:5081:8080', 'bepthaymao-api:dev');
  record('api container khởi động', api.status === 0, api.stderr.trim());
  record('api /health/live 200', await waitStatus(`${base}/health/live`, 200, 60));
  record('api /health/ready 200', await waitStatus(`${base}/health/ready`, 200, 30));
  const response = await fetch(`${base}/api/v1/branches`, { signal: AbortSignal.timeout(5000) });
  const branches = response.ok ? await response.json() : null;
  record('api /api/v1/branches 200 mảng 2 cơ sở', Array.isArray(branches) && branches.length === 2,
    `${response.status}, ${Array.isArray(branches) ? branches.length : '?'}`);

  const worker = docker('run', '-d', '--rm', '--name', containers.worker, '--network', network, '--env-file', envFile,
    'bepthaymao-worker:dev');
  await sleep(5000);
  const running = docker('inspect', '--format', '{{.State.Running}}', containers.worker).stdout.trim();
  record('worker vẫn chạy sau 5 giây', worker.status === 0 && running === 'true', running || worker.stderr.trim());
} catch (error) {
  record('docker smoke chạy hết', false, error.message);
} finally {
  for (const name of Object.values(containers)) docker('rm', '-f', name);
  rmSync(secretsDirectory, { recursive: true, force: true });
}

for (const { name, ok, detail } of results) console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? `  (${detail})` : ''}`);
process.exitCode = results.every((result) => result.ok) ? 0 : 1;
