#define CIMGUI_DEFINE_ENUMS_AND_STRUCTS
#include <cimgui.h>
#include <stdio.h>
#include <string.h>

int main(void)
{
    ImGuiTextBuffer* buffer = ImGuiTextBuffer_ImGuiTextBuffer();
    const char text[] = "BGCS native corpus";
    ImGuiTextBuffer_append(buffer, text, text + strlen(text));
    int size = ImGuiTextBuffer_size(buffer);
    bool same = strcmp(ImGuiTextBuffer_c_str(buffer), text) == 0;
    ImGuiTextBuffer_destroy(buffer);
    printf("{\"version\":\"%s\",\"bufferSize\":%d,\"bufferMatches\":%s}\n", igGetVersion(), size, same ? "true" : "false");
    return same && size == 18 ? 0 : 1;
}
