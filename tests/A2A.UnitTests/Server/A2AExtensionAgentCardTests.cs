using System.Text.Json;

namespace A2A.UnitTests.Server;

public sealed class A2AExtensionAgentCardTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";

    [Fact]
    public void UsageExample_PreparesAnUnsignedCardWithoutChangingTheInput()
    {
        var registry = CreateRegistry();
        var original = CreateCard();

        var hosted = registry.PrepareAgentCard(original);
        registry.ValidateAgentCard(hosted);

        Assert.Null(original.Capabilities.Extensions);
        var extension = Assert.Single(hosted.Capabilities.Extensions!);
        Assert.Equal(ExtensionUri, extension.Uri);
        Assert.Equal("Debate continuation", extension.Description);
        Assert.True(extension.Required);
        Assert.NotSame(original, hosted);
        Assert.NotSame(original.Capabilities, hosted.Capabilities);
        Assert.Equal(original.Name, hosted.Name);
        Assert.Equal(original.Skills[0].Id, hosted.Skills[0].Id);
    }

    [Fact]
    public void EmptyUnsignedList_IsDerivedFromRegistrations()
    {
        var card = CreateCard();
        card.Capabilities.Extensions = [];
        Assert.Equal(ExtensionUri, Assert.Single(CreateRegistry().PrepareAgentCard(card).Capabilities.Extensions!).Uri);
        Assert.Empty(card.Capabilities.Extensions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitMatchingDeclarations_AreAcceptedWithoutChangingOrderOrValues(bool hasSignatures)
    {
        var registry = CreateRegistry();
        var card = CreateCard();
        card.Capabilities.Extensions = [registry.Extensions[ExtensionUri].ToAgentExtension()];
        if (hasSignatures) card.Signatures = [new AgentCardSignature { Protected = "protected", Signature = "signature" }];
        var originalJson = Serialize(card);

        var hosted = registry.PrepareAgentCard(card);

        Assert.Equal(originalJson, Serialize(card));
        Assert.Equal(originalJson, Serialize(hosted));
        if (hasSignatures) Assert.Equal("signature", Assert.Single(hosted.Signatures!).Signature);
    }

    [Theory]
    [InlineData("uri")]
    [InlineData("description")]
    [InlineData("required")]
    [InlineData("params")]
    public void ConflictingDeclarations_AreRejectedWithoutModifyingTheCard(string field)
    {
        var registry = CreateRegistry();
        var declaration = registry.Extensions[ExtensionUri].ToAgentExtension();
        switch (field)
        {
            case "uri": declaration.Uri += "/other"; break;
            case "description": declaration.Description = "different"; break;
            case "required": declaration.Required = false; break;
            case "params": declaration.Params = Json("{\"version\":2}"); break;
        }
        var card = CreateCard();
        card.Capabilities.Extensions = [declaration];
        var before = Serialize(card);

        Assert.Throws<InvalidOperationException>(() => registry.PrepareAgentCard(card));
        Assert.Equal(before, Serialize(card));
    }

    [Fact]
    public void SignedCardMissingDeclarations_IsRejectedWithResigningGuidance()
    {
        var card = CreateCard();
        card.Signatures = [new AgentCardSignature { Protected = "protected", Signature = "signature" }];
        var before = Serialize(card);

        var error = Assert.Throws<InvalidOperationException>(() => CreateRegistry().PrepareAgentCard(card));

        Assert.Contains("re-sign", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Serialize(card));
        Assert.Single(card.Signatures);
    }

    [Fact]
    public void SignedCardConflictingParameters_IsRejectedWithoutDroppingSignatures()
    {
        var registry = CreateRegistry();
        var card = registry.PrepareAgentCard(CreateCard());
        card.Signatures = [new AgentCardSignature { Protected = "protected", Signature = "signature" }];
        card.Capabilities.Extensions![0].Params = Json("{\"version\":99}");
        var before = Serialize(card);

        Assert.Throws<InvalidOperationException>(() => registry.PrepareAgentCard(card));

        Assert.Equal(before, Serialize(card));
        Assert.Equal("signature", card.Signatures[0].Signature);
    }

    [Fact]
    public void DuplicateDeclarations_AreRejected()
    {
        var registry = CreateRegistry();
        var card = CreateCard();
        card.Capabilities.Extensions = [registry.Extensions[ExtensionUri].ToAgentExtension(), registry.Extensions[ExtensionUri].ToAgentExtension()];
        Assert.Throws<InvalidOperationException>(() => registry.PrepareAgentCard(card));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImplicitScalarDefaults_AreEquivalentButTheSuppliedRepresentationIsPreserved(bool hasSignatures)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var registry = builder.Build();
        var card = registry.PrepareAgentCard(CreateCard());
        card.Capabilities.Extensions![0].Required = false;
        card.Capabilities.Extensions[0].Description = "";
        if (hasSignatures) card.Signatures = [new AgentCardSignature { Protected = "protected", Signature = "signature" }];
        var before = Serialize(card);

        registry.ValidateAgentCard(card);

        Assert.Equal(before, Serialize(registry.PrepareAgentCard(card)));
    }

    [Fact]
    public void Parameters_AreComparedStructurallyWithoutRewritingSignedJson()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Params = Json("{\"a\":1,\"b\":[true,2]}") });
        var registry = builder.Build();
        var card = CreateCard();
        card.Capabilities.Extensions = [new AgentExtension { Uri = ExtensionUri, Params = Json("{\"b\":[true,2],\"a\":1}") }];
        card.Signatures = [new AgentCardSignature { Protected = "protected", Signature = "signature" }];
        var before = Serialize(card);

        Assert.Equal(before, Serialize(registry.PrepareAgentCard(card)));
    }

    [Fact]
    public void CardAndRegistry_RemainIndependentAfterPreparation()
    {
        var registry = CreateRegistry();
        var original = CreateCard();
        original.Signatures = [];
        var hosted = registry.PrepareAgentCard(original);
        original.Name = "changed";
        original.Skills[0].Id = "changed";
        hosted.Capabilities.Extensions![0].Description = "changed";

        Assert.Equal("Debate agent", hosted.Name);
        Assert.Equal("debate", hosted.Skills[0].Id);
        Assert.Equal("Debate continuation", registry.Extensions[ExtensionUri].Description);
        Assert.Throws<InvalidOperationException>(() => registry.ValidateAgentCard(hosted));
    }

    [Fact]
    public void Validation_DoesNotDeriveOrMutateDeclarations()
    {
        var card = CreateCard();
        Assert.Throws<InvalidOperationException>(() => CreateRegistry().ValidateAgentCard(card));
        Assert.Null(card.Capabilities.Extensions);
    }

    [Fact]
    public void EmptyRegistry_AcceptsAnUnchangedSignedCardWithNoExtensions()
    {
        var card = CreateCard();
        card.Signatures = [new AgentCardSignature { Protected = "protected", Signature = "signature" }];
        var before = Serialize(card);
        Assert.Equal(before, Serialize(new A2ACustomOperationRegistryBuilder().Build().PrepareAgentCard(card)));
    }

    [Fact]
    public void InvalidInput_IsRejected()
    {
        var registry = CreateRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.PrepareAgentCard(null!));
        var card = CreateCard();
        card.Capabilities = null!;
        Assert.Throws<ArgumentException>(() => registry.PrepareAgentCard(card));
    }

    private static A2ACustomOperationRegistry CreateRegistry()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri, Description = "Debate continuation", Required = true, Params = Json("{\"version\":1}") });
        return builder.Build();
    }

    private static AgentCard CreateCard() => new()
    {
        Name = "Debate agent",
        Description = "Turns ideas into documentation",
        Version = "1.0",
        SupportedInterfaces = [new AgentInterface { Url = "https://example.org/rpc", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }],
        DefaultInputModes = ["text/plain"],
        DefaultOutputModes = ["text/plain"],
        Skills = [new AgentSkill { Id = "debate", Name = "Debate", Description = "Explore an idea" }],
    };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Serialize(AgentCard card) => JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions);
}
