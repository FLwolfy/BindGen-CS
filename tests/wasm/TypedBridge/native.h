#pragma once

typedef struct Sample {
    int first;
    double scale;
    int second;
    union { int marker; float real; } storage;
} Sample;

#if defined(__EMSCRIPTEN__)
#include <pthread.h>
typedef struct SystemStorage { pthread_cond_t condition; pthread_mutex_t mutex; } SystemStorage;
int SystemStorageSize(const SystemStorage* unused);
#endif

typedef Sample (*Transform)(Sample value, void* state);

int Scalar(int left, int right);
Sample Echo(Sample value);
Sample Invoke(Transform callback, Sample value, void* state);
