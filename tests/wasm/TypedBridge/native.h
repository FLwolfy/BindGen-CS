#pragma once

typedef struct Sample {
    int first;
    double scale;
    int second;
} Sample;

typedef Sample (*Transform)(Sample value, void* state);

int Scalar(int left, int right);
Sample Echo(Sample value);
Sample Invoke(Transform callback, Sample value, void* state);
