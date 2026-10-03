#include "api.h"
#include <stdlib.h>
#include <string.h>

struct BgcsCounter {
    int32_t value;
};

static int32_t live_count;

int32_t bgcs_add(
    int32_t left,
    int32_t right
) {
    return left + right;
}

size_t bgcs_pointer_size(void) { return sizeof(void*); }
size_t bgcs_layout_size(void) { return sizeof(BgcsLayout); }
size_t bgcs_layout_context_offset(void) { return offsetof(BgcsLayout, context); }

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
    #define EXPORT(function) if (strcmp(name, #function) == 0) { return (void*)function; }
    EXPORT(bgcs_add)
    EXPORT(bgcs_pointer_size)
    EXPORT(bgcs_layout_size)
    EXPORT(bgcs_layout_context_offset)
    EXPORT(bgcs_transform)
    EXPORT(bgcs_sum)
    EXPORT(bgcs_write)
    EXPORT(bgcs_counter_create)
    EXPORT(bgcs_counter_next)
    EXPORT(bgcs_counter_destroy)
    EXPORT(bgcs_live_count)
    EXPORT(bgcs_invoke_callback)
    EXPORT(bgcs_resolve)
    #undef EXPORT
    return NULL;
}
