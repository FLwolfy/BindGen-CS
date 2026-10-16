#pragma once

typedef void (*table_callback)(void* state);

int table_add(int left, int right);
int table_multiply(int left, int right);
int table_hold(table_callback callback, void* state);
