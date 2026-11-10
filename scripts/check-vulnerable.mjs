// Fails when any direct or transitive NuGet package has a known vulnerability.
import { spawnSync } from 'node:child_process';

const result = spawnSync('dotnet', ['list', 'Bepthaymao.slnx', 'package', '--vulnerable', '--include-transitive', '--format', 'json'],
  { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
if (result.error || result.status !== 0) {
  console.error(result.error?.message ?? result.stderr ?? result.stdout);
  process.exit(1);
}

const report = JSON.parse(result.stdout);
const findings = [];
for (const project of report.projects ?? []) {
  for (const framework of project.frameworks ?? []) {
    for (const item of [...(framework.topLevelPackages ?? []), ...(framework.transitivePackages ?? [])]) {
      for (const vulnerability of item.vulnerabilities ?? []) {
        findings.push(`${item.id}@${item.resolvedVersion} ${vulnerability.severity} ${vulnerability.advisoryurl}`);
      }
    }
  }
}
for (const finding of new Set(findings)) console.error(`CVE  ${finding}`);
console.log(`Đã kiểm ${report.projects?.length ?? 0} project, ${new Set(findings).size} gói có lỗ hổng.`);
process.exitCode = findings.length ? 1 : 0;
