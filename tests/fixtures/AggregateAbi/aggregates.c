#include "aggregates.h"

AggregateSmall bgcs_aggregate_transform_small(AggregateSmall value) { value.a += 3; value.b += 5; value.c += 7; return value; }
AggregatePair bgcs_aggregate_transform_pair(AggregatePair value) { value.a += 4; value.b *= 2; return value; }
AggregateMixed bgcs_aggregate_transform_mixed(AggregateMixed value) { value.a *= 3; value.b += 9; return value; }
AggregateLarge bgcs_aggregate_transform_large(AggregateLarge value) { value.a ^= UINT64_C(0x8000000000000001); value.b += 9; value.c *= 2; value.d += 13; return value; }
AggregateNumber bgcs_aggregate_transform_number(AggregateNumber value) { value.bits ^= UINT64_C(0x8000000000000001); return value; }
AggregateNested bgcs_aggregate_transform_nested(AggregateNested value) { value.prefix += 5; value.nested = bgcs_aggregate_transform_pair(value.nested); value.tail *= 2; return value; }
AggregateFloats bgcs_aggregate_transform_floats(AggregateFloats value) { value.a *= 2; value.b *= 3; value.c *= 4; value.d *= 5; return value; }
AggregateDoubles bgcs_aggregate_transform_doubles(AggregateDoubles value) { value.a *= 4; value.b *= 5; return value; }
AggregateArray bgcs_aggregate_transform_array(AggregateArray value) { value.values[0] *= 2; value.values[1] *= 3; value.values[2] *= 4; value.tag += 7; return value; }
AggregatePacked bgcs_aggregate_transform_packed(AggregatePacked value) { value.prefix += 7; value.value *= 4; return value; }
AggregatePair bgcs_aggregate_invoke_callback(AggregatePair value, AggregatePairCallback callback) { value = callback(bgcs_aggregate_transform_pair(value)); value.a += 5; return value; }
AggregateFloatOne bgcs_aggregate_transform_float_one(AggregateFloatOne value) { value.value *= 4; return value; }
AggregateDoubleOne bgcs_aggregate_transform_double_one(AggregateDoubleOne value) { value.value *= 8; return value; }
