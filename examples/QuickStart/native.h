#pragma once

#if defined(_WIN32)
#define QUICKSTART_EXPORT __declspec(dllexport)
#else
#define QUICKSTART_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

QUICKSTART_EXPORT int bgcs_add(int left, int right);

#ifdef __cplusplus
}
#endif
