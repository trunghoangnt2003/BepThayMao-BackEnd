import { spawnSync } from 'node:child_process';
const result = spawnSync('dotnet', ['list', 'Bepthaymao.slnx', 'package', '--vulnerable', '--include-transitive', '--format', 'json'], { encoding: 'utf8' });
if (result.error || result.status !== 0) throw new Error(result.stderr || 'Vulnerability scan failed.');
const report = JSON.parse(result.stdout);
const vulnerable = report.projects.flatMap((project) => (project.frameworks ?? []).flatMap((framework) => [...(framework.topLevelPackages ?? []), ...(framework.transitivePackages ?? [])]));
for (const item of vulnerable) console.error(`${item.id}: ${JSON.stringify(item.vulnerabilities)}`);
console.log(`Vulnerable NuGet packages: ${vulnerable.length}`);
process.exitCode = vulnerable.length ? 1 : 0;
