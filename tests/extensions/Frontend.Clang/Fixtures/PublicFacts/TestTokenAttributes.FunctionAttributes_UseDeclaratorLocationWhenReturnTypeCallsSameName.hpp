
int factory();
[[nodiscard]] decltype(factory()) factory(int value) __attribute__((annotate("tail")));
