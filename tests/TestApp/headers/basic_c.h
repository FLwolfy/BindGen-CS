typedef struct BgcsPoint
{
    int X;
    int Y;
} BgcsPoint;

typedef enum BgcsMode
{
    BgcsMode_A = 0,
    BgcsMode_B = 1
} BgcsMode;

typedef void (*BgcsCallback)(int value);

void Bgcs_UpdatePoint(BgcsPoint* value);
void Bgcs_SetMode(BgcsMode mode);
void Bgcs_SetCallback(BgcsCallback callback);
