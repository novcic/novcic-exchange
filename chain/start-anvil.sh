#!/bin/sh
set -eu

# Pinning genesis preserves chain identity, but --timestamp also initializes
# Anvil's mining clock. Restore that clock before enabling mining or readiness.
rm -f /tmp/novcic-chain-ready
anvil "$@" &
node_pid=$!
shutdown() {
  trap '' INT TERM
  kill -INT "$node_pid" 2>/dev/null || true
  wait "$node_pid" || true
  exit 0
}
trap shutdown INT TERM

attempt=0
until cast block-number --rpc-url http://127.0.0.1:8545 >/dev/null 2>&1; do
  kill -0 "$node_pid" 2>/dev/null || { wait "$node_pid"; exit 1; }
  attempt=$((attempt + 1))
  if [ "$attempt" -ge 60 ]; then
    echo 'Anvil RPC startup timed out' >&2
    kill -INT "$node_pid" 2>/dev/null || true
    wait "$node_pid" || true
    exit 1
  fi
  sleep 1
done

latest=$(cast block latest --field timestamp --rpc-url http://127.0.0.1:8545)
now=$(date +%s)
if [ "$latest" -ge "$now" ]; then now=$((latest + 1)); fi
cast rpc evm_setNextBlockTimestamp "$now" --rpc-url http://127.0.0.1:8545 >/dev/null
cast rpc evm_setIntervalMining 1 --rpc-url http://127.0.0.1:8545 >/dev/null
touch /tmp/novcic-chain-ready
wait "$node_pid"
