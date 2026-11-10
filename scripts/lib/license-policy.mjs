// Pure SPDX allowlist check. Copied (not imported) in the frontend repo: each repo is self-contained.

function validateExceptions(exceptions) {
  for (const exception of exceptions) {
    if (!/^.+@\d[^@]*$/.test(exception.package ?? '')) {
      throw new Error(`Ngoại lệ "${exception.package}" phải ghi đúng tên@phiên bản, không theo dải.`);
    }
    if ((exception.reason ?? '').trim().length < 10) {
      throw new Error(`Ngoại lệ "${exception.package}" thiếu lý do (tối thiểu 10 ký tự).`);
    }
  }
}

function isAllowed(expression, allow) {
  const normalized = expression.replace(/[()]/g, ' ').trim();
  return normalized.split(/\s+OR\s+/).some((alternative) =>
    alternative.split(/\s+AND\s+/).every((part) => allow.has(part.trim())));
}

/**
 * @param {{name: string, version: string, license?: string, private?: boolean}[]} packages
 * @param {{allow: string[], exceptions: {package: string, license: string, reason: string}[]}} policy
 * @returns {{package: string, license: string}[]} violations
 */
export function evaluatePolicy(packages, policy, { rootName } = {}) {
  validateExceptions(policy.exceptions ?? []);
  const allow = new Set(policy.allow);
  const excepted = new Set((policy.exceptions ?? []).map((exception) => exception.package));
  const violations = [];
  for (const item of packages) {
    if (item.private && item.name === rootName) continue;
    const id = `${item.name}@${item.version}`;
    if (excepted.has(id)) continue;
    if (!item.license || !isAllowed(item.license, allow)) violations.push({ package: id, license: item.license ?? '(không có)' });
  }
  return violations;
}
