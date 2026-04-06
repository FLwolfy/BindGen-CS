#define BGCS_MAX_VALUE 4096
#define BGCS_DEFAULT_TIMEOUT 1000
#define BGCS_IGNORED_CONST 77
#define BGCS_OLD_CONST 7

typedef struct BgcsPoint
{
    int X;
    int Y;
} BgcsPoint;

typedef struct BgcsRect
{
    float Left;
    float Top;
    float Right;
    float Bottom;
} BgcsRect;

typedef struct BgcsStats
{
    int Frame;
    float DeltaTime;
} BgcsStats;

typedef enum BgcsMode
{
    BGCS_MODE_FAST = 1,
    BGCS_MODE_SAFE = 2,
    BGCS_MODE_DEBUG = 4
} BgcsMode;

typedef enum BgcsColor
{
    BGCS_COLOR_RED = 0,
    BGCS_COLOR_GREEN = 1,
    BGCS_COLOR_BLUE = 2
} BgcsColor;

typedef enum BgcsMode BgcsModeAlias;

typedef void (*BgcsCallback)(int value);
typedef void (*BgcsLogCallback)(const char* message);

typedef void* BgcsHandle;

void Bgcs_UpdatePoint(BgcsPoint* value);
void Bgcs_SetMode(BgcsMode mode);
void Bgcs_SetCallback(BgcsCallback callback);
int Bgcs_Add(int a, int b);
int Bgcs_Sub(int a, int b);
void Bgcs_GetStats(BgcsStats* outStats);
void Bgcs_SetRect(BgcsRect rect);
void Bgcs_SetLogCallback(BgcsLogCallback callback);
void Bgcs_SetHandle(BgcsHandle handle);
void bgcs_ignore_me(void);
void Bgcs_Internal_DebugOnly(int flag);
