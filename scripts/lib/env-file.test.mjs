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
    for (const key of ['POSTGRES_PASSWORD', 'REDIS_PASSWORD', 'RABBITMQ_DEFAULT_PASS']) assert.match(first[key], /^[A-Za-z0-9_-]{32}$/);
    const env = buildApiEnv({ ...first, POSTGRES_PORT: '5432', POSTGRES_DB: 'sample', POSTGRES_USER: 'sample', REDIS_PORT: '6379', RABBITMQ_PORT: '5672', RABBITMQ_DEFAULT_USER: 'sample' });
    assert.equal(env.ASPNETCORE_ENVIRONMENT, 'Development');
    assert.ok(env.AllowedHosts.includes('localhost'));
    assert.equal(env.SePay__Mode, 'Fake');
  } finally { rmSync(directory, { recursive: true, force: true }); }
});

test('JWT signing key is generated once with 48 bytes and auth settings reach API', () => {
  const directory = mkdtempSync(join(tmpdir(), 'bep-jwt-env-'));
  try {
    const example = join(directory, 'example');
    const actual = join(directory, 'actual');
    writeFileSync(example, 'Jwt__SigningKey=\nAuth__RefreshCookie__Secure=false\n');
    const first = ensureEnvFile(example, actual);
    const second = ensureEnvFile(example, actual);
    assert.equal(first.Jwt__SigningKey, second.Jwt__SigningKey);
    assert.ok(Buffer.from(first.Jwt__SigningKey, 'base64').length >= 48);
    const api = buildApiEnv(first);
    assert.equal(api.Jwt__SigningKey, first.Jwt__SigningKey);
    assert.equal(api.Auth__RefreshCookie__Secure, 'false');
  } finally { rmSync(directory, { recursive: true, force: true }); }
});
