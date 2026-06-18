use std::os::raw::{c_char, c_int};

extern "C" {
    fn ffi_luaL_error_msg(L: *mut std::ffi::c_void, msg: *const c_char) -> c_int;
}

/// Linker anchor: keeps luau_ffi_extras.c linked without calling through Rust frames.
#[no_mangle]
pub extern "C" fn basis_luau_ffi_extras_anchor() -> usize {
    ffi_luaL_error_msg as usize
}

#[used]
static BASIS_LUAU_FFI_EXTRAS_LINK_ANCHOR: unsafe extern "C" fn() -> usize = basis_luau_ffi_extras_anchor;
