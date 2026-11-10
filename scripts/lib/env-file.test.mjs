import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { buildApiEnv, ensureEnvFile, fillSecrets, parseEnv } from './env-file.mjs';

test('parseEnv ignores comments and keeps everything after the first equals sign', () => {
  assert.deepEqual(parseEnv('# c\n\nA=1\nB = x=y \nbroken\n'), { A: '1', B: 'x=y' });
});

test('fillSecrets only fills empty secret keys', () => {
  const text = '# keep\nPOSTGRES_PASSWORD=\nREDIS_PASSWORD=set\nPOSTGRES_USER=\nRABBITMQ_DEFAULT_PASS=\n';
  assert.equal(fillSecrets(text, () => 'gen'),
    '# keep\nPOSTGRES_PASSWORD=gen\nREDIS_PASSWORD=set\nPOSTGRES_USER=\nRABBITMQ_DEFAULT_PASS=gen\n');
});

test('generated secrets are random url-safe strings', () => {
  const [first, second] = [fillSecrets('REDIS_PASSWORD=\n'), fillSecrets('REDIS_PASSWORD=\n')];
  assert.match(first, /^REDIS_PASSWORD=[A-Za-z0-9_-]{32}\n$/);
  assert.notEqual(first, second);
});

test('ensureEnvFile creates once and keeps existing secrets', () => {
  const directory = mkdtempSync(join(tmpdir(), 'bep-env-'));
  try {
    const example = join(directory, '.env.example');
    const env = join(directory, '.env');
    writeFileSync(example, 'POSTGRES_PASSWORD=\nPOSTGRES_USER=bepthaymao\n');
    const first = ensureEnvFile(example, env);
    const second = ensureEnvFile(example, env);
    assert.equal(first.created, true);
    assert.equal(second.created, false);
    assert.equal(second.values.POSTGRES_PASSWORD, first.values.POSTGRES_PASSWORD);
    assert.equal(readFileSync(example, 'utf8'), 'POSTGRES_PASSWORD=\nPOSTGRES_USER=bepthaymao\n');
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});

test('buildApiEnv builds loopback connection strings and escapes AMQP credentials', () => {
  const env = buildApiEnv({
    POSTGRES_PASSWORD: 'p', POSTGRES_PORT: '25432', REDIS_PASSWORD: 'r',
    RABBITMQ_DEFAULT_USER: 'u', RABBITMQ_DEFAULT_PASS: 'a/b', OTEL_EXPORTER_OTLP_ENDPOINT: '',
  });
  assert.equal(env.ConnectionStrings__Default, 'Host=127.0.0.1;Port=25432;Database=bepthaymao;Username=bepthaymao;Password=p');
  assert.equal(env.ConnectionStrings__Redis, '127.0.0.1:6379,password=r');
  assert.equal(env.ConnectionStrings__RabbitMq, 'amqp://u:a%2Fb@127.0.0.1:5672/');
  assert.equal(env.SePay__Mode, 'Fake');
  assert.equal('OTEL_EXPORTER_OTLP_ENDPOINT' in env, false);
});

test('buildApiEnv inCompose uses service names and internal ports', () => {
  const env = buildApiEnv({ POSTGRES_PASSWORD: 'p', POSTGRES_PORT: '25432', REDIS_PASSWORD: 'r', REDIS_PORT: '6380',
    RABBITMQ_DEFAULT_PASS: 'a' }, { inCompose: true });
  assert.equal(env.ConnectionStrings__Default, 'Host=postgres;Port=5432;Database=bepthaymao;Username=bepthaymao;Password=p');
  assert.equal(env.ConnectionStrings__Redis, 'redis:6379,password=r');
  assert.equal(env.ConnectionStrings__RabbitMq, 'amqp://bepthaymao:a@rabbitmq:5672/');
});

test('buildApiEnv fails with the missing key name', () => {
  assert.throws(() => buildApiEnv({}), /POSTGRES_PASSWORD/);
});
