#include "widget_c.h"
#include "library.hpp"

#include <new>

API_INTERNAL(void*) bgcs_widget_create(void)
{
    return new (std::nothrow) Example::Widget();
}

API_INTERNAL(void) bgcs_widget_destroy(void* widget)
{
    delete static_cast<Example::Widget*>(widget);
}

API_INTERNAL(int) bgcs_widget_sum(void* widget, const int* values, size_t count, int* result)
{
    if (widget == nullptr || result == nullptr || (values == nullptr && count != 0))
        return -1;

    try
    {
        *result = static_cast<Example::Widget*>(widget)->Sum(values, count);
        return 0;
    }
    catch (...)
    {
        return -2;
    }
}
