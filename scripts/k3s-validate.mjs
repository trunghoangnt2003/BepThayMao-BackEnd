// Offline k3s manifest check: kustomize render + kubeconform (CRDs from datreeio catalog).
// Never talks to a cluster: only `kubectl kustomize` (local render) is invoked. Accepts only --file <rendered.yaml>.
import { spawnSync } from 'node:child_process';
import { copyFileSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const args = process.argv.slice(2);
if (!(args.length === 0 || (args.length === 2 && args[0] === '--file'))) {
  console.error('Từ chối đối số: chỉ nhận --file <tệp manifest đã render>.');
  process.exit(2);
}

const directory = mkdtempSync(join(tmpdir(), 'bep-k3s-'));
try {
  const manifest = join(directory, 'manifest.yaml');
  if (args[0] === '--file') {
    copyFileSync(resolve(args[1]), manifest);
  } else {
    const render = spawnSync('kubectl', ['kustomize', join(root, 'deploy', 'k3s', 'overlays', 'dev')], { encoding: 'utf8' });
    if (render.error) throw render.error;
    if (render.status !== 0) throw new Error(`kustomize lỗi:\n${render.stderr}`);
    writeFileSync(manifest, render.stdout);
  }

  const result = spawnSync('docker', ['run', '--rm', '-v', `${directory}:/manifests:ro`, 'ghcr.io/yannh/kubeconform:v0.8.0',
    '-strict', '-summary', '-schema-location', 'default', '-schema-location',
    'https://raw.githubusercontent.com/datreeio/CRDs-catalog/main/{{.Group}}/{{.ResourceKind}}_{{.ResourceAPIVersion}}.json',
    '/manifests/manifest.yaml'], { stdio: 'inherit' });
  if (result.error) throw result.error;
  process.exitCode = result.status ?? 1;
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  rmSync(directory, { recursive: true, force: true });
}
