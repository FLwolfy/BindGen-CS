
template<typename T>
struct TestT {
};

struct Test{
    [[cppast]] TestT<int> channels;
};