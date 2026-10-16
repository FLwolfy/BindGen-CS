#define CIMGUI_DEFINE_ENUMS_AND_STRUCTS
#include <cimguizmo.h>
#include <stdio.h>
#include <math.h>

int main(void)
{
    const float translation[] = { 11, 13, 17 }, rotation[] = { 0, 0, 0 }, scale[] = { 2, 3, 4 };
    float matrix[16] = {}, actual_translation[3] = {}, actual_rotation[3] = {}, actual_scale[3] = {};
    ImGuizmo_RecomposeMatrixFromComponents(translation, rotation, scale, matrix);
    ImGuizmo_DecomposeMatrixToComponents(matrix, actual_translation, actual_rotation, actual_scale);
    printf("{\"translation\":[%.9g,%.9g,%.9g],\"scale\":[%.9g,%.9g,%.9g],\"matrixTranslation\":[%.9g,%.9g,%.9g]}\n",
        actual_translation[0], actual_translation[1], actual_translation[2], actual_scale[0], actual_scale[1], actual_scale[2], matrix[12], matrix[13], matrix[14]);
    for (int index = 0; index < 3; index++)
        if (fabsf(actual_translation[index] - translation[index]) > 0.0001f || fabsf(actual_scale[index] - scale[index]) > 0.0001f) return 1;
    return 0;
}
