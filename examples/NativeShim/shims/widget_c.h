#pragma once

#include <stddef.h>

#if defined(_WIN32)
#define WIDGET_EXPORT __declspec(dllexport)
#else
#define WIDGET_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

WIDGET_EXPORT void* bgcs_widget_create(void);
WIDGET_EXPORT void bgcs_widget_destroy(void* widget);
WIDGET_EXPORT int bgcs_widget_sum(void* widget, const int* values, size_t count, int* result);

#ifdef __cplusplus
}
#endif
