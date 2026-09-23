#pragma once

#include <cstddef>

namespace Example
{
class Widget
{
public:
    int Sum(const int* values, std::size_t count) const
    {
        int result = 0;
        for (std::size_t index = 0; index < count; ++index)
            result += values[index];
        return result;
    }
};
}
