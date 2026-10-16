#include <bgfx/c99/bgfx.h>
#include <stdio.h>

int main(void)
{
    fprintf(stderr, "bgfx probe: entered main\n");
    bgfx_vertex_layout_t layout = {};
    bgfx_vertex_layout_begin(&layout, BGFX_RENDERER_TYPE_NOOP);
    bgfx_vertex_layout_add(&layout, BGFX_ATTRIB_POSITION, 3, BGFX_ATTRIB_TYPE_FLOAT, false, false);
    bgfx_vertex_layout_add(&layout, BGFX_ATTRIB_TEXCOORD0, 2, BGFX_ATTRIB_TYPE_FLOAT, false, false);
    bgfx_vertex_layout_end(&layout);
    fprintf(stderr, "bgfx probe: vertex layout completed\n");
    bgfx_texture_info_t texture = {};
    bgfx_calc_texture_size(&texture, 8, 4, 1, false, false, 1, BGFX_TEXTURE_FORMAT_RGBA8);
    fprintf(stderr, "bgfx probe: texture size completed\n");
    unsigned stride = bgfx_vertex_layout_get_stride(&layout), offset = bgfx_vertex_layout_get_offset(&layout, BGFX_ATTRIB_TEXCOORD0);
    unsigned size = bgfx_vertex_layout_get_size(&layout, 7);
    printf("{\"stride\":%u,\"texcoordOffset\":%u,\"sevenVertices\":%u,\"textureBytes\":%u,\"renderer\":\"%s\"}\n",
        stride, offset, size, texture.storageSize, bgfx_get_renderer_name(BGFX_RENDERER_TYPE_NOOP));
    fflush(stdout);
    return stride == 20 && offset == 12 && size == 140 && texture.storageSize == 128 ? 0 : 1;
}
