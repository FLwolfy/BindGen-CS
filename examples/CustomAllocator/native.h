#ifndef BGCS_CUSTOM_ALLOCATOR_EXAMPLE_H
#define BGCS_CUSTOM_ALLOCATOR_EXAMPLE_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define EXAMPLE_EXPORT __declspec(dllexport)
#else
#define EXAMPLE_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct ExampleAllocatorState ExampleAllocatorState;

EXAMPLE_EXPORT ExampleAllocatorState* example_allocator_create(void);
/* A state cannot be destroyed while it still owns allocations. */
EXAMPLE_EXPORT int example_allocator_destroy(ExampleAllocatorState* state);
/* Allocate and release are paired with the same state, physical extent and alignment. */
EXAMPLE_EXPORT void* example_allocate(ExampleAllocatorState* state, size_t byte_length, size_t alignment);
EXAMPLE_EXPORT int example_release(ExampleAllocatorState* state, void* address, size_t byte_length, size_t alignment);
EXAMPLE_EXPORT uint64_t example_allocations(const ExampleAllocatorState* state);
EXAMPLE_EXPORT uint64_t example_releases(const ExampleAllocatorState* state);
/* Independent native consumption of the managed-filled buffer. */
EXAMPLE_EXPORT uint64_t example_checksum(const unsigned char* data, size_t byte_length);
/* UTF-8 bytes, zero-terminated, read only during this call. */
EXAMPLE_EXPORT uint64_t example_utf8_length(const char* text);

#ifdef __cplusplus
}
#endif
#endif
