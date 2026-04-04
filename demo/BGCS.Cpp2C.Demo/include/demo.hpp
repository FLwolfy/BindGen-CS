#pragma once
#include "demo_common.hpp"

namespace demo
{
    class Counter
    {
    public:
        virtual ~Counter() = default;
        virtual int Add(int a, int b) = 0;
        virtual Mode GetMode() = 0;
    };
}
