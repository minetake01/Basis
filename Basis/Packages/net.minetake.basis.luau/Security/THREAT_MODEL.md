# Luau UGC Threat Model

## Assets

- Main-thread frame budget (VR 90Hz target)
- Co-resident Luau hosts (Prop / Scene / Avatar) in one process
- UnityEngine objects reachable from scripts
- Native heap (VM, rings, buffer pools)

## Trust boundaries

| Trusted | Untrusted |
|---------|-----------|
| Basis Editor compile + signing pipeline | Serialized `byte[]` bytecode in UGC bundles |
| `basis_luau_runtime` native code | Luau script author input |
| Main-thread flush validators | VM userdata lifetime (may escape callbacks) |

## Attack surfaces (legacy runtime)

1. Crafted bytecode → native loader OOB / VM panic at load
2. `basis_object` reflection → methods allowed by default for whitelisted types
3. Cross-host handles without content-root validation on all paths
4. Ring/buffer flood → shard starvation
5. HTTP/network/OSC → SSRF, memory bombs, bandwidth abuse

## Security goals (in-process)

- Malicious UGC fail-stops **only its host/proxy**; benign hosts meet p99 gates under mixed load
- No reflection or arbitrary Unity API paths at worker GA
- Bytecode loads only after **verifier + signature**
- Absolute VM exploit immunity is **out of scope**; optional future process/WASM isolation documented separately

## References

- [BYTECODE_CONTRACT.md](BYTECODE_CONTRACT.md)
- [FAILURE_POLICY.md](FAILURE_POLICY.md)
- [COMMAND_CATALOG.md](COMMAND_CATALOG.md)
