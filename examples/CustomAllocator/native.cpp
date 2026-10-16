#include "native.h"
#include <atomic>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <new>

struct ExampleAllocatorState
{
    std::atomic<uint64_t> allocations{0};
    std::atomic<uint64_t> releases{0};
};

struct AllocationHeader
{
    void* original;
    ExampleAllocatorState* owner;
    size_t byte_length;
    size_t alignment;
};

ExampleAllocatorState* example_allocator_create(void)
{
    return new (std::nothrow) ExampleAllocatorState();
}

int example_allocator_destroy(ExampleAllocatorState* state)
{
    if (!state || state->allocations.load() != state->releases.load()) return 1;
    delete state;
    return 0;
}

void* example_allocate(ExampleAllocatorState* state, size_t byte_length, size_t alignment)
{
    if (!state || !byte_length || !alignment || (alignment & (alignment - 1))) return nullptr;
    if (alignment - 1 > std::numeric_limits<size_t>::max() - sizeof(AllocationHeader)) return nullptr;
    const size_t overhead = alignment - 1 + sizeof(AllocationHeader);
    if (byte_length > std::numeric_limits<size_t>::max() - overhead) return nullptr;
    void* original = std::malloc(byte_length + overhead);
    if (!original) return nullptr;
    const uintptr_t first = reinterpret_cast<uintptr_t>(original) + sizeof(AllocationHeader);
    const uintptr_t aligned = (first + alignment - 1) & ~(static_cast<uintptr_t>(alignment) - 1);
    AllocationHeader header{original, state, byte_length, alignment};
    /* memcpy also supports requested alignments smaller than alignof(AllocationHeader). */
    std::memcpy(reinterpret_cast<void*>(aligned - sizeof(header)), &header, sizeof(header));
    state->allocations.fetch_add(1);
    return reinterpret_cast<void*>(aligned);
}

int example_release(ExampleAllocatorState* state, void* address, size_t byte_length, size_t alignment)
{
    if (!state || !address) return 1;
    AllocationHeader header{};
    std::memcpy(&header, static_cast<unsigned char*>(address) - sizeof(header), sizeof(header));
    if (header.owner != state) return 2;
    if (header.byte_length != byte_length || header.alignment != alignment) return 3;
    std::free(header.original);
    state->releases.fetch_add(1);
    return 0;
}

uint64_t example_allocations(const ExampleAllocatorState* state)
{
    return state ? state->allocations.load() : 0;
}

uint64_t example_releases(const ExampleAllocatorState* state)
{
    return state ? state->releases.load() : 0;
}

uint64_t example_checksum(const unsigned char* data, size_t byte_length)
{
    uint64_t result = 0;
    for (size_t index = 0; index < byte_length; ++index) result += data[index] * (index + 1);
    return result;
}

uint64_t example_utf8_length(const char* text)
{
    return static_cast<uint64_t>(std::strlen(text));
}
