// CS57 — `harness startup` hook for the first e2e smoke gate.
//
// `harness startup` runs `node --test tests/*.test.mjs` as a hard-fail "broken tree"
// check, so this wrapper is the only consumer-side way to add the e2e to startup without
// modifying the harness. It boots the real `aspire run` stack via the .NET e2e
// (tests/AuthzEntitlements.E2E.Tests) but SKIPS green when it cannot run safely:
//   - Docker is unavailable (`docker info` fails), or
//   - host port 8088 is already bound — a live `aspire run` is active. (The .NET e2e uses a
//     proxied Keycloak endpoint and does not itself bind 8088, so this is a guard against
//     running two full stacks at once, not a hard port-binding conflict.)
// Otherwise it runs `dotnet test` with RUN_ASPIRE_E2E=1 and asserts a green exit.
//
// A *skipped* run here is a convenience for Docker-less startups/CI — it does NOT satisfy
// the mandatory pre-PR gate (see docs/testing/e2e-smoke.md). Zero deps, Node 20 stdlib.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import net from 'node:net';

const KEYCLOAK_PORT = 8088;

/** True if `docker info` exits 0 (a Docker daemon is reachable). */
function isDockerAvailable() {
  const result = spawnSync('docker', ['info'], {
    stdio: 'ignore',
    shell: false,
    timeout: 30_000,
  });
  return result.status === 0;
}

/** Resolves true if something is already listening on 127.0.0.1:<port>. */
function isPortInUse(port) {
  return new Promise((resolve) => {
    const socket = new net.Socket();
    let settled = false;
    const done = (inUse) => {
      if (settled) return;
      settled = true;
      socket.destroy();
      resolve(inUse);
    };
    socket.setTimeout(1_000);
    socket.once('connect', () => done(true));
    socket.once('timeout', () => done(false));
    socket.once('error', () => done(false));
    socket.connect(port, '127.0.0.1');
  });
}

test('aspire-run stack e2e smoke gate', async (t) => {
  if (!isDockerAvailable()) {
    t.skip('Docker not available');
    return;
  }

  if (await isPortInUse(KEYCLOAK_PORT)) {
    t.skip(`port ${KEYCLOAK_PORT} in use — an aspire run is active`);
    return;
  }

  const E2E_TIMEOUT_MS = 15 * 60_000; // generous cap; the e2e itself is ~1–3 min.
  const result = spawnSync(
    'dotnet',
    ['test', 'tests/AuthzEntitlements.E2E.Tests', '-c', 'Debug'],
    {
      stdio: 'inherit',
      shell: false,
      timeout: E2E_TIMEOUT_MS,
      killSignal: 'SIGKILL',
      env: { ...process.env, RUN_ASPIRE_E2E: '1' },
    },
  );

  // On timeout, spawnSync sets `.error` (ETIMEDOUT) and a null status — surface it so a hung
  // e2e fails the gate instead of blocking `harness startup`/`node --test` indefinitely.
  if (result.error) {
    assert.fail(`dotnet test (RUN_ASPIRE_E2E=1) failed to run: ${result.error.message}`);
  }
  assert.strictEqual(
    result.status,
    0,
    `dotnet test (RUN_ASPIRE_E2E=1) should exit 0 but exited ${result.status}.`,
  );
});
