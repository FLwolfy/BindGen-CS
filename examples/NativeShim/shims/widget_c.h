#pragma once

#include "common.h"
#include <stddef.h>

API(void*) bgcs_widget_create(void);
API(void) bgcs_widget_destroy(void* widget);
API(int) bgcs_widget_sum(void* widget, const int* values, size_t count, int* result);
