# LightProto for OTLP wire serialization

We use [LightProto](https://github.com/dameng324/LightProto) as the protobuf serialization library for OTLP export, instead of Google.Protobuf or protobuf-net.

The DWA extension compiles to a NativeAOT DLL. Google.Protobuf relies heavily on reflection and runtime code generation, which conflicts with NativeAOT trimming. protobuf-net v3 is moving toward AOT support but is not fully there — its runtime model still uses reflection paths that produce trimming warnings and can fail at runtime.

LightProto is source-generator-only: serializers and parsers are emitted at compile time, no reflection, no IL generation, no third-party dependencies. It targets netstandard2.0 through net10.0, supports `ReadOnlySpan<byte>` and `IBufferWriter<byte>` serialization, and provides `lightproto-gen` to generate C# classes from the official OTLP `.proto` definitions. Benchmarks show 20-50% better throughput than protobuf-net.

JSON serialization via `System.Text.Json` is provided as a parallel destination for debugging, OTLP/HTTP+JSON collectors, and file output. The `IDestination` interface makes the two serialization paths independent.

## Considered options

- **Google.Protobuf**: Industry standard, but reflection-heavy. NativeAOT trimming breaks it without extensive manual annotation. Ruled out.
- **protobuf-net v3**: Familiar API, actively working toward AOT. But the runtime model still has reflection paths that produce trimming warnings on .NET 10 NativeAOT. Too risky for a library that must ship as a trimmed native DLL.
- **Hand-rolled protobuf encoding**: Zero dependencies, but maintaining varint/wire-format code by hand for the full OTLP schema is error-prone and hard to update when the OTLP spec evolves.
- **JSON-only (no protobuf)**: Simplest, but ~30-40% wire overhead. Acceptable for low-volume, but we want the efficient path available from day one.
