cmake_minimum_required(VERSION 3.18)
project(basis_mediastream_native C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 14)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(UNITY_PLUGIN_API_DIR "" CACHE PATH "Path to Unity Editor/Data/PluginAPI headers")

set(CORE_SOURCES
    basis_mediastream_core.c
)

set(PLATFORM_SOURCES "")
set(BASIS_LIBRARY_TYPE SHARED)
set(BASIS_IS_REAL_BACKEND OFF)

set(SPOUT2_ROOT ${CMAKE_CURRENT_SOURCE_DIR}/third_party/Spout2/SPOUTSDK)
set(SPOUT2_GL   ${SPOUT2_ROOT}/SpoutGL)
set(SPOUT2_DX   ${SPOUT2_ROOT}/SpoutDirectX/SpoutDX)

if(WIN32 AND CMAKE_SIZEOF_VOID_P EQUAL 8 AND NOT (CMAKE_GENERATOR_PLATFORM STREQUAL "ARM64"))
    if(EXISTS ${SPOUT2_DX}/SpoutDX.cpp)
        set(BASIS_IS_REAL_BACKEND ON)
        list(APPEND PLATFORM_SOURCES
            windows/basis_mediastream_spout.cpp
            unity/basis_mediastream_unity_plugin.cpp
            ${SPOUT2_DX}/SpoutDX.cpp
            ${SPOUT2_GL}/SpoutDirectX.cpp
            ${SPOUT2_GL}/SpoutSenderNames.cpp
            ${SPOUT2_GL}/SpoutSharedMemory.cpp
            ${SPOUT2_GL}/SpoutFrameCount.cpp
            ${SPOUT2_GL}/SpoutCopy.cpp
            ${SPOUT2_GL}/SpoutUtils.cpp
        )
    else()
        message(WARNING "Spout2 sources not found in third_party/Spout2 — building stub backend.")
        list(APPEND PLATFORM_SOURCES other/basis_mediastream_stub.c)
    endif()
else()
    list(APPEND PLATFORM_SOURCES other/basis_mediastream_stub.c)
    if(IOS OR CMAKE_SYSTEM_NAME STREQUAL "iOS")
        set(BASIS_LIBRARY_TYPE STATIC)
    endif()
endif()

add_library(basis_mediastream_native ${BASIS_LIBRARY_TYPE} ${CORE_SOURCES} ${PLATFORM_SOURCES})
target_include_directories(basis_mediastream_native PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})

if(BASIS_IS_REAL_BACKEND)
    target_include_directories(basis_mediastream_native PRIVATE
        ${SPOUT2_DX}
        ${SPOUT2_GL})
    if(UNITY_PLUGIN_API_DIR)
        target_include_directories(basis_mediastream_native PRIVATE ${UNITY_PLUGIN_API_DIR})
    endif()
    target_include_directories(basis_mediastream_native PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/unity)
    target_link_libraries(basis_mediastream_native PRIVATE d3d11 dxgi psapi)
endif()

if(MSVC)
    target_compile_options(basis_mediastream_native PRIVATE /W3 /EHsc)
    target_compile_definitions(basis_mediastream_native PRIVATE _CRT_SECURE_NO_WARNINGS)
else()
    target_compile_options(basis_mediastream_native PRIVATE -Wall -Wextra -Wno-unused-parameter)
endif()

set_target_properties(basis_mediastream_native PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/../Plugins/Windows/x86_64"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_SOURCE_DIR}/../Plugins/Windows/x86_64"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_CURRENT_SOURCE_DIR}/../Plugins/Windows/x86_64"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/../Plugins/Windows/x86_64"
    LIBRARY_OUTPUT_DIRECTORY_RELEASE "${CMAKE_CURRENT_SOURCE_DIR}/../Plugins/Windows/x86_64"
    LIBRARY_OUTPUT_DIRECTORY_DEBUG "${CMAKE_CURRENT_SOURCE_DIR}/../Plugins/Windows/x86_64")
