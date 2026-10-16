#pragma once
#include <stdint.h>

#if defined(_WIN32)
#define BGCS_AGGREGATE_EXPORT __declspec(dllexport)
#else
#define BGCS_AGGREGATE_EXPORT __attribute__((visibility("default")))
#endif

typedef struct AggregateSmall { uint8_t a, b, c; } AggregateSmall;
typedef struct AggregatePair { int32_t a; float b; } AggregatePair;
typedef struct AggregateMixed { double a; int32_t b; } AggregateMixed;
typedef struct AggregateLarge { uint64_t a; int32_t b; double c; uint8_t d; } AggregateLarge;
typedef union AggregateNumber { double real; uint64_t bits; } AggregateNumber;
typedef struct AggregateNested { uint8_t prefix; AggregatePair nested; double tail; } AggregateNested;
typedef struct AggregateFloats { float a, b, c, d; } AggregateFloats;
typedef struct AggregateDoubles { double a, b; } AggregateDoubles;
typedef struct AggregateArray { uint16_t values[3]; uint8_t tag; } AggregateArray;
typedef struct AggregateFloatOne { float value; } AggregateFloatOne;
typedef struct AggregateDoubleOne { double value; } AggregateDoubleOne;
#pragma pack(push, 1)
typedef struct AggregatePacked { uint8_t prefix; double value; } AggregatePacked;
#pragma pack(pop)

BGCS_AGGREGATE_EXPORT AggregateSmall bgcs_aggregate_transform_small(AggregateSmall value);
BGCS_AGGREGATE_EXPORT AggregatePair bgcs_aggregate_transform_pair(AggregatePair value);
BGCS_AGGREGATE_EXPORT AggregateMixed bgcs_aggregate_transform_mixed(AggregateMixed value);
BGCS_AGGREGATE_EXPORT AggregateLarge bgcs_aggregate_transform_large(AggregateLarge value);
BGCS_AGGREGATE_EXPORT AggregateNumber bgcs_aggregate_transform_number(AggregateNumber value);
BGCS_AGGREGATE_EXPORT AggregateNested bgcs_aggregate_transform_nested(AggregateNested value);
BGCS_AGGREGATE_EXPORT AggregateFloats bgcs_aggregate_transform_floats(AggregateFloats value);
BGCS_AGGREGATE_EXPORT AggregateDoubles bgcs_aggregate_transform_doubles(AggregateDoubles value);
BGCS_AGGREGATE_EXPORT AggregateArray bgcs_aggregate_transform_array(AggregateArray value);
BGCS_AGGREGATE_EXPORT AggregatePacked bgcs_aggregate_transform_packed(AggregatePacked value);
typedef AggregatePair (*AggregatePairCallback)(AggregatePair value);
BGCS_AGGREGATE_EXPORT AggregatePair bgcs_aggregate_invoke_callback(AggregatePair value, AggregatePairCallback callback);
BGCS_AGGREGATE_EXPORT AggregateFloatOne bgcs_aggregate_transform_float_one(AggregateFloatOne value);
BGCS_AGGREGATE_EXPORT AggregateDoubleOne bgcs_aggregate_transform_double_one(AggregateDoubleOne value);
