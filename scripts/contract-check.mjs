import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
const command = process.argv[2];
if (command === 'lint') {
  const result = spawnSync(process.execPath, ['node_modules/@stoplight/spectral-cli/dist/index.js', 'lint', 'openapi/v1.json', '--fail-severity', 'warn'], { stdio: 'inherit' });
  process.exitCode = result.status ?? 1;
} else if (command === 'breaking') {
  const reference = process.argv[3] ?? 'origin/main';
  const baseline = spawnSync('git', ['show', `${reference}:openapi/v1.json`], { encoding: 'utf8' });
  if (baseline.status !== 0) {
    console.log(`No published baseline at ${reference}; first API publication.`);
  } else {
    const directory = mkdtempSync(join(tmpdir(), 'bep-breaking-'));
    try {
      writeFileSync(join(directory, 'base.json'), baseline.stdout);
      const result = spawnSync('docker', ['run', '--rm', '-v', `${directory}:/base:ro`, '-v', `${process.cwd()}:/revision:ro`, 'tufin/oasdiff:v1.36.0', 'breaking', '/base/base.json', '/revision/openapi/v1.json', '--fail-on', 'ERR'], { stdio: 'inherit' });
      process.exitCode = result.status ?? 1;
    } finally { rmSync(directory, { recursive: true, force: true }); }
  }
} else { throw new Error('Expected lint or breaking.'); }
