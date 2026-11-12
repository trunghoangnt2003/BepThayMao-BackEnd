import { readFileSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { evaluatePolicy } from './lib/license-policy.mjs';
const directory = mkdtempSync(join(tmpdir(), 'bep-licenses-'));
try {
  const output = join(directory, 'licenses.json');
  const result = spawnSync('dotnet', ['nuget-license', '-i', 'Bepthaymao.slnx', '-t', '-o', 'Json', '-fo', output], { stdio: 'inherit' });
  if (result.error || result.status === null || result.status > 1) throw new Error('Cannot scan NuGet licenses.');
  const packages = JSON.parse(readFileSync(output, 'utf8')).map((item) => ({ package: `${item.PackageId}@${item.PackageVersion}`, license: item.License ?? null }));
  const policy = JSON.parse(readFileSync(process.env.LICENSE_POLICY_FILE ?? 'scripts/license-policy.json', 'utf8'));
  const violations = evaluatePolicy(packages, policy);
  for (const item of violations) console.error(`${item.package}: ${item.license ?? 'UNKNOWN'}`);
  console.log(`NuGet licenses: ${packages.length} checked, ${violations.length} violations.`);
  process.exitCode = violations.length ? 1 : 0;
} finally { rmSync(directory, { recursive: true, force: true }); }
