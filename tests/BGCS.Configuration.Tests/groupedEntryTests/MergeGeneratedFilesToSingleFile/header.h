struct merge_type
{
    int value;
};

enum merge_mode
{
    MERGE_MODE_OFF = 0,
    MERGE_MODE_ON = 1
};

int sample_add(int a, int b);
void merge_set_mode(struct merge_type* data, enum merge_mode mode);
