using Compass.Agents.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Agents.Contracts.Tests.Hosting;

public sealed class Agent365TracingTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Is_off_by_default_and_needs_nothing()
    {
        var configuration = Config();

        Assert.False(Agent365Tracing.IsEnabled(configuration));
        new ServiceCollection().ValidateAgent365Tracing(configuration);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("yes")]
    public void Anything_but_true_leaves_it_off_even_with_settings_missing(string value)
    {
        new ServiceCollection().ValidateAgent365Tracing(Config((Agent365Tracing.EnabledVariable, value)));
    }

    [Fact]
    public void Turned_on_without_its_settings_fails_fast_naming_every_missing_setting()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().ValidateAgent365Tracing(Config((Agent365Tracing.EnabledVariable, "TRUE"))));

        Assert.All(Agent365Tracing.RequiredVariables, name => Assert.Contains(name, ex.Message));
    }

    [Fact]
    public void The_error_names_settings_but_never_echoes_a_value()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().ValidateAgent365Tracing(Config(
            (Agent365Tracing.EnabledVariable, "true"),
            (Agent365Tracing.HostingEnvironmentVariable, "hosted-secret-value"),
            (Agent365Tracing.TenantIdVariable, "tenant-secret-value"))));

        Assert.Contains(Agent365Tracing.InstanceClientIdVariable, ex.Message);
        Assert.DoesNotContain("secret-value", ex.Message);
        Assert.DoesNotContain(Agent365Tracing.TenantIdVariable, ex.Message.Split("missing:")[1]);
    }

    [Fact]
    public void Turned_on_with_everything_present_starts()
    {
        new ServiceCollection().ValidateAgent365Tracing(Config(
            (Agent365Tracing.EnabledVariable, "true"),
            (Agent365Tracing.HostingEnvironmentVariable, "x"),
            (Agent365Tracing.InstanceClientIdVariable, "x"),
            (Agent365Tracing.TenantIdVariable, "x")));
    }
}
