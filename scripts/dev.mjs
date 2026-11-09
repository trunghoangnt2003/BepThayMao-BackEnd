import { spawn } from 'node:child_process';
import { resolve } from 'node:path';
import { ensureEnvFile, buildApiEnv } from './lib/env-file.mjs';

const root = resolve(import.meta.dirname, '..');
const envPath = resolve(root, 'deploy/.env');
const env = ensureEnvFile(resolve(root, 'deploy/.env.example'), envPath);
const command = process.argv[2] ?? 'up';

if (command === 'up' || command === 'down') {
  const args = ['compose', '-f', 'deploy/compose.dev.yml', '--env-file', 'deploy/.env', command];
  if (command === 'up') args.push('-d', '--wait');
  if (command === 'down' && process.argv.includes('--volumes')) args.push('--volumes');
  process.exitCode = await new Promise(resolveCode => {
    const child = spawn('docker', args, { cwd: root, stdio: 'inherit' });
    child.on('exit', code => resolveCode(code ?? 1));
  });
} else if (command === 'api') {
  const child = spawn('dotnet', ['run', '--project', 'src/Bepthaymao.Api', '--urls', `http://${env.API_BIND_ADDRESS}:${env.API_PORT}`], { cwd: root, env: buildApiEnv(env), stdio: 'inherit' });
  process.on('SIGINT', () => child.kill('SIGINT'));
  child.on('exit', code => { process.exitCode = code ?? 1; });
} else {
  throw new Error('Usage: node scripts/dev.mjs <up|down|api> [--volumes]');
}
