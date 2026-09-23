using BGCS.Examples.NativeShim;

unsafe
{
    void* widget = WidgetNative.BgcsWidgetCreate();
    if (widget == null)
        throw new InvalidOperationException("Unable to allocate the native widget.");

    try
    {
        int[] values = [10, 20, 12];
        fixed (int* valuesPointer = values)
        {
            int result = 0;
            int status = WidgetNative.BgcsWidgetSum(widget, valuesPointer, (ulong)values.Length, &result);
            if (status != 0)
                throw new InvalidOperationException($"Native call failed with status {status}.");
            Console.WriteLine(result);
        }
    }
    finally
    {
        WidgetNative.BgcsWidgetDestroy(widget);
    }
}
