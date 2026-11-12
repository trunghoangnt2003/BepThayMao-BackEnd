import { spawnSync } from 'node:child_process';
import assert from 'node:assert/strict';
import { test } from 'node:test';

test('allows develop integration while retaining feature branch validation', () => {
  for (const name of ['main', 'develop', 'feat/backend-foundation', 'gsd/01-base']) {
    const result = spawnSync(process.execPath, ['scripts/check-branch-name.mjs', '--name', name]);
    assert.equal(result.status, 0, name);
  }
  for (const name of ['Develop', 'unknown', 'feat/', 'feat/Uppercase']) {
    const result = spawnSync(process.execPath, ['scripts/check-branch-name.mjs', '--name', name]);
    assert.equal(result.status, 1, name);
  }
});
