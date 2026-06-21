#include "basis_luau_runtime.h"

#include <limits.h>
#include <stdint.h>
#include <string.h>

enum {
    LBC_VERSION_MIN = 3,
    LBC_VERSION_MAX = 6,
    LBC_TYPE_VERSION_MIN = 1,
    LBC_TYPE_VERSION_MAX = 3,
    LBC_CONSTANT_NIL = 0,
    LBC_CONSTANT_BOOLEAN = 1,
    LBC_CONSTANT_NUMBER = 2,
    LBC_CONSTANT_STRING = 3,
    LBC_CONSTANT_IMPORT = 4,
    LBC_CONSTANT_TABLE = 5,
    LBC_CONSTANT_CLOSURE = 6,
    LBC_CONSTANT_VECTOR = 7,
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

static int skip_string_ref(const uint8_t* data, size_t size, size_t* offset)
{
    uint32_t id;
    return read_varint(data, size, offset, &id);
}

static int skip_constant(const uint8_t* data, size_t size, size_t* offset)
{
    uint8_t tag;
    uint32_t var;

    if (!read_u8(data, size, offset, &tag)) {
        return 0;
    }

    switch (tag) {
    case LBC_CONSTANT_NIL:
        return 1;
    case LBC_CONSTANT_BOOLEAN:
        return skip_bytes(offset, size, 1);
    case LBC_CONSTANT_NUMBER:
        return skip_bytes(offset, size, sizeof(double));
    case LBC_CONSTANT_VECTOR:
        return skip_bytes(offset, size, sizeof(float) * 4);
    case LBC_CONSTANT_STRING:
        return skip_string_ref(data, size, offset);
    case LBC_CONSTANT_IMPORT:
        return skip_bytes(offset, size, sizeof(uint32_t));
    case LBC_CONSTANT_TABLE:
    {
        uint32_t keys;
        if (!read_varint(data, size, offset, &keys)) {
            return 0;
        }
        for (uint32_t i = 0; i < keys; ++i) {
            if (!read_varint(data, size, offset, &var)) {
                return 0;
            }
        }
        return 1;
    }
    case LBC_CONSTANT_CLOSURE:
        return read_varint(data, size, offset, &var);
    default:
        return 0;
    }
}

static int skip_proto_body(const uint8_t* data, size_t size, size_t* offset, uint8_t version, uint8_t typesversion)
{
    uint8_t u8;
    uint32_t var;
    uint32_t sizecode = 0;

    if (!read_u8(data, size, offset, &u8)) return 0;
    if (!read_u8(data, size, offset, &u8)) return 0;
    if (!read_u8(data, size, offset, &u8)) return 0;
    if (!read_u8(data, size, offset, &u8)) return 0;

    if (version >= 4) {
        if (!read_u8(data, size, offset, &u8)) return 0;
        if (typesversion >= 1 && typesversion <= 3) {
            if (!read_varint(data, size, offset, &var)) return 0;
            if (!skip_bytes(offset, size, var)) return 0;
        }
    }

    if (!read_varint(data, size, offset, &sizecode)) return 0;
    if (!skip_bytes(offset, size, (size_t)sizecode * sizeof(uint32_t))) return 0;

    if (!read_varint(data, size, offset, &var)) return 0;
    {
        uint32_t sizek = var;
        for (uint32_t i = 0; i < sizek; ++i) {
            if (!skip_constant(data, size, offset)) return 0;
        }
    }

    if (!read_varint(data, size, offset, &var)) return 0;
    {
        uint32_t sizep = var;
        for (uint32_t i = 0; i < sizep; ++i) {
            if (!read_varint(data, size, offset, &var)) return 0;
        }
    }

    if (!read_varint(data, size, offset, &var)) return 0;
    if (!skip_string_ref(data, size, offset)) return 0;

    if (!read_u8(data, size, offset, &u8)) return 0;
    if (u8) {
        if (!read_u8(data, size, offset, &u8)) return 0;
        uint32_t intervals = ((sizecode - 1u) >> u8) + 1u;
        if (!skip_bytes(offset, size, sizecode)) return 0;
        if (!skip_bytes(offset, size, (size_t)intervals * sizeof(int32_t))) return 0;
    }

    if (!read_u8(data, size, offset, &u8)) return 0;
    if (u8) {
        if (!read_varint(data, size, offset, &var)) return 0;
        {
            uint32_t sizelocvars = var;
            for (uint32_t i = 0; i < sizelocvars; ++i) {
                if (!skip_string_ref(data, size, offset)) return 0;
                if (!read_varint(data, size, offset, &var)) return 0;
                if (!read_varint(data, size, offset, &var)) return 0;
                if (!read_u8(data, size, offset, &u8)) return 0;
            }
        }

        if (!read_varint(data, size, offset, &var)) return 0;
        {
            uint32_t sizeupvalues = var;
            for (uint32_t i = 0; i < sizeupvalues; ++i) {
                if (!skip_string_ref(data, size, offset)) return 0;
            }
        }
    }

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

    uint32_t mainId;
    if (!read_varint(data, size, &offset, &mainId)) {
        return basis_luau_verify_truncated;
    }

    if (offset != size) {
        return basis_luau_verify_malformed;
    }
    return basis_luau_verify_ok;
}
