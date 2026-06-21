# Bytecode acceptance contract

## Format on wire / in bundles

```
[ luau bytecode bytes (verified subset) ]
[ 32-byte HMAC-SHA256 signature ]
```

Signature covers bytecode bytes only (not the signature itself).

## Editor obligations

1. Compile source with trusted Luau compiler (Editor-only).
2. Run native `basis_luau_verify_bytecode` before signing.
3. Sign with host-specific key material from `BasisLuauBytecodeSigner`.
4. Store signed payload on `LuauScriptProxy` / export bundle.

## Runtime obligations

1. Reject payloads shorter than 32 bytes or failing signature verify.
2. Reject bytecode failing bounded verifier (no `luau_load` on unverified input).
3. Enforce max bytecode size (`BasisLuauLimits.MaxBytecodeBytes`).
4. Run `luau_load` only inside `basis_luau_begin_execution` / `basis_luau_end_execution`.

## Dev bridge exception

When `BASIS_LUAU_DEV_BRIDGE` is defined **and** `BasisLuauRuntimeSettings.AllowUnsignedBytecodeInDev` is true, signature may be skipped in Editor/Development builds only. Never in release player builds.

## Key rotation

- Key id is first byte of signature block (optional v2); v1 uses fixed project key in `BasisLuauBytecodeSigner`.
- Document rotation in release notes when key id changes.
