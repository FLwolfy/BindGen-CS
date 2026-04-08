#define HEADER_FLAG 11

enum header_mode
{
    HEADER_MODE_OFF = 0,
    HEADER_MODE_ON = 1
};

int header_eval(enum header_mode mode);
