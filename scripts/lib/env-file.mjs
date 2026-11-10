import { randomBytes } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';

export const SECRET_KEYS = ['POSTGRES_PASSWORD', 'REDIS_PASSWORD', 'RABBITMQ_DEFAULT_PASS'];

/** Parses KEY=VALUE lines; blank lines and # comments are ignored, the first '=' splits. */
export function parseEnv(text) {
  const values = {};
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#')) continue;
    const separator = line.indexOf('=');
    if (separator <= 0) continue;
    values[line.slice(0, separator).trim()] = line.slice(separator + 1).trim();
  }
  return values;
}

/** Fills empty secret keys in-place in the env text, keeping comments and order. */
export function fillSecrets(text, generate = () => randomBytes(24).toString('base64url')) {
  return text.replace(/^([A-Z_]+)=[ \t]*$/gm, (line, key) => (SECRET_KEYS.includes(key) ? `${key}=${generate()}` : line));
}

/** Creates envPath from examplePath when missing and generates any empty secret. Never prints values. */
export function ensureEnvFile(examplePath, envPath) {
  const created = !existsSync(envPath);
  const original = created ? readFileSync(examplePath, 'utf8') : readFileSync(envPath, 'utf8');
  const filled = fillSecrets(original);
  if (created || filled !== original) writeFileSync(envPath, filled, { encoding: 'utf8', mode: 0o600 });
  return { created, values: parseEnv(filled) };
}

/** Environment for the API/CLI process on the host, pointing at the compose services on 127.0.0.1. */
export function buildApiEnv(env) {
  const required = (key) => {
    if (!env[key]) throw new Error(`Thiếu ${key} trong deploy/.env; chạy npm run dev:up.`);
    return env[key];
  };
  const result = {
    ASPNETCORE_ENVIRONMENT: 'Development',
    ConnectionStrings__Default: `Host=127.0.0.1;Port=${env.POSTGRES_PORT || 5432};Database=${env.POSTGRES_DB || 'bepthaymao'};`
      + `Username=${env.POSTGRES_USER || 'bepthaymao'};Password=${required('POSTGRES_PASSWORD')}`,
    ConnectionStrings__Redis: `127.0.0.1:${env.REDIS_PORT || 6379},password=${required('REDIS_PASSWORD')}`,
    ConnectionStrings__RabbitMq: `amqp://${encodeURIComponent(env.RABBITMQ_DEFAULT_USER || 'bepthaymao')}:`
      + `${encodeURIComponent(required('RABBITMQ_DEFAULT_PASS'))}@127.0.0.1:${env.RABBITMQ_PORT || 5672}/`,
    SePay__Mode: env.SePay__Mode || 'Fake',
    Zalo__Mode: env.Zalo__Mode || 'Fake',
  };
  if (env.OTEL_EXPORTER_OTLP_ENDPOINT) result.OTEL_EXPORTER_OTLP_ENDPOINT = env.OTEL_EXPORTER_OTLP_ENDPOINT;
  return result;
}
