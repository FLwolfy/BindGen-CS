#include "aggregates.h"
#include <inttypes.h>
#include <stdio.h>

static AggregatePair mutate_pair(AggregatePair value) { value.a += 8; value.b *= 3; return value; }

int main(void)
{
    AggregateSmall small = bgcs_aggregate_transform_small((AggregateSmall){1, 2, 3});
    AggregatePair pair = bgcs_aggregate_transform_pair((AggregatePair){17, 1.25f});
    AggregateMixed mixed = bgcs_aggregate_transform_mixed((AggregateMixed){3.5, 19});
    AggregateLarge large = bgcs_aggregate_transform_large((AggregateLarge){UINT64_C(0xFEDCBA9876543210), 5, 2.25, 7});
    AggregateNumber number = bgcs_aggregate_transform_number((AggregateNumber){.bits = UINT64_C(0xFEDCBA9876543210)});
    AggregateNested nested = bgcs_aggregate_transform_nested((AggregateNested){3, {19, 2.5f}, 4.25});
    AggregateFloats floats = bgcs_aggregate_transform_floats((AggregateFloats){1.25f, 2.5f, 3.75f, 5});
    AggregateDoubles doubles = bgcs_aggregate_transform_doubles((AggregateDoubles){1.25, 2.5});
    AggregateArray array = bgcs_aggregate_transform_array((AggregateArray){{7, 11, 13}, 2});
    AggregatePacked packed = bgcs_aggregate_transform_packed((AggregatePacked){2, 4.25});
    AggregatePair callback = bgcs_aggregate_invoke_callback((AggregatePair){7, 1.5f}, mutate_pair);
    AggregateFloatOne one_float = bgcs_aggregate_transform_float_one((AggregateFloatOne){1.5f});
    AggregateDoubleOne one_double = bgcs_aggregate_transform_double_one((AggregateDoubleOne){1.25});
    if (small.a != 4 || small.b != 7 || small.c != 10 || pair.a != 21 || pair.b != 2.5f
        || mixed.a != 10.5 || mixed.b != 28 || large.a != (UINT64_C(0xFEDCBA9876543210) ^ UINT64_C(0x8000000000000001))
        || large.b != 14 || large.c != 4.5 || large.d != 20 || number.bits != large.a
        || nested.prefix != 8 || nested.nested.a != 23 || nested.nested.b != 5 || nested.tail != 8.5
        || floats.a != 2.5f || floats.b != 7.5f || floats.c != 15 || floats.d != 25
        || doubles.a != 5 || doubles.b != 12.5 || array.values[0] != 14 || array.values[1] != 33
        || array.values[2] != 52 || array.tag != 9 || packed.prefix != 9 || packed.value != 17
        || callback.a != 24 || callback.b != 9 || one_float.value != 6 || one_double.value != 10) return 1;
    printf("{\"success\":true,\"checks\":13,\"signature\":\"%u,%u,%u;%d,%g;%g,%d;", small.a, small.b, small.c, pair.a, pair.b, mixed.a, mixed.b);
    printf("%" PRIu64 ",%d,%g,%u;%" PRIu64 ";%u,%d,%g,%g;", large.a, large.b, large.c, large.d, number.bits, nested.prefix, nested.nested.a, nested.nested.b, nested.tail);
    printf("%g,%g,%g,%g;%g,%g;%u,%u,%u,%u;%u,%g;%d,%g;%g;%g\"}\n", floats.a, floats.b, floats.c, floats.d,
        doubles.a, doubles.b, array.values[0], array.values[1], array.values[2], array.tag, packed.prefix, packed.value, callback.a, callback.b, one_float.value, one_double.value);
    return fflush(stdout) == 0 ? 0 : 1;
}
