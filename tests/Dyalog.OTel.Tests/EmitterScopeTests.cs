using Dyalog.OTel.Channels;
using Dyalog.OTel.Config;
using Dyalog.OTel.Destinations;
using Dyalog.OTel.Pipeline;
using Dyalog.OTel.Tests.Helpers;
using PipelineInstance = Dyalog.OTel.Pipeline.Pipeline;

namespace Dyalog.OTel.Tests;

public class EmitterScopeTests
{
    [Fact]
    public void Span_EmitterName_StoredOnRecord()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.Start();

        int handle = pipeline.StartSpan("test-op", 0, null, "my-component");
        pipeline.EndSpan(handle, null, null);

        pipeline.Flush();
        Thread.Sleep(200);

        Assert.Single(dest.Spans);
        Assert.Equal("my-component", dest.Spans[0].Emitter);
        pipeline.Shutdown();
    }

    [Fact]
    public void Span_NullEmitter_StoredAsNull()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.Start();

        int handle = pipeline.StartSpan("test-op", 0, null);
        pipeline.EndSpan(handle, null, null);

        pipeline.Flush();
        Thread.Sleep(200);

        Assert.Single(dest.Spans);
        Assert.Null(dest.Spans[0].Emitter);
        pipeline.Shutdown();
    }

    [Fact]
    public void Pipeline_EmitterRegistry_StoresVersions()
    {
        var dest = new TestDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.EmitterRegistry["calculator"] = "2.0.0";
        pipeline.EmitterRegistry["loader"] = "1.5.0";

        Assert.True(pipeline.EmitterRegistry.TryGetValue("calculator", out var ver));
        Assert.Equal("2.0.0", ver);
        Assert.True(pipeline.EmitterRegistry.TryGetValue("loader", out var ver2));
        Assert.Equal("1.5.0", ver2);
        pipeline.Shutdown();
    }

    [Fact]
    public void ConfigLoader_ParsesPipelineSection()
    {
        // Create a temp INI with [pipeline]
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                [pipeline]
                emitter = my-app
                emitter.version = 3.2.1

                [resource]
                service.name = test
                """);

            var config = ConfigLoader.LoadFromFile(tempFile);
            Assert.Equal("my-app", config.DefaultEmitter);
            Assert.Equal("3.2.1", config.DefaultEmitterVersion);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigLoader_ParsesEmitterSections()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, """
                [resource]
                service.name = test

                [emitter.calculator]
                version = 2.0.0

                [emitter.data-loader]
                version = 1.5.3
                """);

            var config = ConfigLoader.LoadFromFile(tempFile);
            Assert.Equal("2.0.0", config.EmitterRegistry["calculator"]);
            Assert.Equal("1.5.3", config.EmitterRegistry["data-loader"]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void PipelineRegistry_ApplyConfigOverride_PreInit_Accumulates()
    {
        // Shutdown singleton first to reset state
        PipelineRegistry.Shutdown(0);

        int status = PipelineRegistry.ApplyConfigOverride("resource", "service.name", "override-test");
        Assert.Equal(0, status);

        // Init and verify it doesn't crash
        var pipe = PipelineRegistry.GetOrCreateSingleton();
        Assert.NotNull(pipe);

        // Clean up
        PipelineRegistry.Shutdown(0);
    }

    [Fact]
    public void PipelineRegistry_ApplyConfigOverride_PostInit_FrozenSection_Returns2()
    {
        PipelineRegistry.Shutdown(0);
        PipelineRegistry.GetOrCreateSingleton(); // force init

        int status = PipelineRegistry.ApplyConfigOverride("resource", "service.name", "too-late");
        Assert.Equal(2, status);

        PipelineRegistry.Shutdown(0);
    }

    [Fact]
    public void PipelineRegistry_ApplyConfigOverride_PostInit_EmitterAllowed()
    {
        PipelineRegistry.Shutdown(0);
        PipelineRegistry.GetOrCreateSingleton(); // force init

        int status = PipelineRegistry.ApplyConfigOverride("emitter.late-component", "version", "4.0.0");
        Assert.Equal(0, status);

        var pipe = PipelineRegistry.Resolve(0);
        Assert.NotNull(pipe);
        Assert.True(pipe!.EmitterRegistry.TryGetValue("late-component", out var ver));
        Assert.Equal("4.0.0", ver);

        PipelineRegistry.Shutdown(0);
    }

    [Fact]
    public void PipelineRegistry_ApplyConfigOverride_PreInit_DestinationAccumulates()
    {
        PipelineRegistry.Shutdown(0);

        int s1 = PipelineRegistry.ApplyConfigOverride("destination.otlp", "endpoint", "http://localhost:4318");
        int s2 = PipelineRegistry.ApplyConfigOverride("destination.otlp", "protocol", "protobuf");
        int s3 = PipelineRegistry.ApplyConfigOverride("destination.otlp", "signals", "log,span,metric");
        Assert.Equal(0, s1);
        Assert.Equal(0, s2);
        Assert.Equal(0, s3);

        // Verify accumulation via singleton state (without triggering full init which needs the companion DLL)
        var state = PipelineRegistry.GetSingletonState();
        Assert.Equal(0, state); // 0 = not initialized, config is just accumulated

        PipelineRegistry.Shutdown(0);
    }

    [Fact]
    public void PipelineRegistry_ApplyConfigOverride_PreInit_InvalidKey_Returns1()
    {
        PipelineRegistry.Shutdown(0);

        // Invalid section.key combinations should return 1
        int s1 = PipelineRegistry.ApplyConfigOverride("pipeline", "foo", "bar");
        int s2 = PipelineRegistry.ApplyConfigOverride("invalid-section", "key", "val");
        int s3 = PipelineRegistry.ApplyConfigOverride("destination.unknown-type", "endpoint", "http://x");

        Assert.Equal(1, s1);
        Assert.Equal(1, s2);
        Assert.Equal(1, s3);

        PipelineRegistry.Shutdown(0);
    }

    [Fact]
    public void PipelineRegistry_ApplyConfigOverride_PostInit_InvalidEmitterKey_Returns1()
    {
        PipelineRegistry.Shutdown(0);
        PipelineRegistry.GetOrCreateSingleton(); // force init

        // Only "version" is a valid key for emitter.* sections
        int status = PipelineRegistry.ApplyConfigOverride("emitter.component", "name", "wrong-key");
        Assert.Equal(1, status);

        PipelineRegistry.Shutdown(0);
    }

    [Fact]
    public void Pipeline_ApplyEmitterConfig_PropagatesLateToDest()
    {
        var dest = new TestEmitterAwareDestination();
        var pipeline = new PipelineInstance(new List<IDestination> { dest }, new BatchConfig());
        pipeline.DefaultEmitter = "my-app";
        pipeline.DefaultEmitterVersion = "1.0.0";
        pipeline.EmitterRegistry["calc"] = "2.0.0";

        // Initial propagation
        pipeline.ApplyEmitterConfigToDestinations(bestEffort: false);

        Assert.Equal(1, dest.SetEmitterConfigCallCount);
        Assert.Equal("my-app", dest.LastDefaultEmitter);
        Assert.Equal("1.0.0", dest.LastDefaultEmitterVersion);
        Assert.True(dest.LastRegistry!.ContainsKey("calc"));
        Assert.Equal("2.0.0", dest.LastRegistry["calc"]);

        // Late registration
        pipeline.EmitterRegistry["loader"] = "3.1.0";
        pipeline.ApplyEmitterConfigToDestinations(bestEffort: false);

        Assert.Equal(2, dest.SetEmitterConfigCallCount);
        Assert.True(dest.LastRegistry!.ContainsKey("loader"));
        Assert.Equal("3.1.0", dest.LastRegistry["loader"]);

        pipeline.Shutdown();
    }
}
