const base = process.env.GATEWAY_URL ?? 'http://127.0.0.1:8080';
const response = await fetch(`${base}/health/live`);
if (!response.ok) throw new Error(`Gateway smoke failed: HTTP ${response.status}`);
console.log(`Gateway smoke passed: ${response.status}`);
