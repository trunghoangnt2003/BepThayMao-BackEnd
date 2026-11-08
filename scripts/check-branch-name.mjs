import { spawnSync } from 'node:child_process';

export function validBranch(name) {
  return name === 'main' || name === 'develop' || /^(feat|fix|docs|refactor|test|chore|ci|build|perf)\/[a-z0-9][a-z0-9._-]*$/.test(name)
    || /^gsd\/[a-z0-9._/-]+$/.test(name);
}

const explicit = process.argv.indexOf('--name');
const result = explicit < 0 ? spawnSync('git', ['branch', '--show-current'], { encoding: 'utf8' }) : null;
const name = explicit < 0 ? result?.stdout?.trim() : process.argv[explicit + 1];
if (result && result.status !== 0) {
  console.error('Không đọc được nhánh Git.');
  process.exitCode = 2;
} else if (name === '' && explicit < 0) {
  console.log('HEAD tách rời: không kiểm tên nhánh.');
} else if (!name || !validBranch(name)) {
  console.error('Tên nhánh không hợp lệ; dùng main, develop, <type>/<slug> hoặc gsd/<slug>.');
  process.exitCode = 1;
}
