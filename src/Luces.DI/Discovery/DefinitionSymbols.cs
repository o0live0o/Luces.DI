using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Luces.DI.Generators.Discovery;

/// <summary>
/// Symbols of the injected marker interfaces and <c>[Register*]</c> attributes within this assembly. Lookup is confined to the current assembly: the injected types are <c>internal</c>, so same-named types in referenced assemblies must not participate in matching.
/// </summary>
internal sealed record DefinitionSymbols(
    INamedTypeSymbol? ScopedAttribute,
    INamedTypeSymbol? SingletonAttribute,
    INamedTypeSymbol? TransientAttribute,
    INamedTypeSymbol? ScopedMarker,
    INamedTypeSymbol? SingletonMarker,
    INamedTypeSymbol? TransientMarker)
{
    /// <summary>
    /// The simple names of every type the generator emits into the definition namespace.
    /// </summary>
    private static readonly ImmutableArray<string> InjectedTypeNames = ImmutableArray.Create(
        "RegisterScopedAttribute",
        "RegisterSingletonAttribute",
        "RegisterTransientAttribute",
        "IAutoScoped",
        "IAutoSingleton",
        "IAutoTransient");

    public static DefinitionSymbols Resolve(Compilation compilation, string definitionNamespace)
    {
        var assembly = compilation.Assembly;
        var prefix = definitionNamespace + ".";

        return new DefinitionSymbols(
            assembly.GetTypeByMetadataName(prefix + "RegisterScopedAttribute"),
            assembly.GetTypeByMetadataName(prefix + "RegisterSingletonAttribute"),
            assembly.GetTypeByMetadataName(prefix + "RegisterTransientAttribute"),
            assembly.GetTypeByMetadataName(prefix + "IAutoScoped"),
            assembly.GetTypeByMetadataName(prefix + "IAutoSingleton"),
            assembly.GetTypeByMetadataName(prefix + "IAutoTransient"));
    }

    /// <summary>
    /// The generated type names the consumer itself declares inside the definition namespace. Such a declaration is never reused and never renamed away silently: the call site reports the conflict and then generates neither definitions nor registrations.
    /// </summary>
    public static ImmutableArray<string> FindConflicts(
        Compilation compilation,
        string definitionNamespace)
    {
        var prefix = definitionNamespace + ".";
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var name in InjectedTypeNames)
        {
            if (compilation.Assembly.GetTypeByMetadataName(prefix + name) is not null)
            {
                builder.Add(prefix + name);
            }
        }

        return builder.ToImmutable();
    }
}
