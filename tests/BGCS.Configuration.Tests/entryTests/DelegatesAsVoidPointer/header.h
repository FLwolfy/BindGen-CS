typedef int (*math_cb)(int value);
typedef void (*notify_cb)(void);
typedef void (*mix_cb)(const char* name, int value);

int apply_cb(math_cb cb, int value);
void set_notify(notify_cb cb);
void dispatch_mix(mix_cb cb, const char* name, int value);
