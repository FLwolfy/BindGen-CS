import { dotnet } from './_framework/dotnet.js';

let report;
try {
    const runtime = await dotnet.create();
    const exitCode = await runtime.runMain();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    report = JSON.parse(exports.WasmAcceptance.Program.GetReport());
    if (exitCode !== 0) report.success = false;
} catch (error) {
    report = { success: false, error: String(error) };
}
document.getElementById('result').textContent = JSON.stringify(report, null, 2);
const token = new URLSearchParams(location.search).get('token');
await fetch('/result?token=' + encodeURIComponent(token), {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(report)
});
