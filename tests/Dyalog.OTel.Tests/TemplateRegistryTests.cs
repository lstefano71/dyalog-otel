using Dyalog.OTel.Templates;

public class TemplateRegistryTests
{
    [Fact]
    public void CascadeDelete_RemovesAllDescendants()
    {
        var registry = new TemplateRegistry();
        registry.Create("root", new[] { KeyValuePair.Create<string, object>("env", "prod") });
        registry.Derive("child1", "root", new[] { KeyValuePair.Create<string, object>("svc", "api") });
        registry.Derive("child2", "root", new[] { KeyValuePair.Create<string, object>("svc", "web") });
        registry.Derive("grandchild", "child1", new[] { KeyValuePair.Create<string, object>("ver", "1") });

        registry.Delete("root");

        Assert.Null(registry.TryGet("root"));
        Assert.Null(registry.TryGet("child1"));
        Assert.Null(registry.TryGet("child2"));
        Assert.Null(registry.TryGet("grandchild"));
    }

    [Fact]
    public void CascadeDelete_DeeplyNested_NoStackOverflow()
    {
        var registry = new TemplateRegistry();
        string prev = "root";
        registry.Create(prev, new[] { KeyValuePair.Create<string, object>("k", "v") });

        // Build a chain 200 deep
        for (int i = 0; i < 200; i++)
        {
            string name = $"child-{i}";
            registry.Derive(name, prev, new[] { KeyValuePair.Create<string, object>($"k{i}", $"v{i}") });
            prev = name;
        }

        // Should not throw StackOverflowException
        registry.Delete("root");
        Assert.Null(registry.TryGet("root"));
        Assert.Null(registry.TryGet("child-199"));
    }

    [Fact]
    public void Delete_NonExistent_DoesNotThrow()
    {
        var registry = new TemplateRegistry();
        registry.Delete("nonexistent"); // should be a no-op
    }
}
