// Dev environment: up | down [--volumes] | api | db <migrate|seed> [...]. Arguments are passed as arrays (P21).
import { spawn, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join, resolve } from 'node:path';
import { buildApiEnv, ensureEnvFile } from './lib/env-file.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const envPath = join(root, 'deploy', '.env');
const examplePath = join(root, 'deploy', '.env.example');
export const composeArgs = ['compose', '-f', join(root, 'deploy', 'compose.dev.yml'), '--env-file', envPath];

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { stdio: 'inherit', cwd: root, ...options });
  if (result.error) throw result.error;
  return result.status ?? 1;
}

function requireDocker() {
  const result = spawnSync('docker', ['info', '--format', '{{.ServerVersion}}'], { stdio: 'ignore' });
  if (result.error || result.status !== 0) throw new Error('Docker chưa chạy: mở Docker Desktop rồi thử lại.');
}

function hostEnv() {
  const { values } = ensureEnvFile(examplePath, envPath);
  return { values, env: { ...process.env, ...buildApiEnv(values) } };
}

const dotnetRun = ['run', '--project', join(root, 'src', 'Bepthaymao.Api'), '--no-launch-profile'];

export function startApi(stdio = 'inherit') {
  const { values, env } = hostEnv();
  const url = `http://${values.API_BIND_ADDRESS || '127.0.0.1'}:${values.API_PORT || 5080}`;
  return spawn('dotnet', [...dotnetRun, '--urls', url], { stdio, cwd: root, env });
}

async function main([command, ...rest]) {
  switch (command) {
    case 'up': {
      requireDocker();
      const { created } = ensureEnvFile(examplePath, envPath);
      if (created) console.log('Đã tạo deploy/.env với mật khẩu ngẫu nhiên.');
      return run('docker', [...composeArgs, 'up', '-d', '--wait']);
    }
    case 'down':
      requireDocker();
      return run('docker', [...composeArgs, 'down', ...(rest.includes('--volumes') ? ['--volumes'] : [])]);
    case 'api': {
      const child = startApi();
      return await new Promise((resolve) => child.on('exit', (code) => resolve(code ?? 1)));
    }
    case 'db': {
      // dotnet run switches the working directory to the project, so hand the CLI absolute seed paths.
      const args = rest.map((value, index) => (rest[index - 1] === '--file' ? resolve(value) : value));
      const env = { SEED_BRANCHES_FILE: join(root, 'seed', 'branches.sample.json'), ...hostEnv().env };
      return run('dotnet', [...dotnetRun, '--', 'db', ...args], { env });
    }
    default:
      console.error('Cách dùng: node scripts/dev.mjs up | down [--volumes] | api | db <migrate|seed> [--file <path>]');
      return 2;
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  main(process.argv.slice(2)).then((code) => { process.exitCode = code; }, (error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
