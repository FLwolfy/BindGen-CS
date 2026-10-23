

#if !defined(__cppast)
#define __cppast(...)
#endif

class __cppast(script, is_browsable=true, desc="a class") TestClass
{
  public:
    __cppast(desc="a member function")
    __cppast(desc2="a member function 2")
    void TestMemberFunc();

    __cppast(desc="a member field")
    __cppast(desc2="a member field 2")
    int X;
};

