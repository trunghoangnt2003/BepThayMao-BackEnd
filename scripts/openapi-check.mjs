import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const directories = [];
try {
  const documents = [];
  for (let index = 0; index < 2; index += 1) {
    const directory = mkdtempSync(join(tmpdir(), 'bep-openapi-'));
    directories.push(directory);
    const result = spawnSync('dotnet', ['build', 'src/Bepthaymao.Api', '--no-incremental',
      `-p:OpenApiDocumentsDirectory=${directory}`], { stdio: 'inherit' });
    if (result.error) throw result.error;
    if (result.status !== 0) throw new Error('Không build được OpenAPI.');
    documents.push(readFileSync(join(directory, 'v1.json'), 'utf8').replaceAll('\r\n', '\n'));
  }
  const committed = readFileSync('openapi/v1.json', 'utf8').replaceAll('\r\n', '\n');
  if (documents[0] !== documents[1] || documents[0] !== committed) {
    throw new Error('openapi/v1.json lỗi thời hoặc không tất định: build lại rồi commit cùng thay đổi mã.');
  }
  console.log('OpenAPI tất định: hai lần build và tệp lưu trong repo trùng nhau.');
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  for (const directory of directories) rmSync(directory, { recursive: true, force: true });
}
