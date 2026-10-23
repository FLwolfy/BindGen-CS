#ifndef BGCS_PUBLIC_FACT_GUID_SHAPE
#define BGCS_PUBLIC_FACT_GUID_SHAPE
/* Repository-owned fixture: only the 16-byte GUID declaration shape used by the expression source. */
typedef struct _GUID {
    unsigned long Data1;
    unsigned short Data2;
    unsigned short Data3;
    unsigned char Data4[8];
} GUID;
#define DEFINE_GUID(name,l,w1,w2,b1,b2,b3,b4,b5,b6,b7,b8) \
    extern "C" const GUID name = { l, w1, w2, { b1, b2, b3, b4, b5, b6, b7, b8 } }
#endif
