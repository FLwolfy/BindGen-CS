

#if !defined(__cppast)
#define __cppast(...)
#endif

#define UUID() 12345

__cppast(id=UUID(), desc="a function with macro")
void TestFunc()
{
}

