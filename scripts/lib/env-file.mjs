import { randomBytes } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';

const secrets = new Map([['POSTGRES_PASSWORD', 24], ['REDIS_PASSWORD', 24], ['RABBITMQ_DEFAULT_PASS', 24], ['Jwt__SigningKey', 48]]);

export function readEnvFile(path) {
  if (!existsSync(path)) return {};
  return Object.fromEntries(readFileSync(path, 'utf8').split(/\r?\n/)
    .map(line => line.match(/^\s*([^#=\s]+)\s*=\s*(.*)\s*$/))
    .filter(Boolean).map(([, key, value]) => [key, value]));
}

export function ensureEnvFile(examplePath, envPath) {
  if (!existsSync(envPath)) writeFileSync(envPath, readFileSync(examplePath, 'utf8'));
  const env = readEnvFile(envPath);
  for (const [key, bytes] of secrets) if (!env[key]) env[key] = randomBytes(bytes).toString('base64url');
  const written = new Set();
  const lines = readFileSync(envPath, 'utf8').split(/\r?\n/).map(line => {
    const match = line.match(/^\s*([^#=\s]+)\s*=\s*(.*)\s*$/);
    if (!match || !secrets.has(match[1])) return line;
    written.add(match[1]);
    return `${match[1]}=${env[match[1]]}`;
  });
  for (const key of secrets.keys()) if (!written.has(key)) lines.push(`${key}=${env[key]}`);
  writeFileSync(envPath, lines.join('\n'));
  return env;
}

export function buildApiEnv(env) {
  return {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    AllowedHosts: process.env.AllowedHosts ?? 'localhost;127.0.0.1;host.docker.internal',
    ConnectionStrings__Default: `Host=127.0.0.1;Port=${env.POSTGRES_PORT};Database=${env.POSTGRES_DB};Username=${env.POSTGRES_USER};Password=${env.POSTGRES_PASSWORD}`,
    ConnectionStrings__Redis: `127.0.0.1:${env.REDIS_PORT},password=${env.REDIS_PASSWORD}`,
    ConnectionStrings__RabbitMq: `amqp://${encodeURIComponent(env.RABBITMQ_DEFAULT_USER)}:${encodeURIComponent(env.RABBITMQ_DEFAULT_PASS)}@127.0.0.1:${env.RABBITMQ_PORT}/`,
    'SePay__Mode': env['SePay__Mode'] ?? 'Fake',
    'Zalo__Mode': env['Zalo__Mode'] ?? 'Fake',
    Auth__RefreshCookie__Secure: env.Auth__RefreshCookie__Secure ?? 'false',
    ...Object.fromEntries(Object.entries(env).filter(([key]) => key.startsWith('Jwt__') || key.startsWith('Auth__'))),
  };
}
