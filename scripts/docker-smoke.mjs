import { spawnSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { setTimeout } from 'node:timers/promises';
import { readEnvFile, buildApiEnv } from './lib/env-file.mjs';
const containers = [];
const directory = mkdtempSync(join(tmpdir(), 'bep-docker-'));
function docker(args) {
  const result = spawnSync('docker', args, { encoding: 'utf8' });
  if (result.error || result.status !== 0) throw new Error(`Docker ${args[0]} failed.`);
  return result.stdout.trim();
}
try {
  const settings = readEnvFile('deploy/.env');
  const env = buildApiEnv(settings);
  const selected = { ASPNETCORE_ENVIRONMENT: 'Development',
    ConnectionStrings__Default: env.ConnectionStrings__Default.replace('Host=127.0.0.1', 'Host=postgres').replace(`Port=${settings.POSTGRES_PORT}`, 'Port=5432'),
    ConnectionStrings__Redis: `redis:6379,password=${settings.REDIS_PASSWORD}`,
    ConnectionStrings__RabbitMq: env.ConnectionStrings__RabbitMq.replace(`127.0.0.1:${settings.RABBITMQ_PORT}`, 'rabbitmq:5672'),
  };
  const file = join(directory, 'runtime.env');
  writeFileSync(file, Object.entries(selected).map(([key, value]) => `${key}=${value}`).join('\n'), { mode: 0o600 });
  for (const target of ['api', 'worker']) {
    const image = `bepthaymao-${target}:dev`;
    const user = docker(['image', 'inspect', '--format', '{{.Config.User}}', image]);
    if (!user || ['0', 'root', '0:0'].includes(user)) throw new Error(`${target} is root.`);
    const name = `bep-smoke-${target}-${Date.now()}`;
    containers.push(name);
    docker(['run', '-d', '--name', name, '--network', 'bepthaymao-dev_default', '--env-file', file, ...(target === 'api' ? ['-p', '127.0.0.1:5081:8080'] : []), image]);
  }
  let ready = false;
  for (let index = 0; index < 60; index++) {
    try { ready = (await fetch('http://127.0.0.1:5081/health/ready')).ok; } catch { /* Wait for container startup, bounded to 30 seconds. */ }
    if (ready) break;
    await setTimeout(500);
  }
  if (!ready) throw new Error('Container readiness failed.');
  const response = await fetch('http://127.0.0.1:5081/api/v1/branches');
  if (!response.ok || (await response.json()).length !== 2) throw new Error('Container branch response failed.');
  if (docker(['inspect', '--format', '{{.State.Running}}', containers[1]]) !== 'true') throw new Error('Worker exited.');
  console.log('API ready, 2 public branches, worker running; both images non-root.');
} finally {
  for (const name of containers) spawnSync('docker', ['rm', '-f', name], { stdio: 'ignore' });
  rmSync(directory, { recursive: true, force: true });
}
