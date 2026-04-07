typedef void (*log_cb)(int level);
typedef int (*sum_cb)(int left, int right);
typedef void (*tick_cb)(void);

void set_log_callback(log_cb cb);
void set_sum_callback(sum_cb cb);
void set_tick_callback(tick_cb cb);
