// NuGet licenses (including transitive) against scripts/license-policy.json (or LICENSE_POLICY_FILE for drills).
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { evaluatePolicy } from './lib/license-policy.mjs';

try {
  const policy = JSON.parse(readFileSync(process.env.LICENSE_POLICY_FILE || 'scripts/license-policy.json', 'utf8'));
  const result = spawnSync('dotnet', ['tool', 'run', 'nuget-license', '--', '-i', 'Bepthaymao.slnx', '-t', '-o', 'Json'],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  if (result.error) throw result.error;
  const start = result.stdout.indexOf('[');
  if (start < 0) throw new Error(`nuget-license không trả JSON (exit ${result.status}):\n${result.stderr}`);
  const seen = new Map();
  for (const item of JSON.parse(result.stdout.slice(start))) {
    seen.set(`${item.PackageId}@${item.PackageVersion}`, { name: item.PackageId, version: item.PackageVersion, license: item.License });
  }
  const violations = evaluatePolicy([...seen.values()], policy);
  for (const violation of violations) console.error(`VI PHẠM  ${violation.package}  ${violation.license}`);
  console.log(`Đã kiểm ${seen.size} gói NuGet, ${violations.length} vi phạm, ${policy.exceptions.length} ngoại lệ có lý do.`);
  process.exitCode = violations.length ? 1 : 0;
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
}
