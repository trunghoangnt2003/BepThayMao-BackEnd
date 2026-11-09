import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { ensureEnvFile, buildApiEnv } from './env-file.mjs';
test('dev secrets are generated once and host allowlist supports Docker SSR', () => {
  const directory = mkdtempSync(join(tmpdir(), 'bep-env-'));
  try {
    const example = join(directory, 'example');
    const actual = join(directory, 'actual');
    writeFileSync(example, 'POSTGRES_PASSWORD=\nREDIS_PASSWORD=\nRABBITMQ_DEFAULT_PASS=\n');
    const first = ensureEnvFile(example, actual);
    const second = ensureEnvFile(example, actual);
    assert.deepEqual(first, second);
    for (const value of Object.values(first)) assert.match(value, /^[A-Za-z0-9_-]{32}$/);
    const env = buildApiEnv({ ...first, POSTGRES_PORT: '5432', POSTGRES_DB: 'sample', POSTGRES_USER: 'sample', REDIS_PORT: '6379', RABBITMQ_PORT: '5672', RABBITMQ_DEFAULT_USER: 'sample' });
    assert.equal(env.ASPNETCORE_ENVIRONMENT, 'Development');
    assert.ok(env.AllowedHosts.includes('localhost'));
    assert.equal(env.SePay__Mode, 'Fake');
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
