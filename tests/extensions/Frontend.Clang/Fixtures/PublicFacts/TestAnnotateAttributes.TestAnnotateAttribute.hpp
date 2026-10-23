

#if !defined(__cppast) 
#define __cppast(...)
#endif

__cppast(script, is_browsable=true, desc="a function")
void TestFunc()
{
}

enum class __cppast(script, is_browsable=true, desc="a enum") TestEnum
{
};

class __cppast(script, is_browsable=true, desc="a class") TestClass
{
  public:
    __cppast(desc="a member function")
    void TestMemberFunc();

    __cppast(desc="a member field")
    int X;
};
