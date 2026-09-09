using System.Text.Json;
using Microsoft.Extensions.AI;

namespace A2A.V0_3.UnitTests.GitHubIssues
{
    // https://github.com/a2aproject/a2a-dotnet/issues/298
    // FileContent (de)serialization poisoned a static JsonSerializerOptions cache in
    // A2AJsonConverter<T>.GetSafeOptions: the first options instance was captured in a
    // static field, and any second, distinct instance threw InvalidOperationException
    // ("Operation is not valid due to the current state of the object"), surfaced as
    // A2AException. A host layer such as Microsoft.Agents.AI.Hosting builds its own
    // options from A2AJsonUtilities.DefaultOptions with a rebuilt resolver chain; that
    // is exactly the second instance that tripped the guard.
    public sealed class Issue298
    {
        [Fact]
        public void Issue_298_SecondOptionsInstance_DoesNotPoisonConverterCache()
        {
            var file = new FileContent(new Uri("https://example.com/file.txt")) { MimeType = "text/plain" };

            // Prime the converter cache with the shared default options instance.
            var defaultOptions = A2AJsonUtilities.DefaultOptions;
            _ = JsonSerializer.Serialize(file, defaultOptions);

            // A second, distinct options instance built the way a host layer builds one:
            // a clone of the defaults with the resolver chain rebuilt (MEAI resolver first).
            var hostOptions = new JsonSerializerOptions(A2AJsonUtilities.DefaultOptions);
            hostOptions.TypeInfoResolverChain.Clear();
            hostOptions.TypeInfoResolverChain.Add(AIJsonUtilities.DefaultOptions.TypeInfoResolver!);
            foreach (var resolver in A2AJsonUtilities.DefaultOptions.TypeInfoResolverChain)
            {
                hostOptions.TypeInfoResolverChain.Add(resolver);
            }

            // Before the fix this threw A2AException ("Operation is not valid due to the
            // current state of the object"); it must now round-trip cleanly.
            var json = JsonSerializer.Serialize(file, hostOptions);
            var roundTripped = JsonSerializer.Deserialize<FileContent>(json, hostOptions);

            Assert.NotNull(roundTripped);
            Assert.Equal("https://example.com/file.txt", roundTripped!.Uri?.ToString());
            Assert.Equal("text/plain", roundTripped.MimeType);

            // The shared default instance must remain usable after the second instance was seen.
            var again = JsonSerializer.Serialize(file, defaultOptions);
            Assert.Contains("https://example.com/file.txt", again);
        }
    }
}
