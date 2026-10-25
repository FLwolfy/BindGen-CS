#include "native.h"

int Scalar(int left, int right) { return left + right; }
unsigned long long Scalar64(void* state) {
    (void)state;
    return 0xfedcba9876543210ULL;
}
unsigned long long Mixed64(unsigned long long value, int increment, double scale, void* state) {
    return value + (unsigned long long)increment + (unsigned long long)scale + (unsigned long long)*(int*)state;
}
unsigned long long Invoke64(ScalarCallback64 callback, void* state) { return callback(state); }
Sample Echo(Sample value) {
    value.first += 5;
    value.scale *= 2;
    value.second -= 3;
    value.storage.marker += 1;
    return value;
}
Sample Invoke(Transform callback, Sample value, void* state) {
    return callback(value, state);
}

#if defined(__EMSCRIPTEN__)
int SystemStorageSize(const SystemStorage* unused) {
    (void)unused;
    return (int)sizeof(SystemStorage);
}
#endif
