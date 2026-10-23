

#if !defined(__cppast)
#define __cppast(...)
#endif

template <typename T>
class TestTemplateClass
{
  public:

    __cppast(desc="a template member field")
    T X;
};

using IntClass __cppast(desc="a template class for int") = TestTemplateClass<int>;
using DoubleClass __cppast(desc="a template class for double") = TestTemplateClass<double>;

typedef TestTemplateClass<float> __cppast(desc="a template class for float") FloatClass;
