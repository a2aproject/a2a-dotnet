using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System.Collections.Immutable;

namespace A2A.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(A2A0002_CountLastCodeFix)), System.Composition.Shared]
public sealed class A2A0002_CountLastCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["A2A0002"];

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
                title: "Ensure 'Count' is last",
                createChangedDocument: ct => EnsureCountLastAsync(context.Document, enumDecl, ct),
                equivalenceKey: "EnsureCountLast"),
            diagnostic);
#pragma warning restore CS0162 // Unreachable code detected
    }

    private static async Task<Document> EnsureCountLastAsync(Document document, EnumDeclarationSyntax enumDecl, CancellationToken ct)
    {
        var editor = await DocumentEditor.CreateAsync(document, ct).ConfigureAwait(false);

        var members = enumDecl.Members;

        // Remove existing Count if present
        var existingCount = members.FirstOrDefault(m => m.Identifier.Text == "Count");
        var workingEnum = enumDecl;
        if (existingCount != default)
        {
            workingEnum = workingEnum.WithMembers(workingEnum.Members.Remove(existingCount));
        }

        // Create Count with XML summary using structured trivia and elastic newlines
        var count = SyntaxFactory.EnumMemberDeclaration("Count")
            .WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia("""
                /// <summary> Helper value to track the number of enum values when used as array indices. This must always be the last value in the enumeration. </summary>

                """));

        // Append at end
        var newMembers = workingEnum.Members.Add(count);
        var newEnumDecl = workingEnum.WithMembers(newMembers);

        editor.ReplaceNode(enumDecl, newEnumDecl);
        return editor.GetChangedDocument();
    }
}
