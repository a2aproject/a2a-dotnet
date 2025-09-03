using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace A2A.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(A2A0001_UnknownFirstCodeFix)), System.Composition.Shared]
public sealed class A2A0001_UnknownFirstCodeFix : CodeFixProvider
{
    private const string Title = "Ensure 'Unknown = 0' is first";
    private const string EquivalenceKey = "EnsureUnknownFirst";

    // Prebuilt members to avoid repeated allocations
    private static readonly SyntaxTriviaList UnknownDocTrivia = SyntaxFactory.ParseLeadingTrivia(
        """
        /// <summary> Unknown value, used for unrecognized values. </summary>

        """);

    private static readonly EnumMemberDeclarationSyntax UnknownZeroMemberTemplate =
        SyntaxFactory.EnumMemberDeclaration("Unknown")
            .WithEqualsValue(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))))
            .WithLeadingTrivia(UnknownDocTrivia);

    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["A2A0001"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var ct = context.CancellationToken;
        var root = await context.Document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
            return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var enumDecl = node.FirstAncestorOrSelf<EnumDeclarationSyntax>() ?? node as EnumDeclarationSyntax;
        if (enumDecl is null)
            return;

        context.RegisterCodeFix(
            Microsoft.CodeAnalysis.CodeActions.CodeAction.Create(
                title: Title,
                createChangedDocument: token => EnsureUnknownFirstAsync(context.Document, root, enumDecl, token),
                equivalenceKey: EquivalenceKey),
            diagnostic);
    }

    private static Task<Document> EnsureUnknownFirstAsync(Document document, SyntaxNode root, EnumDeclarationSyntax enumDecl, CancellationToken ct)
    {
        var members = enumDecl.Members;
        var workingEnum = enumDecl;

        // Remove first existing Unknown if present to ensure it ends up first
        int unknownIndex = -1;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Identifier.ValueText == "Unknown")
            {
                unknownIndex = i;
                break;
            }
        }
        if (unknownIndex >= 0)
        {
            workingEnum = workingEnum.WithMembers(workingEnum.Members.RemoveAt(unknownIndex));
        }

        // Insert at start
        var newEnumDecl = workingEnum.WithMembers(workingEnum.Members.Insert(0, UnknownZeroMemberTemplate));

        var newRoot = root.ReplaceNode(enumDecl, newEnumDecl);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }
}
