#include "api.h"
#include <stdlib.h>
#include <string.h>

struct BgcsCounter {
    int32_t value;
};

static int32_t live_count;

uint64_t bgcs_uint64_transform(uint64_t value) { return value ^ (UINT64_C(1) << 59); }
BgcsWideFlags bgcs_wide_flags_transform(BgcsWideFlags value) { return bgcs_uint64_transform(value); }

int32_t bgcs_add(
    int32_t left,
    int32_t right
) {
    return left + right;
}

size_t bgcs_pointer_size(void) { return sizeof(void*); }
size_t bgcs_long_size(void) { return sizeof(long); }
long bgcs_long_transform(long value) { return value * 3 - 7; }
bool bgcs_bool_not(bool value) { return !value; }
size_t bgcs_layout_size(void) { return sizeof(BgcsLayout); }
size_t bgcs_layout_context_offset(void) { return offsetof(BgcsLayout, context); }

size_t bgcs_bits_size(void) { return sizeof(BgcsBits); }
int32_t bgcs_bits_transform(BgcsBits* data) {
    int32_t valid = data->count == 17 && data->mode == MODE_SECOND && data->delta == -123;
    data->count += 1;
    data->mode = MODE_FIRST;
    data->delta *= 2;
    return valid;
}

void bgcs_transform(BgcsLayout* data) {
    data->tag += 1;
    data->value *= 2;
    data->count += 3;
}

int32_t bgcs_sum(
    const int32_t* values,
    size_t count
) {
    int32_t total = 0;
    for (size_t index = 0; index < count; index++) {
        total += values[index];
    }
    return total;
}

int32_t bgcs_write(
    int32_t* values,
    size_t capacity,
    size_t* written
) {
    *written = 0;
    if (capacity < 3) {
        return 0;
    }
    values[0] = 7;
    values[1] = 11;
    values[2] = 13;
    *written = 3;
    return 1;
}

BgcsCounter* bgcs_counter_create(int32_t initial) {
    BgcsCounter* counter = malloc(sizeof(BgcsCounter));
    if (counter != NULL) {
        counter->value = initial;
        live_count++;
    }
    return counter;
}

int32_t bgcs_counter_next(
    BgcsCounter* counter,
    int32_t delta
) {
    if (counter == NULL) {
        return -1;
    }
    counter->value += delta;
    return counter->value;
}

void bgcs_counter_destroy(BgcsCounter* counter) {
    if (counter != NULL) {
        free(counter);
        live_count--;
    }
}

int32_t bgcs_live_count(void) { return live_count; }

int32_t bgcs_invoke_callback(
    BgcsCounter* counter,
    int32_t value,
    BgcsCallback callback,
    void* state
) {
    return callback == NULL ? -1 : callback(counter, value, state);
}

void* bgcs_resolve(const char* name) {
    // This fixture owns its symbol catalog. BGCS consumes it through INativeContext.
    #define BGCS_FIXTURE_SYMBOL(function) if (strcmp(name, #function) == 0) { return (void*)function; }
    BGCS_FIXTURE_SYMBOL(bgcs_add)
    BGCS_FIXTURE_SYMBOL(bgcs_uint64_transform)
    BGCS_FIXTURE_SYMBOL(bgcs_wide_flags_transform)
    BGCS_FIXTURE_SYMBOL(bgcs_pointer_size)
    BGCS_FIXTURE_SYMBOL(bgcs_long_size)
    BGCS_FIXTURE_SYMBOL(bgcs_long_transform)
    BGCS_FIXTURE_SYMBOL(bgcs_bool_not)
    BGCS_FIXTURE_SYMBOL(bgcs_layout_size)
    BGCS_FIXTURE_SYMBOL(bgcs_layout_context_offset)
    BGCS_FIXTURE_SYMBOL(bgcs_bits_size)
    BGCS_FIXTURE_SYMBOL(bgcs_bits_transform)
    BGCS_FIXTURE_SYMBOL(bgcs_transform)
    BGCS_FIXTURE_SYMBOL(bgcs_sum)
    BGCS_FIXTURE_SYMBOL(bgcs_write)
    BGCS_FIXTURE_SYMBOL(bgcs_counter_create)
    BGCS_FIXTURE_SYMBOL(bgcs_counter_next)
    BGCS_FIXTURE_SYMBOL(bgcs_counter_destroy)
    BGCS_FIXTURE_SYMBOL(bgcs_live_count)
    BGCS_FIXTURE_SYMBOL(bgcs_invoke_callback)
    BGCS_FIXTURE_SYMBOL(bgcs_resolve)
    #undef BGCS_FIXTURE_SYMBOL
    return NULL;
}
