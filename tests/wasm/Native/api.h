#pragma once

#include <stddef.h>
#include <stdint.h>

typedef struct BgcsCounter BgcsCounter;

typedef struct BgcsLayout {
    uint8_t tag;
    int32_t value;
    size_t count;
    void* context;
} BgcsLayout;

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
