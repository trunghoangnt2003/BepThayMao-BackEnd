// Fails when openapi/v1.json breaks the version on --base <ref> (default origin/main). Uses the oasdiff image (K7).
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const baseIndex = process.argv.indexOf('--base');
const base = baseIndex > 0 && process.argv[baseIndex + 1] ? process.argv[baseIndex + 1] : 'origin/main';

const show = spawnSync('git', ['show', `${base}:openapi/v1.json`], { encoding: 'utf8', maxBuffer: 32 * 1024 * 1024 });
if (show.status !== 0) {
  console.log(`Chưa có bản cơ sở openapi/v1.json trên ${base}, bỏ qua so breaking.`);
  process.exit(0);
}

const directory = mkdtempSync(join(tmpdir(), 'bep-breaking-'));
try {
  writeFileSync(join(directory, 'base.json'), show.stdout);
  copyFileSync('openapi/v1.json', join(directory, 'revision.json'));
  const result = spawnSync('docker', ['run', '--rm', '-v', `${directory}:/specs:ro`, 'tufin/oasdiff:v1.36.0',
    'breaking', '/specs/base.json', '/specs/revision.json', '--fail-on', 'ERR'], { stdio: 'inherit' });
  if (result.error) throw result.error;
  process.exitCode = result.status ?? 1;
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  rmSync(directory, { recursive: true, force: true });
}
