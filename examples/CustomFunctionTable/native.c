#include "native.h"
#include <stddef.h>

#if defined(_WIN32)
#define TABLE_EXPORT __declspec(dllexport)
#else
#define TABLE_EXPORT __attribute__((visibility("default")))
#endif

#define TABLE_IDENTITY "custom-table/v1;Cdecl;pointer-sized-slots;7;table_add:i32(i32,i32)@6;table_multiply:i32(i32,i32)@2;table_hold:i32(callback(void*),void*)@4"

int table_add(int left, int right) { return left + right; }
int table_multiply(int left, int right) { return left * right; }
int table_hold(table_callback callback, void* state) { callback(state); return 42; }

typedef void (*table_slot)(void);
static table_slot slots[7] = { 0, 0, (table_slot)table_multiply, 0, (table_slot)table_hold, 0, (table_slot)table_add };

#if defined(BGCS_CUSTOM_TABLE_TEST_FAULTS)
static int fault;
TABLE_EXPORT void custom_table_set_fault(int value) { fault = value; }
#else
#define fault 0
#endif

TABLE_EXPORT const table_slot* custom_table_get(void) { return fault == 5 ? 0 : slots; }
TABLE_EXPORT size_t custom_table_count(void) { return fault == 2 ? 6 : 7; }
TABLE_EXPORT size_t custom_table_pointer_size(void) { return fault == 3 ? 1 : sizeof(void*); }
TABLE_EXPORT size_t custom_table_slot_size(void) { return fault == 4 ? 1 : sizeof(table_slot); }
TABLE_EXPORT const char* custom_table_identity(void) { return fault == 1 ? "incompatible-table" : TABLE_IDENTITY; }
