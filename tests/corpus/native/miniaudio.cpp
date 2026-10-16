#include <miniaudio.h>
#include <stdio.h>

int main(void)
{
    ma_uint32 major = 0, minor = 0, patch = 0;
    ma_version(&major, &minor, &patch);
    printf("{\"major\":%u,\"minor\":%u,\"patch\":%u,\"version\":\"%s\"}\n", major, minor, patch, ma_version_string());
    return major == 0 && minor > 0 ? 0 : 1;
}
