#pragma once

#include <stddef.h>
#include <stdint.h>
#include <stdbool.h>

typedef struct BgcsCounter BgcsCounter;
typedef uint64_t BgcsWideFlags;

uint64_t bgcs_uint64_transform(uint64_t value);
BgcsWideFlags bgcs_wide_flags_transform(BgcsWideFlags value);

typedef struct BgcsLayout {
    uint8_t tag;
    int32_t value;
    size_t count;
    void* context;
} BgcsLayout;

typedef enum BgcsMode { MODE_FIRST = 1, MODE_SECOND = 2 } BgcsMode;
typedef struct BgcsBits {
    unsigned int count : 8;
    BgcsMode mode : 8;
    int delta : 16;
} BgcsBits;

size_t bgcs_bits_size(void);
int32_t bgcs_bits_transform(BgcsBits* data);

typedef int32_t (*BgcsCallback)(
    BgcsCounter* counter,
    int32_t value,
    void* state
);

int32_t bgcs_add(
    int32_t left,
    int32_t right
);
size_t bgcs_pointer_size(void);
size_t bgcs_long_size(void);
long bgcs_long_transform(long value);
bool bgcs_bool_not(bool value);
size_t bgcs_layout_size(void);
size_t bgcs_layout_context_offset(void);
void bgcs_transform(BgcsLayout* data);
int32_t bgcs_sum(
    const int32_t* values,
    size_t count
);
int32_t bgcs_write(
    int32_t* values,
    size_t capacity,
    size_t* written
);
BgcsCounter* bgcs_counter_create(int32_t initial);
int32_t bgcs_counter_next(
    BgcsCounter* counter,
    int32_t delta
);
void bgcs_counter_destroy(BgcsCounter* counter);
int32_t bgcs_live_count(void);
int32_t bgcs_invoke_callback(
    BgcsCounter* counter,
    int32_t value,
    BgcsCallback callback,
    void* state
);
void* bgcs_resolve(const char* name);
