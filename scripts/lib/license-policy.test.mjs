import { test } from 'node:test';
import assert from 'node:assert/strict';
import { evaluatePolicy } from './license-policy.mjs';
test('license policy respects exact exceptions and SPDX AND/OR semantics', () => {
  const policy = { allow: ['MIT'], exceptions: [{ package: 'special@1', license: 'BlueOak-1.0.0', reason: 'Reviewed development-only tool license.' }] };
  const packages = [
    { package: 'ok@1', license: '(MIT OR CC0-1.0)' },
    { package: 'bad@1', license: 'MIT AND GPL-3.0-only' },
    { package: 'special@1', license: 'BlueOak-1.0.0' },
    { package: 'special@2', license: 'BlueOak-1.0.0' },
  ];
  assert.deepEqual(evaluatePolicy(packages, policy).map((item) => item.package), ['bad@1', 'special@2']);
  assert.throws(() => evaluatePolicy([], { allow: [], exceptions: [{ package: 'bad', reason: '' }] }));
});
