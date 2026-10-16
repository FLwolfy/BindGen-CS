using System;
using BGCS.Examples.NativeShim;

using WidgetApi api = WidgetApi.Load();
unsafe
{
    void* widget = api.Raw.CreateWidget();
    if (widget == null)
        throw new InvalidOperationException("Unable to allocate the native widget.");

    try
    {
        int[] values = [10, 20, 12];
        fixed (int* valuesPointer = values)
        {
            int result = 0;
            int status = api.Raw.Sum(widget, valuesPointer, (uint)values.Length, &result);
            if (status != 0)
                throw new InvalidOperationException($"Native call failed with status {status}.");
            Console.WriteLine(result);
        }
    }
    finally
    {
        api.Raw.DestroyWidget(widget);
    }
}
