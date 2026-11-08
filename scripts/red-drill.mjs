import { spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { assertRed, withFiles } from './lib/drill.mjs';

const root = process.cwd();
const run = (command, args, options = {}) => spawnSync(command, args, { cwd: root, encoding: 'utf8', ...options });
const git = (...args) => run('git', args);
const status = () => {
  const result = git('status', '--porcelain');
  if (result.status !== 0) throw new Error('Cannot read Git status.');
  return result.stdout;
};
if (status().trim()) throw new Error('Commit changes before running the reversible drill.');
const lockFiles = git('ls-files').stdout.split('\n').filter((file) => file.endsWith('packages.lock.json'));
const locks = new Map(lockFiles.map((file) => [file, readFileSync(file)]));
const tests = ['test', '--project', 'tests/Bepthaymao.Architecture.Tests'];
const domain = 'src/Bepthaymao.Domain/Bepthaymao.Domain.csproj';
const application = 'src/Bepthaymao.Application/Bepthaymao.Application.csproj';
const cases = [
  {
    name: 'domain-references-efcore',
    changes: {
      [domain]: '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="Microsoft.EntityFrameworkCore" /></ItemGroup></Project>',
      'src/Bepthaymao.Domain/Drill/Probe.cs': 'namespace Bepthaymao.Domain.Drill; public sealed class Probe : Microsoft.EntityFrameworkCore.DbContext {}',
    }, args: tests, expected: /AssemblyDoesNotDependOnForbiddenLayer|ProjectReferencesFollowLayerRules/,
  },
  {
    name: 'endpoints-reference-infrastructure',
    changes: {
      'src/Bepthaymao.Infrastructure/DrillProbe.cs': 'namespace Bepthaymao.Infrastructure; public sealed class DrillProbe {}',
      'src/Bepthaymao.Api/Endpoints/Drill/Probe.cs': 'namespace Bepthaymao.Api.Endpoints.Drill; public static class Probe { public static Bepthaymao.Infrastructure.DrillProbe Create() => new(); }',
    }, args: tests, expected: /EndpointsDoNotDependOnInfrastructureOrEfCore/,
  },
  {
    name: 'controller-base',
    changes: { 'src/Bepthaymao.Api/Drill/Probe.cs': 'namespace Bepthaymao.Api.Drill; public sealed class Probe : Microsoft.AspNetCore.Mvc.ControllerBase {}' },
    args: tests, expected: /NoTypeDerivesFromControllerBase/,
  },
  {
    name: 'application-references-infrastructure',
    changes: { [application]: readFileSync(application, 'utf8').replace('</Project>', '<ItemGroup><ProjectReference Include="../Bepthaymao.Infrastructure/Bepthaymao.Infrastructure.csproj" /></ItemGroup></Project>') },
    args: ['build', 'Bepthaymao.slnx'], expected: /NU1108|MSB4006/,
  },
  {
    name: 'warning-is-error',
    changes: { 'src/Bepthaymao.Domain/Drill/Probe.cs': '#warning deliberate quality gate drill\n' },
    args: ['build', 'Bepthaymao.slnx', '-warnaserror'], expected: /error CS1030/,
  },
];
try {
  for (const drill of cases) {
    const evidence = withFiles(root, drill.changes, () => assertRed(run('dotnet', drill.args), drill.expected));
    console.log(`${drill.name} | dotnet ${drill.args.join(' ')} | ĐỎ ĐÚNG | ${evidence}`);
    for (const [file, bytes] of locks) writeFileSync(file, bytes);
  }
  const cli = resolve('node_modules/@commitlint/cli/cli.js');
  const evidence = assertRed(run(process.execPath, [cli], { input: 'Update stuff\n' }), /type-empty|subject-empty/);
  const valid = run(process.execPath, [cli], { input: 'feat(01-02): add solution skeleton\n' });
  if (valid.status !== 0) throw new Error('Conventional Commit rejected.');
  console.log(`commit-message | commitlint | ĐỎ ĐÚNG | ${evidence}`);
  const secretPath = 'drill-secret.txt';
  withFiles(root, { [secretPath]: 'ghp_' + randomBytes(27).toString('base64url') + '\n' }, () => {
    try {
      if (git('add', '--', secretPath).status !== 0) throw new Error('Cannot stage secret probe.');
      const proof = assertRed(run(process.execPath, ['scripts/scan-secrets.mjs', '--staged']), /leaks found/);
      console.log(`secret-in-diff | gitleaks --staged | ĐỎ ĐÚNG | ${proof}`);
    } finally {
      if (git('reset', '-q', 'HEAD', '--', secretPath).status !== 0) throw new Error('Cannot unstage secret probe.');
    }
  });
} finally {
  for (const [file, bytes] of locks) writeFileSync(file, bytes);
  if (status().trim()) throw new Error('Drill did not restore a clean working tree.');
}
