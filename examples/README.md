# Examples

Every generator example emits source and requirements. Consumer projects and their compiler commands demonstrate the consumer's responsibility, not a BGCS build feature.

| Start | Extension and integration |
| --- | --- |
| [C quick start](QuickStart/README.md) | [Inline interface](InlineExtension/README.md) |
| [C# generation library](EmbeddedGeneration/README.md) | [Custom target](CustomTargetPlugin/README.md) |
| [Native shim](NativeShim/README.md) | [Custom lowering](LoweringPlugin/README.md) |
| [Custom allocator](CustomAllocator/README.md) | [External function table](CustomFunctionTable/README.md) |

Requires .NET 10 / C# 14. Each example states additional inputs needed by its own native consumer. Repository project references build the sample generation tool; installed usage references the matching BGCS products.
