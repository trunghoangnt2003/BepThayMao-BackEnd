import { spawnSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
const args = process.argv.slice(2);
if (args.length && !(args.length === 2 && args[0] === '--file')) throw new Error('Only --file <path> is supported; this script never changes a cluster.');
const directory = mkdtempSync(join(tmpdir(), 'bep-k3s-'));
try {
  let file = args[1];
  if (!file) {
    const render = spawnSync('kubectl', ['kustomize', 'deploy/k3s/overlays/dev'], { encoding: 'utf8' });
    if (render.error || render.status !== 0) throw new Error(render.stderr || 'Cannot render kustomize.');
    file = join(directory, 'rendered.yaml');
    writeFileSync(file, render.stdout);
  }
  const result = spawnSync('docker', ['run', '--rm', '-v', `${resolve(file)}:/manifest.yaml:ro`, 'ghcr.io/yannh/kubeconform:v0.8.0', '-strict', '-summary', '-schema-location', 'default', '-schema-location', 'https://raw.githubusercontent.com/datreeio/CRDs-catalog/main/{{.Group}}/{{.ResourceKind}}_{{.ResourceAPIVersion}}.json', '/manifest.yaml'], { stdio: 'inherit' });
  if (result.error) throw result.error;
  process.exitCode = result.status ?? 1;
} finally { rmSync(directory, { recursive: true, force: true }); }
