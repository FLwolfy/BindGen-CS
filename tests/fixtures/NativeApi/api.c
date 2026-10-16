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

int32_t bgcs_alias_ref(int32_t* first, int32_t* second) {
    *first = 17;
    *second += 5;
    return (first == second ? 1000 : 0) + *first;
}

int32_t bgcs_alias_in_out(const int32_t* input, int32_t* output) {
    *output = *input + 3;
    return (input == output ? 1000 : 0) + *input;
}

int32_t bgcs_alias_record(BgcsLayout* record, int32_t* field) {
    record->value = 3;
    *field += 7;
    return (&record->value == field ? 1000 : 0) + record->value;
}

int32_t bgcs_alias_span(int32_t* buffer, size_t length, int32_t* value) {
    if (length < 2) return -1;
    buffer[1] = 9;
    *value += 4;
    return (buffer + 1 == value ? 1000 : 0) + buffer[1];
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

#if defined(__EMSCRIPTEN__)
typedef struct BgcsEventRegistration {
    BgcsEventCallback callback;
    void* state;
    int32_t active;
} BgcsEventRegistration;
typedef struct BgcsEventOperation {
    int32_t* values;
    size_t length;
    BgcsEventCompletion completion;
    void* state;
    int32_t active, cancelled, duplicate;
} BgcsEventOperation;
static int32_t event_live, event_errors, event_async_live, event_async_errors;
static BgcsEventOperation* event_pending;

BgcsEventCompletion bgcs_event_completion_identity(BgcsEventCompletion callback) { return callback; }

void* bgcs_event_register(BgcsEventCallback callback, void* state) {
    if (!callback) return NULL;
    BgcsEventRegistration* registration = (BgcsEventRegistration*)calloc(1, sizeof(BgcsEventRegistration));
    if (!registration) return NULL;
    registration->callback = callback; registration->state = state; event_live++;
    return registration;
}
int32_t bgcs_event_fire(void* handle, int32_t value) {
    BgcsEventRegistration* registration = (BgcsEventRegistration*)handle;
    if (!registration) return -100;
    registration->active++;
    int32_t result = registration->callback(value, registration->state);
    registration->active--;
    return result;
}
void bgcs_event_unregister(void* handle) {
    BgcsEventRegistration* registration = (BgcsEventRegistration*)handle;
    if (!registration) return;
    if (registration->active) { event_errors++; return; }
    registration->callback = NULL; registration->state = NULL;
    free(registration); event_live--;
}
int32_t bgcs_event_live(void) { return event_live; }
int32_t bgcs_event_errors(void) { return event_errors; }
void bgcs_event_async_pump(int32_t status) {
    BgcsEventOperation* operation = event_pending;
    if (!operation) return;
    operation->active++;
    int32_t sum = 0;
    for (size_t index = 0; index < operation->length; index++) sum += operation->values[index];
    if (operation->length) operation->values[0] += 1;
    operation->completion(sum, status, operation->state);
    if (operation->duplicate) operation->completion(sum + 1000, status, operation->state);
    /* Deliberately access pinned storage after managed completion returns. */
    if (operation->length > 1) operation->values[1] = 99;
    operation->active--;
}
void* bgcs_event_async_start(int32_t* values, size_t length, BgcsEventCompletion completion, void* state, int32_t immediate) {
    if (event_pending || !values || !length || !completion) return NULL;
    BgcsEventOperation* operation = (BgcsEventOperation*)calloc(1, sizeof(BgcsEventOperation));
    if (!operation) return NULL;
    operation->values = values; operation->length = length; operation->completion = completion;
    operation->state = state; operation->duplicate = immediate == 2;
    event_pending = operation; event_async_live++;
    if (immediate == 1) bgcs_event_async_pump(0);
    return operation;
}
void bgcs_event_async_cancel(void* handle) {
    BgcsEventOperation* operation = (BgcsEventOperation*)handle;
    if (operation) operation->cancelled = 1;
}
void bgcs_event_async_stop(void* handle) {
    BgcsEventOperation* operation = (BgcsEventOperation*)handle;
    if (!operation) return;
    if (operation->active) { event_async_errors++; return; }
    if (event_pending == operation) event_pending = NULL;
    operation->values = NULL; operation->completion = NULL; operation->state = NULL;
    free(operation); event_async_live--;
}
int32_t bgcs_event_async_live(void) { return event_async_live; }
int32_t bgcs_event_async_errors(void) { return event_async_errors; }
int32_t bgcs_event_async_cancel_requested(void) { return event_pending ? event_pending->cancelled : 0; }
#endif

void* bgcs_resolve(const char* name) {
    // This fixture owns its symbol catalog; a consumer module provider resolves exact names.
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
    BGCS_FIXTURE_SYMBOL(bgcs_alias_ref)
    BGCS_FIXTURE_SYMBOL(bgcs_alias_in_out)
    BGCS_FIXTURE_SYMBOL(bgcs_alias_record)
    BGCS_FIXTURE_SYMBOL(bgcs_alias_span)
    BGCS_FIXTURE_SYMBOL(bgcs_counter_create)
    BGCS_FIXTURE_SYMBOL(bgcs_counter_next)
    BGCS_FIXTURE_SYMBOL(bgcs_counter_destroy)
    BGCS_FIXTURE_SYMBOL(bgcs_live_count)
    BGCS_FIXTURE_SYMBOL(bgcs_invoke_callback)
    BGCS_FIXTURE_SYMBOL(bgcs_resolve)
    #if defined(__EMSCRIPTEN__)
    BGCS_FIXTURE_SYMBOL(bgcs_event_register)
    BGCS_FIXTURE_SYMBOL(bgcs_event_fire)
    BGCS_FIXTURE_SYMBOL(bgcs_event_unregister)
    BGCS_FIXTURE_SYMBOL(bgcs_event_live)
    BGCS_FIXTURE_SYMBOL(bgcs_event_errors)
    BGCS_FIXTURE_SYMBOL(bgcs_event_async_start)
    BGCS_FIXTURE_SYMBOL(bgcs_event_completion_identity)
    BGCS_FIXTURE_SYMBOL(bgcs_event_async_pump)
    BGCS_FIXTURE_SYMBOL(bgcs_event_async_cancel)
    BGCS_FIXTURE_SYMBOL(bgcs_event_async_stop)
    BGCS_FIXTURE_SYMBOL(bgcs_event_async_live)
    BGCS_FIXTURE_SYMBOL(bgcs_event_async_errors)
    BGCS_FIXTURE_SYMBOL(bgcs_event_async_cancel_requested)
    #endif
    #undef BGCS_FIXTURE_SYMBOL
    return NULL;
}
