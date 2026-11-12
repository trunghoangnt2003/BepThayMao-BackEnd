import { spawn, spawnSync } from 'node:child_process';
import { setTimeout } from 'node:timers/promises';
const args = process.argv.slice(2);
if (args.some((arg) => !['--start-api', '--check-ready-503'].includes(arg))) throw new Error('Unsupported smoke argument.');
const base = process.env.GATEWAY_URL ?? 'http://127.0.0.1:8080';
const api = process.env.API_URL ?? 'http://127.0.0.1:5080';
const child = args.includes('--start-api') ? spawn(process.execPath, ['scripts/dev.mjs', 'api'], { stdio: 'inherit' }) : null;
const compose = (command) => {
  const result = spawnSync('docker', ['compose', '-f', 'deploy/compose.dev.yml', '--env-file', 'deploy/.env', ...command], { stdio: 'inherit' });
  if (result.status !== 0) throw new Error('Compose operation failed.');
};
try {
  let ready = false;
  for (let attempt = 0; attempt < 100; attempt++) {
    try { ready = (await fetch(`${api}/health/ready`)).ok; } catch { /* Bounded startup wait. */ }
    if (ready) break;
    await setTimeout(200);
  }
  if (!ready) throw new Error('API is not ready.');
  const response = await fetch(`${base}/api/v1/branches`);
  if (!response.ok || (await response.json()).length !== 2) throw new Error('Gateway branch smoke failed.');
  if (args.includes('--check-ready-503')) {
    compose(['stop', 'postgres']);
    try {
      if ((await fetch(`${api}/health/ready`)).status !== 503) throw new Error('Ready did not fail after Postgres stopped.');
      if (!(await fetch(`${api}/health/live`)).ok) throw new Error('Live failed with infrastructure stopped.');
    } finally { compose(['start', '--wait', 'postgres']); }
  }
  console.log('Gateway branches and API health smoke passed.');
} finally { child?.kill(); }
