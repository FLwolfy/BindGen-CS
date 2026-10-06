# BGCS.Runtime 0.1.0

## Architecture and extensibility

- Introduce the independently packaged runtime support library used by generated C# interop code.

## Parser portability

- Keep the runtime package separate from the generator and its native parser dependencies.

## Verification and packaging

- Publish the original `BGCS.Runtime` package for .NET 9.
- This archive contains the package retrieved from NuGet; it does not rebuild the historical source.

## Requirements and scope

This is a runtime-only release. It does not contain the C# generator, C++ bridge generator, or command-line tool.

## Release provenance

Source: [`runtime-v0.1.0`](https://github.com/FLwolfy/BindGen-CS/tree/f38c44fd460ab825933dab625764021792dfe7dd) (`f38c44fd460ab825933dab625764021792dfe7dd`).

Release assets are copies of the original NuGet packages, plus `release-archive.json` and `SHA256SUMS`. Historical symbol packages and build attestations are not reconstructed. This archive does not assert that the original source passed today's CI.
