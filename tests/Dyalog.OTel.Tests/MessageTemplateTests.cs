using Dyalog.OTel.MessageTemplates;
using Dyalog.OTel.Channels;

namespace Dyalog.OTel.Tests;

public class MessageTemplateTests
{
    [Fact]
    public void Render_WithAttributes_ProducesSameOutput()
    {
        var tpl = MessageTemplate.Parse("Order {OrderId} total {Amount}");
        var attrs = new OTelAttribute[]
        {
            new("OrderId", 42),
            new("Amount", 99.5)
        };
        string result = tpl.Render(attrs);
        Assert.Equal("Order 42 total 99.5", result);
    }

    [Fact]
    public void Render_WithObjectSpan_ProducesSameOutput()
    {
        var tpl = MessageTemplate.Parse("Order {OrderId} total {Amount}");
        var values = new object[] { 42, 99.5 };
        string result = tpl.Render(values);
        Assert.Equal("Order 42 total 99.5", result);
    }

    [Fact]
    public void Render_NoPlaceholders_ReturnsOriginal()
    {
        var tpl = MessageTemplate.Parse("No placeholders here");
        var attrs = new OTelAttribute[] { new("key", "val") };
        string result = tpl.Render(attrs);
        Assert.Equal("No placeholders here", result);
    }

    [Fact]
    public void Render_BothOverloads_ProduceIdenticalOutput()
    {
        var tpl = MessageTemplate.Parse("User {Name} logged in from {IP}");
        var attrs = new OTelAttribute[]
        {
            new("Name", "Alice"),
            new("IP", "192.168.1.1")
        };
        var values = new object[] { "Alice", "192.168.1.1" };

        Assert.Equal(tpl.Render(values), tpl.Render(attrs));
    }

    [Fact]
    public void Render_WithNullValue_RendersEmptyString()
    {
        var tpl = MessageTemplate.Parse("Value is {Val}");
        var attrs = new OTelAttribute[]
        {
            new("Val", null!)
        };
        string result = tpl.Render(attrs);
        Assert.Equal("Value is ", result);
    }
}
