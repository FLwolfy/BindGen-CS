#if !defined(FROM_ADDITIONAL_ARGUMENTS)
#error FROM_ADDITIONAL_ARGUMENTS is required
#endif

class Counter
{
public:
    virtual int Add(int a, int b);
};
