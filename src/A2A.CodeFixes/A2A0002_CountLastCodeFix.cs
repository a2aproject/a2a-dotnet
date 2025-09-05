using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace A2A.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(A2A0002_CountLastCodeFix)), System.Composition.Shared]
public sealed class A2A0002_CountLastCodeFix : CodeFixProvider
{
    private const string Title = "Ensure 'Count' is last";
    private const string EquivalenceKey = "EnsureCountLast";

    // Parse once to avoid repeated allocations
    private static readonly SyntaxTriviaList CountDocTrivia = SyntaxFactory.ParseLeadingTrivia(
        """
        /// <summary> Helper value to track the number of enum values when used as array indices. This must always be the last value in the enumeration. </summary>

        """);

    private static readonly EnumMemberDeclarationSyntax CountMemberTemplate =
        SyntaxFactory.EnumMemberDeclaration("Count").WithLeadingTrivia(CountDocTrivia);

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["A2A0002"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var ct = context.CancellationToken;
        var root = await context.Document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // The diagnostic is reported on the enum identifier, so we need to find the containing enum declaration
        var enumDecl = node.FirstAncestorOrSelf<EnumDeclarationSyntax>();
        if (enumDecl is null)
            return;

        context.RegisterCodeFix(
            Microsoft.CodeAnalysis.CodeActions.CodeAction.Create(
                title: Title,
                createChangedDocument: token => EnsureCountLastAsync(context.Document, root, enumDecl, token),
                equivalenceKey: EquivalenceKey),
            diagnostic);
    }

    private static Task<Document> EnsureCountLastAsync(Document document, SyntaxNode root, EnumDeclarationSyntax enumDecl, CancellationToken ct)
    {
        var members = enumDecl.Members;
        var workingEnum = enumDecl;

        // Remove first existing Count if present to ensure it ends up last
        int countIndex = -1;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Identifier.ValueText == "Count")
            {
                countIndex = i;
                break;
            }
        }
        if (countIndex >= 0)
        {
            workingEnum = workingEnum.WithMembers(workingEnum.Members.RemoveAt(countIndex));
        }

        // Append Count at end with pre-parsed documentation trivia
        var newEnumDecl = workingEnum.WithMembers(workingEnum.Members.Add(CountMemberTemplate));

        var newRoot = root.ReplaceNode(enumDecl, newEnumDecl);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
