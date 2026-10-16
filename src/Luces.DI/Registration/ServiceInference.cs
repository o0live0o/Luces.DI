using Luces.DI.Generators.Discovery;
using Luces.DI.Generators.Symbols;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Linq;

namespace Luces.DI.Generators.Registration;

/// <summary>
/// Infers the service type from an implementation: it takes the "most specific" non-marker interface (excluding those inherited by another candidate); with no candidate interface it registers itself; multiple unrelated candidates are ambiguous.
/// </summary>
internal static class ServiceInference
{
    public static ServiceInferenceResult Infer(
        INamedTypeSymbol implementation,
        DefinitionSymbols definitions)
    {
        var candidates = CollectCandidates(implementation, definitions);
        if (candidates.Length == 0)
        {
            return new ServiceInferenceResult(
                implementation,
                ImmutableArray<INamedTypeSymbol>.Empty);
        }

        if (candidates.Length == 1)
        {
            return new ServiceInferenceResult(
                candidates[0],
                ImmutableArray<INamedTypeSymbol>.Empty);
        }

        return new ServiceInferenceResult(null, candidates);
    }

    private static ImmutableArray<INamedTypeSymbol> CollectCandidates(
        INamedTypeSymbol implementation,
        DefinitionSymbols definitions)
    {
        var contracts = implementation.AllInterfaces
            .Where(@interface => !IsMarker(@interface, definitions))
            .Where(@interface => !IsRecordEqualityContract(implementation, @interface))
            .ToArray();

        return contracts
            .Where(candidate => !IsInheritedByAnother(contracts, candidate))
            .ToImmutableArray();
    }

    /// <summary>
    /// Whether the candidate is inherited by another sibling candidate interface—if so, it is not the "most specific" one.
    /// </summary>
    private static bool IsInheritedByAnother(
        INamedTypeSymbol[] contracts,
        INamedTypeSymbol candidate)
    {
        foreach (var other in contracts)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, other))
            {
                continue;
            }

            foreach (var inherited in other.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(inherited, candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsMarker(INamedTypeSymbol type, DefinitionSymbols definitions)
    {
        return SymbolEqualityComparer.Default.Equals(type, definitions.ScopedMarker)
            || SymbolEqualityComparer.Default.Equals(type, definitions.SingletonMarker)
            || SymbolEqualityComparer.Default.Equals(type, definitions.TransientMarker);
    }

    /// <summary>
    /// The <c>System.IEquatable&lt;T&gt;</c> synthesized for a record in the implementation's own record inheritance chain is not a service contract; a record that implements <c>IEquatable&lt;TSomethingElse&gt;</c> explicitly does declare a contract.
    /// </summary>
    private static bool IsRecordEqualityContract(
        INamedTypeSymbol implementation,
        INamedTypeSymbol @interface)
    {
        if (@interface.OriginalDefinition.GetMetadataFullName() != "System.IEquatable`1"
            || @interface.TypeArguments.Length != 1
            || @interface.TypeArguments[0] is not INamedTypeSymbol argument
            || !argument.IsRecord)
        {
            return false;
        }

        // A record reaches the equality interfaces synthesized for the whole record inheritance chain, so every record
        // base type up to the implementation itself counts, not only the implementation.
        for (INamedTypeSymbol? current = implementation; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, argument))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Inference result: a non-empty <see cref="Service"/> means success; an empty <see cref="Service"/> with a non-empty <see cref="AmbiguousCandidates"/> means ambiguous.
/// </summary>
internal readonly record struct ServiceInferenceResult(
    INamedTypeSymbol? Service,
    ImmutableArray<INamedTypeSymbol> AmbiguousCandidates);
