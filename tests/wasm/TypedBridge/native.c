#include "native.h"

int Scalar(int left, int right) { return left + right; }
Sample Echo(Sample value) {
    value.first += 5;
    value.scale *= 2;
    value.second -= 3;
    return value;
}
Sample Invoke(Transform callback, Sample value, void* state) {
    return callback(value, state);
}
