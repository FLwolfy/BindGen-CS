#include "widget_c.h"
#include "library.hpp"

#include <new>

WIDGET_EXPORT void* bgcs_widget_create(void)
{
    return new (std::nothrow) Example::Widget();
}

WIDGET_EXPORT void bgcs_widget_destroy(void* widget)
{
    delete static_cast<Example::Widget*>(widget);
}

WIDGET_EXPORT int bgcs_widget_sum(void* widget, const int* values, size_t count, int* result)
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
