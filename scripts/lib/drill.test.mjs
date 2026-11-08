import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, existsSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { assertRed, withFiles } from './drill.mjs';

test('restores original bytes and removes probes even when verification throws', () => {
  const root = mkdtempSync(join(tmpdir(), 'bep-drill-test-'));
  try {
    writeFileSync(join(root, 'original.txt'), Buffer.from([0, 1, 2, 255]));
    assert.throws(() => withFiles(root, { 'original.txt': 'mutated', 'new.txt': 'probe' }, () => {
      assert.equal(readFileSync(join(root, 'original.txt'), 'utf8'), 'mutated');
      throw new Error('verification failed');
    }), /verification failed/);
    assert.deepEqual(readFileSync(join(root, 'original.txt')), Buffer.from([0, 1, 2, 255]));
    assert.equal(existsSync(join(root, 'new.txt')), false);
    assert.throws(() => withFiles(root, { '../outside.txt': 'no' }, () => {}), /outside/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
test('only accepts a nonzero exit that matches the expected failure', () => {
  assert.equal(assertRed({ status: 1, stdout: 'CS1030: expected' }, /CS1030/), 'CS1030: expected');
  assert.throws(() => assertRed({ status: 0, stdout: 'CS1030' }, /CS1030/));
  assert.throws(() => assertRed({ status: 1, stderr: 'network unavailable' }, /CS1030/));
  assert.throws(() => assertRed({ status: null, error: new Error('spawn failed') }, /CS1030/));
});
