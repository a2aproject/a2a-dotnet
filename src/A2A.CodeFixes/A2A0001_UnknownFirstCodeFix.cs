using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace A2A.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(A2A0001_UnknownFirstCodeFix)), System.Composition.Shared]
public sealed class A2A0001_UnknownFirstCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["A2A0001"];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        //return;

#pragma warning disable CS0162 // Unreachable code detected
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var enumDecl = node.FirstAncestorOrSelf<EnumDeclarationSyntax>() ?? node as EnumDeclarationSyntax;
        if (enumDecl is null) return;

        context.RegisterCodeFix(
            Microsoft.CodeAnalysis.CodeActions.CodeAction.Create(
                title: "Ensure 'Unknown = 0' is first",
                createChangedDocument: ct => EnsureUnknownFirstAsync(context.Document, enumDecl, ct),
                equivalenceKey: "EnsureUnknownFirst"),
            diagnostic);
#pragma warning restore CS0162 // Unreachable code detected
    }

    private static async Task<Document> EnsureUnknownFirstAsync(Document document, EnumDeclarationSyntax enumDecl, CancellationToken ct)
    {
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);

        var members = enumDecl.Members;

        // Remove existing Unknown if present
        var existingUnknown = members.FirstOrDefault(m => m.Identifier.Text == "Unknown");
        var workingEnum = enumDecl;
        if (existingUnknown != default)
        {
            workingEnum = workingEnum.WithMembers(workingEnum.Members.Remove(existingUnknown));
        }

        // Create Unknown = 0 with XML summary
        var unknown = SyntaxFactory.EnumMemberDeclaration("Unknown")
            .WithEqualsValue(SyntaxFactory.EqualsValueClause(
                SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0))))
            .WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia("""
                /// <summary> Unknown value, used for unrecognized values. </summary>

                """));

        // Insert at start (SeparatedSyntaxList handles separators)
        var newMembers = workingEnum.Members.Insert(0, unknown);
        var newEnumDecl = workingEnum.WithMembers(newMembers);

        editor.ReplaceNode(enumDecl, newEnumDecl);
        return editor.GetChangedDocument();
    }
}
