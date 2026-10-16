#include <SDL3/SDL.h>
#include <stdio.h>

int main(void)
{
    int version = SDL_GetVersion();
    int absolute = SDL_abs(-37);
    printf("{\"version\":%d,\"absolute\":%d,\"revision\":\"%s\"}\n", version, absolute, SDL_GetRevision());
    return version >= 3000000 && absolute == 37 ? 0 : 1;
}
