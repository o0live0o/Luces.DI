using Luces.DI.Generators.Emission;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Luces.DI.Generators.Discovery;

/// <summary>
/// Stitches candidates together with the compilation: what gets injected are <c>internal</c> symbols, which can only be matched correctly in a "symbol-visible compilation"; therefore the definitions of the selected namespace must always be added to the compilation and the candidate symbols re-resolved. The generator's own outputs are not part of the compilation it analyzes, so this holds for the default namespace as well.
/// </summary>
internal static class CandidateBinder
{
    public static CandidateBinding Bind(
        Compilation compilation,
        ImmutableArray<Candidate> candidates,
        string definitionNamespace)
    {
        var semantic = AddDefinitionsForBinding(compilation, definitionNamespace);

        return new CandidateBinding(
            semantic,
            Rebind(semantic, candidates),
            DefinitionSymbols.Resolve(semantic, definitionNamespace));
    }

    private static Compilation AddDefinitionsForBinding(
        Compilation compilation,
        string definitionNamespace)
    {
        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options
            as CSharpParseOptions
            ?? CSharpParseOptions.Default;

        var definitions = CSharpSyntaxTree.ParseText(
            RegistrationEmitter.BuildDefinitions(definitionNamespace),
            parseOptions,
            path: "DiRegistration.Definitions.Binding.g.cs",
            encoding: Encoding.UTF8);

        return compilation.AddSyntaxTrees(definitions);
    }

    private static ImmutableArray<Candidate> Rebind(
        Compilation compilation,
        ImmutableArray<Candidate> candidates)
    {
        var builder = ImmutableArray.CreateBuilder<Candidate>(candidates.Length);
        foreach (var candidate in candidates)
        {
            builder.Add(Rebind(compilation, candidate));
        }

        return builder.ToImmutable();
    }

    private static Candidate Rebind(Compilation compilation, Candidate candidate)
    {
        var tree = candidate.Location.SourceTree;
        if (tree is null)
        {
            return candidate;
        }

        var declaration = FindEnclosingTypeDeclaration(
            tree,
            candidate.Location.SourceSpan.Start);
        if (declaration is null)
        {
            return candidate with { Symbol = null };
        }

        var symbol = compilation.GetSemanticModel(tree)
            .GetDeclaredSymbol(declaration) as INamedTypeSymbol;
        return candidate with { Symbol = symbol };
    }

    private static TypeDeclarationSyntax? FindEnclosingTypeDeclaration(
        SyntaxTree tree,
        int position)
    {
        var node = tree.GetRoot().FindToken(position).Parent;
        while (node is not null && node is not TypeDeclarationSyntax)
        {
            node = node.Parent;
        }

        return node as TypeDeclarationSyntax;
    }
}

/// <summary>
/// Binding result: the symbol-visible compilation, the re-resolved candidates, and the injected definition symbols.
/// </summary>
internal readonly record struct CandidateBinding(
    Compilation Compilation,
    ImmutableArray<Candidate> Candidates,
    DefinitionSymbols Symbols);
