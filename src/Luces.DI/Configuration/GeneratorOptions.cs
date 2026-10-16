using Microsoft.CodeAnalysis.Diagnostics;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Luces.DI.Generators.Configuration;

/// <summary>
/// The generator's MSBuild configuration. The two properties are exposed via the buildTransitive props as <c>CompilerVisibleProperty</c>; this type only reads and validates them.
/// </summary>
internal readonly record struct GeneratorOptions(
    string DefinitionNamespace,
    ImmutableArray<SingleResolveTarget> SingleResolveServices,
    ImmutableArray<string> InvalidValues)
{
    /// <summary>
    /// The default namespace of the marker interfaces and <c>[Register*]</c> attributes.
    /// </summary>
    public const string DefaultNamespace = "Luces.DI";

    private const string NamespacePropertyKey = "build_property.DiRegistrationNamespace";
    private const string SingleResolvePropertyKey = "build_property.AutoDiSingleResolveServices";

    public static GeneratorOptions Read(AnalyzerConfigOptions options)
    {
        var definitionNamespace = options.TryGetValue(NamespacePropertyKey, out var configured)
            && !string.IsNullOrWhiteSpace(configured)
                ? configured.Trim()
                : DefaultNamespace;

        var singleResolve = options.TryGetValue(SingleResolvePropertyKey, out var raw)
            ? SingleResolveOptionParser.Parse(raw)
            : SingleResolveParseResult.Empty;

        return new GeneratorOptions(
            definitionNamespace,
            singleResolve.Targets,
            singleResolve.InvalidValues);
    }

    /// <summary>
    /// Whether the namespace is valid: a sequence of plain identifiers separated by <c>.</c>.
    /// </summary>
    public static bool IsValidNamespace(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Split('.').All(SingleResolveOptionParser.IsValidIdentifier);
    }
}
