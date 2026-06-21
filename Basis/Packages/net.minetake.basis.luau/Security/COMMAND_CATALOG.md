# Command catalog v1 (deny-by-default)

Only IDs listed in `LuauCommandCatalog` may be enqueued. Unregistered IDs are rejected at native enqueue and main-thread flush.

## Transform / time (Phase 2+)

| ID | Name | Flush | Notes |
|----|------|-------|-------|
| 1 | SetPosition | Deferred N+1 | content-root validate |
| 2 | SetRotation | Deferred N+1 | |
| 3 | SetLocalPosition | Deferred N+1 | |
| 4 | Rotate | Deferred N+1 | |
| 5 | DestroyObject | Tombstone + N+1 flush | handle invalid immediately in shadow |

## Snapshot getters (worker read-only)

| ID | Name | Source |
|----|------|--------|
| 64 | GetPosition | RCU snapshot |
| 65 | GetRotation | RCU snapshot |

## Tickets (async result)

| ID | Name | Notes |
|----|------|-------|
| 128 | Clone | ticket callback with new handle |
| 129 | DownloadImage | audited URL + size limits |

## Events (main → worker)

| ID | Name |
|----|------|
| 192 | NetworkMessage |
| 193 | OscMessage |
| 194 | TriggerEnter |

Legacy `basis_object` / reflection APIs are **not** in this catalog and must be unreachable when `BASIS_LUAU_RUNTIME` is enabled.
