#include <bimg/bimg.h>
#include <stdio.h>
#include <stdint.h>

int main(void)
{
    uint32_t pixels[4] = {};
    bimg::imageSolid(pixels, 2, 2, 0xaabbccddU);
    unsigned bits = bimg::getBitsPerPixel(bimg::TextureFormat::RGBA8);
    unsigned mips = bimg::imageGetNumMips(bimg::TextureFormat::RGBA8, 8, 4, 1);
    printf("{\"format\":\"%s\",\"bitsPerPixel\":%u,\"mips\":%u,\"pixel\":%u}\n", bimg::getName(bimg::TextureFormat::RGBA8), bits, mips, pixels[3]);
    return bits == 32 && mips == 4 && pixels[3] == 0xaabbccddU ? 0 : 1;
}
