using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace A2A.UnitTests.Server;

public sealed class A2ACustomOperationContextTests
{
    private const string ExtensionUri = "https://example.test/extensions/context/v1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtensionActivation_ReturnsNullWithoutANegotiatedSnapshot(bool hasHostFeature)
    {
        var context = hasHostFeature
            ? new A2ACustomOperationContext(features: [new TestFeature("host")])
            : new A2ACustomOperationContext();

        Assert.Null(context.ExtensionActivation);
        Assert.False(context.TryGetFeature<A2ARequestContext>(out var activation));
        Assert.Null(activation);
    }

    [Fact]
    public async Task ExtensionActivation_ReturnsTheExistingFeatureInstance()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var activation = await builder.Build().NegotiateExtensionsAsync([extension.Uri]);
        var context = new A2ACustomOperationContext(features: [new TestFeature("host"), activation]);

        var observed = Assert.IsType<A2ARequestContext>(context.ExtensionActivation);
        Assert.Same(activation, observed);
        Assert.Same(activation, context.GetRequiredFeature<A2ARequestContext>());
        Assert.True(context.TryGetFeature<A2ARequestContext>(out var feature));
        Assert.Same(activation, feature);
        Assert.Same(activation, context.ExtensionActivation);
        Assert.True(observed.IsActivated(extension));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtensionActivation_PreservesAnEmptySnapshot(bool requestedButUnavailable)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var activation = await builder.Build().NegotiateExtensionsAsync(
            requestedButUnavailable ? [extension.Uri] : [],
            canActivate: static (_, _) => ValueTask.FromResult(false));
        var context = new A2ACustomOperationContext(features: [activation]);

        var observed = Assert.IsType<A2ARequestContext>(context.ExtensionActivation);
        Assert.Same(activation, observed);
        Assert.Empty(observed.ActivatedExtensions);
        Assert.Equal(requestedButUnavailable, observed.WasRequested(extension));
        Assert.False(observed.IsActivated(extension));
        Assert.True(context.TryGetFeature<A2ARequestContext>(out var feature));
        Assert.Same(activation, feature);
    }

    [Fact]
    public void TryGetFeature_MatchesConcreteBaseAndInterfaceTypes()
    {
        var feature = new DerivedTestFeature("host");
        var context = new A2ACustomOperationContext(features: [new object(), feature]);

        Assert.True(context.TryGetFeature<DerivedTestFeature>(out var concrete));
        Assert.True(context.TryGetFeature<TestFeature>(out var baseFeature));
        Assert.True(context.TryGetFeature<ITestFeature>(out var interfaceFeature));
        Assert.Same(feature, concrete);
        Assert.Same(feature, baseFeature);
        Assert.Same(feature, interfaceFeature);
        Assert.Same(feature, context.GetRequiredFeature<ITestFeature>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryGetFeature_ClearsTheOutputWhenNoAssignableFeatureExists(bool hasUnrelatedFeature)
    {
        var context = hasUnrelatedFeature
            ? new A2ACustomOperationContext(features: [new object()])
            : new A2ACustomOperationContext();
        TestFeature? feature = new("previous value");

        Assert.False(context.TryGetFeature(out feature));
        Assert.Null(feature);
    }

    [Fact]
    public void TryGetFeature_AllowsNonNullAccessWhenItReturnsTrue()
    {
        var context = new A2ACustomOperationContext(features: [new TestFeature("host")]);

        if (context.TryGetFeature<TestFeature>(out var feature))
        {
            Assert.Equal("host", feature.Value);
        }
        else
        {
            Assert.Fail("The host feature should be available.");
        }
    }

    [Fact]
    public void GetRequiredFeature_KeepsTheMissingFeatureException()
    {
        var context = new A2ACustomOperationContext();

        Assert.False(context.TryGetFeature<TestFeature>(out _));
        var error = Assert.Throws<InvalidOperationException>(() => context.GetRequiredFeature<TestFeature>());

        Assert.Equal($"Custom operation feature '{typeof(TestFeature).FullName}' is not available.", error.Message);
        Assert.Null(context.ExtensionActivation);
    }

    [Fact]
    public void Context_PreservesHostFeaturesAndRequestServices()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services, TraceIdentifier = "request-42" };
        var context = new A2ACustomOperationContext(services, http);

        Assert.Same(services, context.RequestServices);
        if (context.TryGetFeature<HttpContext>(out var feature))
        {
            Assert.Same(http, feature);
            Assert.Equal("request-42", feature.TraceIdentifier);
        }
        else
        {
            Assert.Fail("The HTTP context should be available through its base type.");
        }

        Assert.Null(context.ExtensionActivation);
    }

    [Fact]
    public async Task Accessors_KeepRequestSnapshotsIsolatedWithoutReevaluatingAvailability()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var registry = builder.Build();
        var available = true;
        var policyCalls = 0;
        Func<A2AExtensionRegistration, CancellationToken, ValueTask<bool>> policy = (_, _) =>
        {
            policyCalls++;
            return ValueTask.FromResult(available);
        };
        var firstActivation = await registry.NegotiateExtensionsAsync([extension.Uri], policy);
        available = false;
        var secondActivation = await registry.NegotiateExtensionsAsync([extension.Uri], policy);
        var firstFeature = new TestFeature("first");
        var secondFeature = new TestFeature("second");
        var first = new A2ACustomOperationContext(features: [firstActivation, firstFeature]);
        var second = new A2ACustomOperationContext(features: [secondActivation, secondFeature]);
        var withoutNegotiation = new A2ACustomOperationContext();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            var firstObservedActivation = Assert.IsType<A2ARequestContext>(first.ExtensionActivation);
            var secondObservedActivation = Assert.IsType<A2ARequestContext>(second.ExtensionActivation);
            Assert.Same(firstActivation, firstObservedActivation);
            Assert.Same(secondActivation, secondObservedActivation);
            Assert.True(firstObservedActivation.IsActivated(extension));
            Assert.False(secondObservedActivation.IsActivated(extension));
            Assert.True(first.TryGetFeature<TestFeature>(out var firstObserved));
            Assert.True(second.TryGetFeature<TestFeature>(out var secondObserved));
            Assert.Same(firstFeature, firstObserved);
            Assert.Same(secondFeature, secondObserved);
            Assert.Null(withoutNegotiation.ExtensionActivation);
        })));

        Assert.Equal(2, policyCalls);
        Assert.NotSame(firstActivation, secondActivation);
    }

    private interface ITestFeature
    {
        string Value { get; }
    }

    private class TestFeature(string value)
    {
        public string Value { get; } = value;
    }

    private sealed class DerivedTestFeature(string value) : TestFeature(value), ITestFeature;
}
