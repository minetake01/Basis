# Command catalog v6 (deny-by-default)

Only IDs listed in `LuauCommandCatalog` may be enqueued. Unregistered IDs are rejected at native enqueue and main-thread flush.

## Transform / UI / util (worker → main)

| ID | Name | Flush | Notes |
|----|------|-------|-------|
| 1 | SetPosition | Deferred N+1 | content-root validate |
| 2 | SetRotation | Deferred N+1 | |
| 3 | SetLocalPosition | Deferred N+1 | |
| 4 | Rotate | Deferred N+1 | |
| 5 | DestroyObject | Immediate | tombstone handle |
| 6 | SetUiText | Immediate | TMP via buffer pool |
| 7 | Log | Immediate | |
| 8 | Warn | Immediate | |
| 9 | Error | Immediate | |

## Snapshot getters (worker read-only)

| ID | Name | Source |
|----|------|--------|
| 64 | GetPosition | RCU snapshot |
| 65 | GetRotation | RCU snapshot |
| 66 | GetLocalPosition | RCU snapshot |

## Tickets (async / main-thread service)

| ID | Name | Notes |
|----|------|-------|
| 128 | Clone | returns new handle |
| 129 | DownloadImage | audited URL |
| 130 | NetworkSend | |
| 131 | TakeOwnership | |
| 132 | MakeNetworkable | |
| 133 | MakeInteractable | |
| 140–143 | OscPublish (float/int/bool/string) | |
| 144 | OscSubscribe | registers OSC + lua callback ref |
| 150 | AvatarResolve | |
| 151 | PlayerTeleport | reserved |
| 152 | PlayerRespawn | reserved |
| 153 | VixxyGet | reserved |
| 154 | VixxyApply | reserved |
| 155 | InteractPress | reserved |

## Events (main → worker via event ring)

| ID | Name |
|----|------|
| 192 | NetworkMessage |
| 193 | OscMessage |
| 194 | TriggerEnter |
| 195 | TriggerExit |
| 196 | CollisionEnter |
| 197 | CollisionExit |

## Internal

| ID | Name |
|----|------|
| 255 | Shutdown |

Legacy `basis_object` / reflection whitelist APIs are **not** in this catalog.

Native Lua modules: `transform`, `basis_time`, `basis_ui`, `basis_util`, `basis_instantiate`, `basis_image`, `basis_osc`, `basis_network`, `basis_avatar`, `basis_interact`.
