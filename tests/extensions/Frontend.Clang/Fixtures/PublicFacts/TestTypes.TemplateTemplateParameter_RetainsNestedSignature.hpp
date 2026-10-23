
template <template <typename Item> class Container, typename Value>
struct GenericHolder { Container<Value>* value; };
template <typename T> struct Box { T value; };
GenericHolder<Box, int>* holder;
