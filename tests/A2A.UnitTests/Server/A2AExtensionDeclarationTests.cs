using System.Text.Json;

namespace A2A.UnitTests.Server;

public sealed class A2AExtensionDeclarationTests
{
    private const string ExtensionUri = "https://example.org/extensions/debate/v1";
    private const string OtherUri = "https://example.org/extensions/other/v1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UsageExample_AddsOnlyTheSelectedDeclarationWithoutChangingTheInput(bool emptyList)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        builder.RegisterExtension(new AgentExtension { Uri = OtherUri });
        var registry = builder.Build();
        var card = CreateCard();
        if (emptyList) card.Capabilities.Extensions = [];
        var before = Serialize(card);

        var prepared = extension.EnsureAgentCardDeclaration(card);

        Assert.Equal(before, Serialize(card));
        Assert.Equal(ExtensionUri, Assert.Single(prepared.Capabilities.Extensions!).Uri);
        Assert.Equal(2, registry.Extensions.Count);
        Assert.NotSame(card, prepared);
        Assert.NotSame(card.Capabilities, prepared.Capabilities);
        Assert.Equal(Serialize(prepared), Serialize(extension.EnsureAgentCardDeclaration(prepared)));
    }

    [Fact]
    public void MissingDeclaration_IsAppendedWithoutChangingUnrelatedDeclarations()
    {
        var extension = CreateRegistration();
        var card = CreateCard();
        var unrelated = new AgentExtension { Uri = OtherUri, Description = "Host declaration", Params = Json("{\"a\":1}") };
        card.Capabilities.Extensions = [unrelated];
        var before = Serialize(card);

        var prepared = extension.EnsureAgentCardDeclaration(card);

        Assert.Equal(before, Serialize(card));
        Assert.Equal(new[] { OtherUri, ExtensionUri }, prepared.Capabilities.Extensions!.Select(item => item.Uri));
        Assert.Equal("Host declaration", prepared.Capabilities.Extensions![0].Description);
        Assert.Equal(1, prepared.Capabilities.Extensions[0].Params!.Value.GetProperty("a").GetInt32());
        Assert.Equal(extension.Description, prepared.Capabilities.Extensions[1].Description);
        Assert.Equal(extension.Required, prepared.Capabilities.Extensions[1].Required);
        Assert.True(JsonElement.DeepEquals(extension.Params!.Value, prepared.Capabilities.Extensions[1].Params!.Value));
        Assert.NotSame(unrelated, prepared.Capabilities.Extensions[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingDeclaration_PreservesOrderRepresentationAndSignatures(bool hasSignatures)
    {
        var extension = CreateRegistration();
        var card = CreateCard();
        card.Capabilities.Extensions =
        [
            new AgentExtension { Uri = OtherUri },
            extension.ToAgentExtension(),
            new AgentExtension { Uri = "https://example.org/extensions/third/v1" },
        ];
        if (hasSignatures) Sign(card);
        var before = Serialize(card);

        var prepared = extension.EnsureAgentCardDeclaration(card);

        Assert.Equal(before, Serialize(card));
        Assert.Equal(before, Serialize(prepared));
        Assert.NotSame(card.Capabilities.Extensions, prepared.Capabilities.Extensions);
        Assert.NotSame(card.Capabilities.Extensions[1], prepared.Capabilities.Extensions![1]);
        if (hasSignatures) Assert.Equal("signature", Assert.Single(prepared.Signatures!).Signature);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImplicitScalarDefaults_AreEquivalentWithoutNormalizingTheCard(bool hasSignatures, bool explicitRegistrationDefaults)
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension
        {
            Uri = ExtensionUri,
            Description = explicitRegistrationDefaults ? "" : null,
            Required = explicitRegistrationDefaults ? false : null,
        });
        var card = CreateCard();
        card.Capabilities.Extensions = [new AgentExtension
        {
            Uri = ExtensionUri,
            Description = explicitRegistrationDefaults ? null : "",
            Required = explicitRegistrationDefaults ? null : false,
        }];
        if (hasSignatures) Sign(card);
        var before = Serialize(card);

        Assert.Equal(before, Serialize(extension.EnsureAgentCardDeclaration(card)));
        Assert.Equal(before, Serialize(card));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructurallyEquivalentParameters_RetainTheirSuppliedOrder(bool hasSignatures)
    {
        var extension = CreateRegistration();
        var card = CreateCard();
        card.Capabilities.Extensions = [extension.ToAgentExtension()];
        card.Capabilities.Extensions[0].Params = Json("{\"b\":[true,2],\"a\":1}");
        if (hasSignatures) Sign(card);
        var before = Serialize(card);

        Assert.Equal(before, Serialize(extension.EnsureAgentCardDeclaration(card)));
        Assert.Equal(before, Serialize(card));
    }

    [Theory]
    [InlineData("description")]
    [InlineData("required")]
    [InlineData("params-value")]
    [InlineData("params-order")]
    [InlineData("params-missing")]
    [InlineData("params-null")]
    public void ConflictingDeclaration_IsRejectedWithoutMutatingUnsignedOrSignedCards(string field)
    {
        var extension = CreateRegistration();
        var card = CreateCard();
        var declaration = extension.ToAgentExtension();
        switch (field)
        {
            case "description": declaration.Description = "conflicting"; break;
            case "required": declaration.Required = true; break;
            case "params-value": declaration.Params = Json("{\"a\":99,\"b\":[true,2]}"); break;
            case "params-order": declaration.Params = Json("{\"a\":1,\"b\":[2,true]}"); break;
            case "params-missing": declaration.Params = null; break;
            case "params-null": declaration.Params = Json("null"); break;
        }
        card.Capabilities.Extensions = [new AgentExtension { Uri = OtherUri }, declaration];
        var before = Serialize(card);
        Assert.Throws<InvalidOperationException>(() => extension.EnsureAgentCardDeclaration(card));
        Assert.Equal(before, Serialize(card));

        Sign(card);
        before = Serialize(card);
        var error = Assert.Throws<InvalidOperationException>(() => extension.EnsureAgentCardDeclaration(card));
        Assert.Contains("re-sign", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Serialize(card));
    }

    [Fact]
    public void AbsentParameters_DoNotMatchAnExplicitJsonNull()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        var card = CreateCard();
        card.Capabilities.Extensions = [new AgentExtension { Uri = ExtensionUri, Params = Json("null") }];

        Assert.Throws<InvalidOperationException>(() => extension.EnsureAgentCardDeclaration(card));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateTargetDeclarations_AreRejectedWithoutMutation(bool hasSignatures)
    {
        var extension = CreateRegistration();
        var card = CreateCard();
        card.Capabilities.Extensions = [extension.ToAgentExtension(), new AgentExtension { Uri = OtherUri }, extension.ToAgentExtension()];
        if (hasSignatures) Sign(card);
        var before = Serialize(card);

        Assert.Throws<InvalidOperationException>(() => extension.EnsureAgentCardDeclaration(card));
        Assert.Equal(before, Serialize(card));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedCardMissingTheTarget_RequiresResigningAndRetainsSignatures(bool hasUnrelatedDeclaration)
    {
        var extension = CreateRegistration();
        var card = CreateCard();
        if (hasUnrelatedDeclaration) card.Capabilities.Extensions = [new AgentExtension { Uri = OtherUri }];
        Sign(card);
        var before = Serialize(card);

        var error = Assert.Throws<InvalidOperationException>(() => extension.EnsureAgentCardDeclaration(card));

        Assert.Contains(ExtensionUri, error.Message, StringComparison.Ordinal);
        Assert.Contains("re-sign", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, Serialize(card));
        Assert.Single(card.Signatures!);
    }

    [Theory]
    [InlineData("HTTPS://EXAMPLE.ORG/EXTENSIONS/DEBATE/V1")]
    [InlineData("https://example.org/extensions/debate/v2")]
    public void DifferentCaseOrVersion_IsAnUnrelatedDeclaration(string uri)
    {
        var card = CreateCard();
        card.Capabilities.Extensions = [new AgentExtension { Uri = uri }];

        var prepared = CreateRegistration().EnsureAgentCardDeclaration(card);

        Assert.Equal(new[] { uri, ExtensionUri }, prepared.Capabilities.Extensions!.Select(item => item.Uri));
        Assert.Equal(uri, Assert.Single(card.Capabilities.Extensions).Uri);
    }

    [Fact]
    public void ReturnedCard_IsDetachedIncludingSkillsDeclarationsAndSignatureHeaders()
    {
        var extension = CreateRegistration();
        var card = CreateCard();
        card.Capabilities.Extensions = [extension.ToAgentExtension()];
        Sign(card);
        var before = Serialize(card);
        var prepared = extension.EnsureAgentCardDeclaration(card);

        prepared.Name = "changed";
        prepared.Skills[0].Id = "changed";
        prepared.Capabilities.Extensions![0].Description = "changed";
        prepared.Signatures![0].Signature = "changed";
        prepared.Signatures[0].Header!["kid"] = Json("\"changed\"");

        Assert.Equal(before, Serialize(card));
        Assert.Equal("Debate continuation", extension.Description);
        Assert.Equal(1, extension.Params!.Value.GetProperty("a").GetInt32());
    }

    [Fact]
    public async Task Composition_DoesNotScopeWholeRegistryValidationOrRequiredExtensionChecks()
    {
        var builder = new A2ACustomOperationRegistryBuilder();
        var extension = builder.RegisterExtension(new AgentExtension { Uri = ExtensionUri });
        builder.RegisterExtension(new AgentExtension { Uri = OtherUri, Required = true });
        var registry = builder.Build();

        var partial = extension.EnsureAgentCardDeclaration(CreateCard());

        Assert.Equal(ExtensionUri, Assert.Single(partial.Capabilities.Extensions!).Uri);
        Assert.Throws<InvalidOperationException>(() => registry.ValidateAgentCard(partial));
        Assert.Throws<InvalidOperationException>(() => registry.PrepareAgentCard(partial));
        var error = await Assert.ThrowsAsync<A2AException>(async () =>
            await registry.NegotiateExtensionsAsync([extension.Uri]));
        Assert.Equal(A2AErrorCode.ExtensionSupportRequired, error.ErrorCode);
        var complete = registry.PrepareAgentCard(CreateCard());
        registry.ValidateAgentCard(complete);
        Assert.Equal(2, complete.Capabilities.Extensions!.Count);
    }

    [Fact]
    public void InvalidInput_IsRejectedWithTheArgumentName()
    {
        var extension = CreateRegistration();
        Assert.Equal("agentCard", Assert.Throws<ArgumentNullException>(() => extension.EnsureAgentCardDeclaration(null!)).ParamName);
        var card = CreateCard();
        card.Capabilities = null!;
        Assert.Equal("agentCard", Assert.Throws<ArgumentException>(() => extension.EnsureAgentCardDeclaration(card)).ParamName);
    }

    private static A2AExtensionRegistration CreateRegistration()
        => new A2ACustomOperationRegistryBuilder().RegisterExtension(new AgentExtension
        {
            Uri = ExtensionUri,
            Description = "Debate continuation",
            Required = false,
            Params = Json("{\"a\":1,\"b\":[true,2]}"),
        });

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

    private static void Sign(AgentCard card)
        => card.Signatures = [new AgentCardSignature
        {
            Protected = "protected",
            Signature = "signature",
            Header = new Dictionary<string, JsonElement> { ["kid"] = Json("\"original\"") },
        }];

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Serialize(AgentCard card) => JsonSerializer.Serialize(card, A2AJsonUtilities.DefaultOptions);
}
