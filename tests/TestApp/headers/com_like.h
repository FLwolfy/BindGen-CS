#define DEFINE_GUID(name, l, w1, w2, b1,b2,b3,b4,b5,b6,b7,b8)
DEFINE_GUID(IID_IMyInterface, 0x12345678, 0x1234, 0x5678, 0x90, 0xab, 0xcd, 0xef, 0x12, 0x34, 0x56, 0x78);

typedef struct IMyInterfaceVtbl
{
    void* QueryInterface;
    void* AddRef;
    void* Release;
} IMyInterfaceVtbl;

typedef struct IMyInterface
{
    IMyInterfaceVtbl* lpVtbl;
} IMyInterface;

void Bgcs_UseCom(IMyInterface* value);
