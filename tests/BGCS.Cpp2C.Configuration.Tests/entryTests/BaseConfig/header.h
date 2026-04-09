#if !defined(FROM_BASE)
#error FROM_BASE is required
#endif

#include "dep.hpp"

class Counter
{
public:
    virtual int Add(int a, int b);
};
