#include "basis_luau_runtime.h"

#include <limits.h>
#include <stdint.h>
#include <string.h>

enum {
    LBC_VERSION_MIN = 3,
    LBC_VERSION_MAX = 6,
    LBC_TYPE_VERSION_MIN = 1,
    LBC_TYPE_VERSION_MAX = 3,
    BASIS_LUAU_MAX_STRING_COUNT = 65536,
    BASIS_LUAU_MAX_PROTO_COUNT = 8192,
    BASIS_LUAU_MAX_VARINT = 0x0fffffffu,
};

static int check_bounds(size_t offset, size_t add, size_t size)
{
    if (add > size || offset > size - add) {
        return 0;
    }
    return 1;
}

static int read_u8(const uint8_t* data, size_t size, size_t* offset, uint8_t* out)
{
    if (!check_bounds(*offset, 1, size)) {
        return 0;
    }
    *out = data[*offset];
    *offset += 1;
    return 1;
}

static int read_varint(const uint8_t* data, size_t size, size_t* offset, uint32_t* out)
{
    uint32_t result = 0;
    unsigned int shift = 0;
    for (int i = 0; i < 5; ++i) {
        uint8_t byte;
        if (!read_u8(data, size, offset, &byte)) {
            return 0;
        }
        result |= (uint32_t)(byte & 127u) << shift;
        if ((byte & 128u) == 0) {
            if (result > BASIS_LUAU_MAX_VARINT) {
                return 0;
            }
            *out = result;
            return 1;
        }
        shift += 7;
        if (shift >= 32) {
            return 0;
        }
    }
    return 0;
}

static int skip_bytes(size_t* offset, size_t size, size_t count)
{
    if (!check_bounds(*offset, count, size)) {
        return 0;
    }
    *offset += count;
    return 1;
}

static int skip_proto_body(const uint8_t* data, size_t size, size_t* offset, uint8_t version, uint8_t typesversion)
{
    uint8_t u8;
    uint32_t var;

    if (!read_u8(data, size, offset, &u8)) return 0;
    if (!read_u8(data, size, offset, &u8)) return 0;
    if (!read_u8(data, size, offset, &u8)) return 0;
    if (!read_u8(data, size, offset, &u8)) return 0;

    if (version >= 4) {
        if (!read_u8(data, size, offset, &u8)) return 0;
        if (!read_varint(data, size, offset, &var)) return 0;
        if (!skip_bytes(offset, size, var)) return 0;
    }

    if (!read_varint(data, size, offset, &var)) return 0;
    if (!skip_bytes(offset, size, var)) return 0;

    if (!read_varint(data, size, offset, &var)) return 0;
    if (!skip_bytes(offset, size, var * 4u)) return 0;

    if (!read_varint(data, size, offset, &var)) return 0;
    for (uint32_t i = 0; i < var; ++i) {
        if (!read_varint(data, size, offset, &var)) return 0;
        if (!skip_bytes(offset, size, var)) return 0;
    }

    if (!read_varint(data, size, offset, &var)) return 0;
    for (uint32_t i = 0; i < var; ++i) {
        if (!read_varint(data, size, offset, &var)) return 0;
    }

    if (!read_varint(data, size, offset, &var)) return 0;
    for (uint32_t i = 0; i < var; ++i) {
        if (!read_u8(data, size, offset, &u8)) return 0;
    }

    if (!read_varint(data, size, offset, &var)) return 0;
    for (uint32_t i = 0; i < var; ++i) {
        if (!read_varint(data, size, offset, &var)) return 0;
    }

    if (!read_varint(data, size, offset, &var)) return 0;
    for (uint32_t i = 0; i < var; ++i) {
        if (!read_varint(data, size, offset, &var)) return 0;
    }

    (void)typesversion;
    return 1;
}

BASIS_LUAU_API basis_luau_verify_error basis_luau_verify_bytecode(const uint8_t* data, size_t size, size_t max_bytes)
{
    if (!data || size == 0) {
        return basis_luau_verify_empty;
    }
    if (size > max_bytes) {
        return basis_luau_verify_limit_exceeded;
    }

    size_t offset = 0;
    uint8_t version;
    if (!read_u8(data, size, &offset, &version)) {
        return basis_luau_verify_truncated;
    }
    if (version == 0) {
        return basis_luau_verify_malformed;
    }
    if (version < LBC_VERSION_MIN || version > LBC_VERSION_MAX) {
        return basis_luau_verify_bad_version;
    }

    uint8_t typesversion = 0;
    if (version >= 4) {
        if (!read_u8(data, size, &offset, &typesversion)) {
            return basis_luau_verify_truncated;
        }
        if (typesversion < LBC_TYPE_VERSION_MIN || typesversion > LBC_TYPE_VERSION_MAX) {
            return basis_luau_verify_bad_type_version;
        }
    }

    uint32_t stringCount;
    if (!read_varint(data, size, &offset, &stringCount)) {
        return basis_luau_verify_truncated;
    }
    if (stringCount > BASIS_LUAU_MAX_STRING_COUNT) {
        return basis_luau_verify_limit_exceeded;
    }
    for (uint32_t i = 0; i < stringCount; ++i) {
        uint32_t length;
        if (!read_varint(data, size, &offset, &length)) {
            return basis_luau_verify_truncated;
        }
        if (!skip_bytes(&offset, size, length)) {
            return basis_luau_verify_truncated;
        }
    }

    if (typesversion == 3) {
        uint8_t index;
        if (!read_u8(data, size, &offset, &index)) {
            return basis_luau_verify_truncated;
        }
        while (index != 0) {
            if (!read_varint(data, size, &offset, &stringCount)) {
                return basis_luau_verify_truncated;
            }
            if (!read_u8(data, size, &offset, &index)) {
                return basis_luau_verify_truncated;
            }
        }
    }

    uint32_t protoCount;
    if (!read_varint(data, size, &offset, &protoCount)) {
        return basis_luau_verify_truncated;
    }
    if (protoCount > BASIS_LUAU_MAX_PROTO_COUNT) {
        return basis_luau_verify_limit_exceeded;
    }
    for (uint32_t i = 0; i < protoCount; ++i) {
        if (!skip_proto_body(data, size, &offset, version, typesversion)) {
            return basis_luau_verify_malformed;
        }
    }

    if (offset != size) {
        return basis_luau_verify_malformed;
    }
    return basis_luau_verify_ok;
}
