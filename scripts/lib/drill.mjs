import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, resolve, sep } from 'node:path';

export function withFiles(root, changes, verify) {
  const paths = Object.keys(changes).map((file) => resolve(root, file));
  if (paths.some((file) => !file.startsWith(resolve(root) + sep))) {
    throw new Error('Drill path outside repository.');
  }
  const originals = paths.map((file) => existsSync(file) ? readFileSync(file) : null);
  try {
    for (const [index, file] of paths.entries()) {
      mkdirSync(dirname(file), { recursive: true });
      writeFileSync(file, Object.values(changes)[index]);
    }
    return verify();
  } finally {
    for (const [index, file] of paths.entries()) {
      const original = originals[index];
      if (original === null) rmSync(file, { force: true });
      else writeFileSync(file, original);
    }
  }
}

export function assertRed(result, expected) {
  const output = `${result.stdout ?? ''}\n${result.stderr ?? ''}`;
  if (result.error || result.status === 0 || result.status === null || !expected.test(output)) {
    throw new Error(`Drill did not fail for the expected reason (exit ${result.status}):\n${output}`);
  }
  return output.split('\n').find((line) => expected.test(line))?.trim() ?? '';
}
