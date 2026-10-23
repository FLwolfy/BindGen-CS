
namespace ns1 {
class TmpClass;
}

namespace ns2 {
const ns1::TmpClass* tmpClass1;
volatile ns1::TmpClass* tmpClass2;
const unsigned int * const dummy_pu32 = (const unsigned int * const)0x12345678;
}

namespace ns1 {
class TmpClass {
};
}
