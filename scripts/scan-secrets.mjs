import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve, sep } from 'node:path';

const staged = process.argv.includes('--staged');
const run = (command, args, options = {}) => spawnSync(command, args, { stdio: 'inherit', ...options });
const docker = run('docker', ['info'], { stdio: 'ignore' });
if (docker.status !== 0) {
  console.error('Docker chưa chạy: chưa quét được secret; CI phải kiểm trước merge.');
  process.exit(staged && process.env.CI !== 'true' ? 0 : 2);
}
const temporary = staged ? mkdtempSync(join(tmpdir(), 'bep-staged-')) : null;
try {
  if (staged) {
    const diff = run('git', ['diff', '--cached', '--name-only', '-z', '--diff-filter=ACM'], { stdio: 'pipe', encoding: 'utf8' });
    if (diff.status !== 0) throw new Error('Không đọc được staged diff.');
    const files = diff.stdout.split('\0').filter(Boolean);
    for (const file of files) {
      const destination = resolve(temporary, file);
      if (!destination.startsWith(temporary + sep)) throw new Error('Đường dẫn staged vượt ngoài thư mục tạm.');
      const content = run('git', ['show', `:${file}`], { stdio: 'pipe' });
      if (content.status !== 0) throw new Error(`Không đọc được staged file ${file}.`);
      mkdirSync(dirname(destination), { recursive: true });
      writeFileSync(destination, content.stdout);
    }
  }
  const result = run('docker', ['run', '--rm', '-v', `${temporary ?? process.cwd()}:/repo:ro`,
    'zricethezav/gitleaks:v8.30.1', staged ? 'dir' : 'git', '/repo', '--no-banner', '--redact']);
  process.exitCode = result.status ?? 2;
} catch (error) {
  console.error(error.message);
  process.exitCode = 2;
} finally {
  if (temporary) rmSync(temporary, { recursive: true, force: true });
}
