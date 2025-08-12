namespace A2A.UnitTests.Server
{
    public class AgentCardProviderTests
    {
        [Fact]
        public async Task UserCanCustomizeReturnedAgentCardByProvidingItUpFront()
        {
            AgentCard expected = CreateValidAgentCard();

            AgentCardProvider provider = new(expected);

            AgentCard returned = await provider.GetAgentCardAsync("https://example.com/test-agent");

            Assert.False(ReferenceEquals(expected, returned), "Returned agent card should be a copy of the provided card.");
            Assert.Equal(expected.Name, returned.Name);
            Assert.Equal(expected.Description, returned.Description);
            Assert.Equal(expected.Version, returned.Version);
            Assert.Equal(expected.Url, returned.Url);
            Assert.Equal(expected.Capabilities.PushNotifications, returned.Capabilities.PushNotifications);
            Assert.Equal(expected.Capabilities.Streaming, returned.Capabilities.Streaming);
        }

        [Theory]
        [InlineData(CustomAgentCardProvider.FirstAgentUrl, CustomAgentCardProvider.FirstAgentName)]
        [InlineData(CustomAgentCardProvider.SecondAgentUrl, CustomAgentCardProvider.SecondAgentName)]
        public async Task UserCanCustomizeReturnedAgentCardByBuildingItWhenRequested(string url, string expectedName)
        {
            CustomAgentCardProvider provider = new();

            AgentCard agentCard = await provider.GetAgentCardAsync(url);

            Assert.Equal(expectedName, agentCard.Name);
            Assert.Equal(url, agentCard.Url);
        }

        private sealed class CustomAgentCardProvider : AgentCardProvider
        {
            internal const string FirstAgentUrl = "https://example.com/first-agent";
            internal const string SecondAgentUrl = "https://example.com/second-agent";
            internal const string FirstAgentName = "First";
            internal const string SecondAgentName = "Second";

            public override Task<AgentCard> GetAgentCardAsync(string agentUrl, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new AgentCard()
                {
                    Name = agentUrl == FirstAgentUrl ? FirstAgentName : SecondAgentName,
                    Description = "Custom Agent Card Provider",
                    Version = "1.0.0",
                    Url = agentUrl,
                    Capabilities = new AgentCapabilities
                    {
                        PushNotifications = true,
                        Streaming = true
                    }
                });
            }
        }

        [Fact]
        public async Task InvalidAgentCardProviderThrowsException()
        {
            InvalidAgentCardProvider provider = new();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.GetAgentCardAsync("https://example.com/invalid-agent"));
        }

        private sealed class InvalidAgentCardProvider : AgentCardProvider
        {
            // This type does not specify an agent card via constructor
            // and does not override GetAgentCardAsync, so it will throw an exception.
        }

        [Fact]
        public void NullIsInvalidAgentCard()
        {
            Assert.Throws<ArgumentNullException>(() => new AgentCardProvider(agentCard: null!));
            Assert.Throws<ArgumentNullException>(() => AgentCardProvider.Validate(agentCard: null!));
        }

        public static IEnumerable<object[]> InvalidStrings()
        {
            yield return new object[] { null! };
            yield return new object[] { "" };
            yield return new object[] { " " };
        }

        [Theory, MemberData(nameof(InvalidStrings))]
        public void AgentCardMustDefineNonEmptyName(string? invalidName)
        {
            AgentCard agentCard = CreateValidAgentCard();
            agentCard.Name = invalidName!;
            var ex = Assert.Throws<ArgumentException>(() => AgentCardProvider.Validate(agentCard));
            Assert.Equal("Agent card must define a non-empty Name. (Parameter 'agentCard')", ex.Message);
        }

        [Theory, MemberData(nameof(InvalidStrings))]
        public void AgentCardMustDefineNonEmptyDescription(string? invalidDescription)
        {
            AgentCard agentCard = CreateValidAgentCard();
            agentCard.Description = invalidDescription!;
            var ex = Assert.Throws<ArgumentException>(() => AgentCardProvider.Validate(agentCard));
            Assert.Equal("Agent card must define a non-empty Description. (Parameter 'agentCard')", ex.Message);
        }

        [Theory, MemberData(nameof(InvalidStrings))]
        public void AgentCardMustDefineNonEmptyVersion(string? invalidVersion)
        {
            AgentCard agentCard = CreateValidAgentCard();
            agentCard.Version = invalidVersion!;
            var ex = Assert.Throws<ArgumentException>(() => AgentCardProvider.Validate(agentCard));
            Assert.Equal("Agent card must define a non-empty Version. (Parameter 'agentCard')", ex.Message);
        }

        [Theory, MemberData(nameof(InvalidStrings))]
        public void AgentCardMustDefineNonEmptyProtocolVersion(string? invalidVersion)
        {
            AgentCard agentCard = CreateValidAgentCard();
            agentCard.ProtocolVersion = invalidVersion!;
            var ex = Assert.Throws<ArgumentException>(() => AgentCardProvider.Validate(agentCard));
            Assert.Equal("Agent card must define a non-empty ProtocolVersion. (Parameter 'agentCard')", ex.Message);
        }

        public static IEnumerable<object[]> InvalidModes()
        {
            yield return new object[] { null! };
            yield return new object[] { new List<string>() };
        }

        [Theory, MemberData(nameof(InvalidModes))]
        public void AgentCardMustDefineDefaultInputModesWithAtLeastOneMode(List<string> modes)
        {
            AgentCard agentCard = CreateValidAgentCard();
            agentCard.DefaultInputModes = modes;
            var ex = Assert.Throws<ArgumentException>(() => AgentCardProvider.Validate(agentCard));
            Assert.Equal("Agent card must define DefaultInputModes with at least one mode. (Parameter 'agentCard')", ex.Message);
        }

        [Theory, MemberData(nameof(InvalidModes))]
        public void AgentCardMustDefineDefaultOutputModesWithAtLeastOneMode(List<string> modes)
        {
            AgentCard agentCard = CreateValidAgentCard();
            agentCard.DefaultOutputModes = modes;
            var ex = Assert.Throws<ArgumentException>(() => AgentCardProvider.Validate(agentCard));
            Assert.Equal("Agent card must define DefaultOutputModes with at least one mode. (Parameter 'agentCard')", ex.Message);
        }

        private static AgentCard CreateValidAgentCard() => new()
        {
            Name = "Test Agent",
            Description = "This is a test agent expected.",
            Version = "1.0.0",
            Url = "https://example.com/test-agent",
            Capabilities = new()
            {
                PushNotifications = false,
                Streaming = true,
            }
        };
    }
}