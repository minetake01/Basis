# Host / proxy failure policy

| Event | Scope | Action |
|-------|-------|--------|
| Execution timeout | Proxy | Disable proxy; host continues |
| Bytecode verify / signature fail | Proxy | Disable proxy at load |
| Command/event/ticket credit overflow | Proxy | Disable offending proxy; discard pending commands for that proxy generation |
| Host memory quota / OOM | Host | Destroy host state; disable all proxies |
| VM panic / native internal | Host | Destroy host state |
| Buffer pool quota exceeded | Host | Host fail-stop |
| Mixed-load flood host | Proxy | Offending proxy only; benign hosts must pass p99 gates |

## Generation bumps

- Proxy disable increments `proxy_generation`; stale commands/events/tickets rejected at flush.
- Host destroy increments `host_epoch`; all handles tombstoned.

## Teardown

1. Main thread posts `BasisLuauShutdown` to shard worker.
2. Worker acks after VM `lua_close` / child `lua_unref` complete.
3. Main thread joins worker, then frees native rings/pools.
