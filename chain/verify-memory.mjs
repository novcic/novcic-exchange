// Isolated Anvil persistence/memory regression; never attaches exchange volumes or networks.
import assert from 'node:assert/strict';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {dirname, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {randomUUID} from 'node:crypto';
import {Interface} from 'ethers';
import solc from 'solc';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const exec = promisify(execFile);
const docker = async (...args) => (await exec('docker', args, {cwd: root, timeout: 120000, maxBuffer: 4 * 1024 * 1024})).stdout.trim();
const config = JSON.parse(await docker('compose', '-f', 'compose.next.yaml', 'config', '--format', 'json', '--no-interpolate'));
const chain = config.services.chain;
const name = `novcic-chain-check-${randomUUID().slice(0, 8)}`;
const artifact = resolve(root, 'artifacts', name);
await mkdir(artifact, {recursive: true});
const rpc = async (method, ...params) => JSON.parse(await docker('exec', name, 'cast', 'rpc', '--raw', method, JSON.stringify(params)));
const waitReady = async () => {
  for (let i = 0; i < 60; i++) {
    try {
      await docker('exec', name, 'test', '-f', '/tmp/novcic-chain-ready');
      assert.equal(await rpc('eth_chainId'), '0x7a69'); return;
    } catch {}
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  throw Error('Isolated chain did not become ready');
};
const memory = async () => Number(await docker('exec', name, 'sh', '-c',
  'if [ -f /sys/fs/cgroup/memory.current ]; then cat /sys/fs/cgroup/memory.current; else cat /sys/fs/cgroup/memory/memory.usage_in_bytes; fi'));
let created = false;
try {
  // Writable container layer survives restart; no live state or host keys are mounted.
  const command = chain.command.map(value => value === '/state/anvil.json' ? '/tmp/anvil.json'
    : value === '${CHAIN_GENESIS_TIMESTAMP:-1704067200}' ? '1704067200' : value);
  await docker('run', '-d', '--name', name, '--network', 'none', '--memory', '512m', '--memory-swap', '512m', '--cpus', '2',
    '--stop-signal', 'SIGINT', '--mount', `type=bind,source=${resolve(root, 'chain/start-anvil.sh')},target=/usr/local/bin/start-anvil.sh,readonly`,
    '--entrypoint', 'sh', chain.image, '/usr/local/bin/start-anvil.sh', ...command);
  created = true;
  await waitReady();
  await rpc('evm_setIntervalMining', 0);
  const genesis = await rpc('eth_getBlockByNumber', '0x0', false);
  const [sender, recipient] = await rpc('eth_accounts');
  const input = {language: 'Solidity', sources: {'Token.sol': {content: await readFile(resolve(root, 'chain/Token.sol'), 'utf8')}},
    settings: {evmVersion: 'paris', optimizer: {enabled: true, runs: 200}, outputSelection: {'*': {'*': ['abi', 'evm.bytecode.object']}}}};
  const compiled = JSON.parse(solc.compile(JSON.stringify(input)));
  assert.ok(!compiled.errors?.some(error => error.severity === 'error'), 'Token compilation failed');
  const token = compiled.contracts['Token.sol'].Token;
  const iface = new Interface(token.abi);
  const supply = 1000000000000n;
  const deploy = await rpc('eth_sendTransaction', {from: sender, data: '0x' + token.evm.bytecode.object + iface.encodeDeploy(['Memory test', 'MEM', 6, supply]).slice(2), gas: '0x2dc6c0'});
  await rpc('anvil_mine', '0x1');
  const deployment = await rpc('eth_getTransactionReceipt', deploy);
  assert.equal(deployment.status, '0x1');
  const address = deployment.contractAddress;
  const hash = await rpc('eth_sendTransaction', {from: sender, to: address, data: iface.encodeFunctionData('transfer', [recipient, 1234567n]), gas: '0x30d40'});
  await rpc('anvil_mine', '0x1');
  const receipt = await rpc('eth_getTransactionReceipt', hash);
  assert.equal(receipt.status, '0x1');
  const readBalance = async owner => iface.decodeFunctionResult('balanceOf', await rpc('eth_call', {to: address, data: iface.encodeFunctionData('balanceOf', [owner])}, 'latest'))[0];
  const before = await memory();
  await rpc('anvil_mine', '0x4e20', '0x1'); // 20,000 empty blocks: over five hours at one block/second.
  const afterMining = await memory();
  assert.equal(await readBalance(recipient), 1234567n);
  assert.equal(await readBalance(sender), supply - 1234567n);
  const head = await rpc('eth_getBlockByNumber', 'latest', false);
  await docker('stop', '--timeout', '60', name);
  await docker('cp', name + ':/tmp/anvil.json', resolve(artifact, 'state.json'));
  const saved = JSON.parse(await readFile(resolve(artifact, 'state.json'), 'utf8'));
  assert.ok(saved.historical_states == null, 'Historical EVM snapshots must not be persisted');
  assert.ok(saved.blocks.length >= 20000, 'Block history was unexpectedly discarded');
  for (let restart = 0; restart < 2; restart++) {
    await docker('start', name);
    await waitReady();
    await rpc('evm_setIntervalMining', 0);
    assert.equal((await rpc('eth_getBlockByNumber', '0x0', false)).hash, genesis.hash);
    assert.equal((await rpc('eth_getBlockByNumber', head.number, false)).hash, head.hash);
    await rpc('anvil_mine', '0x1');
    const current = await rpc('eth_getBlockByNumber', 'latest', false);
    assert.ok(BigInt(current.timestamp) > BigInt(head.timestamp), 'Restart rewound the block clock');
    assert.ok(BigInt(current.timestamp) >= BigInt(Math.floor(Date.now() / 1000) - 5));
    assert.deepEqual(await rpc('eth_getTransactionReceipt', hash), receipt);
    assert.equal(await readBalance(recipient), 1234567n);
    assert.equal(await readBalance(sender), supply - 1234567n);
    if (restart === 0) await docker('stop', '--timeout', '60', name);
  }
  const logs = await rpc('eth_getLogs', {address, fromBlock: '0x0', toBlock: 'latest', topics: [iface.getEvent('Transfer').topicHash]});
  assert.equal(logs.filter(log => log.transactionHash === hash).length, 1);
  const afterRestart = await memory();
  const report = {blocks: saved.blocks.length, memoryBytes: {before, afterMining, afterRestart},
    checks: '20,000 blocks under a 512 MiB hard cap; genesis, balances, block hashes, receipt and single transfer log survive two restarts without historical EVM snapshots'};
  await writeFile(resolve(artifact, 'result.json'), JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
} catch (error) {
  if (created) {
    const state = await docker('inspect', name, '--format', '{{json .State}}').catch(() => 'unavailable');
    const logs = await exec('docker', ['logs', '--tail', '80', name])
      .then(result => result.stdout + result.stderr).catch(logError => logError.stderr ?? logError.message);
    await writeFile(resolve(artifact, 'failure.json'), JSON.stringify({state, logs, error: error.message}, null, 2));
    console.error(`Isolated failure details saved to ${artifact}`);
  }
  throw error;
} finally {
  if (created) {
    await docker('stop', '--timeout', '60', name).catch(() => {});
    await docker('rm', name);
  }
}
