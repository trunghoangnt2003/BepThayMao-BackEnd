import assert from 'node:assert/strict';
import test from 'node:test';
import { evaluatePolicy } from './license-policy.mjs';

const policy = {
  allow: ['MIT', 'Apache-2.0'],
  exceptions: [{ package: 'blue@1.0.0', license: 'BlueOak-1.0.0', reason: 'Giấy phép permissive đã được duyệt riêng.' }],
};
const pkg = (name, version, license, extra = {}) => ({ name, version, license, ...extra });

test('allowed license passes', () => {
  assert.deepEqual(evaluatePolicy([pkg('a', '1.0.0', 'MIT')], policy), []);
});

test('license outside allowlist is a violation', () => {
  assert.deepEqual(evaluatePolicy([pkg('gpl', '2.0.0', 'GPL-3.0-only')], policy),
    [{ package: 'gpl@2.0.0', license: 'GPL-3.0-only' }]);
});

test('exception matches exact name@version only', () => {
  assert.deepEqual(evaluatePolicy([pkg('blue', '1.0.0', 'BlueOak-1.0.0')], policy), []);
  assert.equal(evaluatePolicy([pkg('blue', '1.0.1', 'BlueOak-1.0.0')], policy).length, 1);
});

test('OR expression passes when one side is allowed; AND needs every side', () => {
  assert.deepEqual(evaluatePolicy([pkg('dual', '1.0.0', '(MIT OR CC0-1.0)')], policy), []);
  assert.equal(evaluatePolicy([pkg('both', '1.0.0', 'MIT AND CC0-1.0')], policy).length, 1);
});

test('missing license is a violation unless excepted', () => {
  assert.deepEqual(evaluatePolicy([pkg('none', '1.0.0', undefined)], policy), [{ package: 'none@1.0.0', license: '(không có)' }]);
});

test('exception without a real reason is a configuration error', () => {
  const bad = { allow: ['MIT'], exceptions: [{ package: 'x@1.0.0', license: 'X', reason: 'ok' }] };
  assert.throws(() => evaluatePolicy([], bad), /lý do/);
  const range = { allow: ['MIT'], exceptions: [{ package: 'x', license: 'X', reason: 'thiếu phiên bản cụ thể' }] };
  assert.throws(() => evaluatePolicy([], range), /tên@phiên bản/);
});

test('private UNLICENSED root package of the repo is skipped', () => {
  assert.deepEqual(evaluatePolicy([pkg('app', '0.0.0', 'UNLICENSED', { private: true })], policy, { rootName: 'app' }), []);
  assert.equal(evaluatePolicy([pkg('other', '0.0.0', 'UNLICENSED', { private: true })], policy, { rootName: 'app' }).length, 1);
});
