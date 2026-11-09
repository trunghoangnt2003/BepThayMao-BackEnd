export function evaluatePolicy(packages, policy) {
  for (const exception of policy.exceptions) {
    if (!exception.package.includes('@') || exception.reason.trim().length < 10) throw new Error('License exception requires exact package@version and a substantive reason.');
  }
  function allowed(expression) {
    const tokens = expression.match(/[A-Za-z0-9.+:-]+|\(|\)/g) ?? [];
    let index = 0;
    const primary = () => {
      if (tokens[index] === '(') { index++; const value = disjunction(); if (tokens[index++] !== ')') throw new Error('Invalid SPDX expression.'); return value; }
      return policy.allow.includes(tokens[index++]);
    };
    const conjunction = () => {
      let value = primary();
      while (tokens[index] === 'AND') { index++; const next = primary(); value = value && next; }
      return value;
    };
    const disjunction = () => {
      let value = conjunction();
      while (tokens[index] === 'OR') { index++; const next = conjunction(); value = value || next; }
      return value;
    };
    const result = disjunction();
    return result && index === tokens.length;
  }
  return packages.filter((item) => {
    if (policy.exceptions.some((exception) => exception.package === item.package && exception.license === item.license)) return false;
    return !allowed(item.license ?? 'UNKNOWN');
  });
}
