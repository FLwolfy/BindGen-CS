typedef void (*my_delegate)(int value);
typedef void (*keep_delegate)(int value);

void set_keep_delegate(keep_delegate cb);
