namespace A2A.UnitTests.Server;

public sealed class A2ARequestContextTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";

    [Fact]
    public async Task UsageExample_ChecksRequestedAndActivatedRegistrations()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var context = await builder.Build().NegotiateExtensionsAsync([extension.Uri]);

        Assert.True(context.WasRequested(extension));
        Assert.True(context.IsActivated(extension));
        Assert.Contains(extension.Uri, context.RequestedExtensions);
        Assert.Contains(extension.Uri, context.ActivatedExtensions);
    }

    [Fact]
    public async Task DeclinedOptionalExtension_IsRequestedButNotActivated()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var calls = 0;
        var context = await builder.Build().NegotiateExtensionsAsync([extension.Uri], canActivate: (_, _) =>
        {
            calls++;
            return ValueTask.FromResult(false);
        });

        Assert.True(context.WasRequested(extension));
        Assert.False(context.IsActivated(extension));
        Assert.True(context.WasRequested(extension));
        Assert.False(context.IsActivated(extension));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnrequestedExtension_ReturnsFalseFromBothHelpers()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var context = await builder.Build().NegotiateExtensionsAsync([]);

        Assert.False(context.WasRequested(extension));
        Assert.False(context.IsActivated(extension));
    }

    [Fact]
    public async Task Identifiers_PreserveCaseAndVersionDistinctions()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var upperCase = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri.ToUpperInvariant() });
        var nextVersion = builder.RegisterExtension(new AgentExtension { Uri = "https://example.org/extensions/debate/v2" });
        var context = await builder.Build().NegotiateExtensionsAsync([extension.Uri]);

        Assert.True(context.WasRequested(extension));
        Assert.True(context.IsActivated(extension));
        Assert.False(context.WasRequested(upperCase));
        Assert.False(context.IsActivated(upperCase));
        Assert.False(context.WasRequested(nextVersion));
        Assert.False(context.IsActivated(nextVersion));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignRegistration_IsRejectedEvenWithTheSameUri(bool sameUri)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var foreignBuilder = new A2ACustomOperationRegistryBuilder();
        var foreign = foreignBuilder.RegisterExtension(new AgentExtension
        {
            Uri = sameUri ? ExtensionUri : "https://example.org/extensions/other/v1",
        });
        var context = await builder.Build().NegotiateExtensionsAsync([extension.Uri, foreign.Uri]);

        Assert.Contains(foreign.Uri, context.RequestedExtensions);
        Assert.Throws<InvalidOperationException>(() => context.WasRequested(foreign));
        Assert.Throws<InvalidOperationException>(() => context.IsActivated(foreign));
        Assert.True(context.IsActivated(extension));
    }

    [Fact]
    public async Task NullRegistration_IsRejectedWithTheArgumentName()
    {
        var context = await new A2ACustomOperationRegistryBuilder().Build().NegotiateExtensionsAsync([]);

        Assert.Equal("registration", Assert.Throws<ArgumentNullException>(() => context.WasRequested(null!)).ParamName);
        Assert.Equal("registration", Assert.Throws<ArgumentNullException>(() => context.IsActivated(null!)).ParamName);
    }

    [Fact]
    public async Task Helpers_KeepTheNegotiatedSnapshotWithoutReevaluatingAvailability()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var registry = builder.Build();
        var available = true;
        var calls = 0;
        Func<A2AExtensionRegistration, CancellationToken, ValueTask<bool>> policy = (_, _) =>
        {
            calls++;
            return ValueTask.FromResult(available);
        };
        var requested = new List<string> { extension.Uri };
        var first = await registry.NegotiateExtensionsAsync(requested, policy);
        requested.Clear();
        available = false;
        var second = await registry.NegotiateExtensionsAsync([extension.Uri], policy);
        var firstRequested = first.RequestedExtensions;
        var firstActivated = first.ActivatedExtensions;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() =>
        {
            Assert.True(first.WasRequested(extension));
            Assert.True(first.IsActivated(extension));
            Assert.True(second.WasRequested(extension));
            Assert.False(second.IsActivated(extension));
        })));

        Assert.Equal(2, calls);
        Assert.Same(firstRequested, first.RequestedExtensions);
        Assert.Same(firstActivated, first.ActivatedExtensions);
        Assert.Single(first.RequestedExtensions);
        Assert.Single(first.ActivatedExtensions);
        Assert.Empty(second.ActivatedExtensions);
    }
}
