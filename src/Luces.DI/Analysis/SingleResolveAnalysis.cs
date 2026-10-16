using Luces.DI.Generators.Configuration;
using Luces.DI.Generators.Diagnostics;
using Luces.DI.Generators.Discovery;
using Luces.DI.Generators.Registration;
using Luces.DI.Generators.Symbols;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Luces.DI.Generators.Analysis;

/// <summary>
/// Completeness check for the single-implementation <c>&lt;AutoDiSingleResolveServices&gt;</c> configuration: first, a source implementation is missing a non-keyed registration; second, the same closed service has multiple implementations within the dependency graph.
/// </summary>
internal static class SingleResolveAnalysis
{
    public static void Validate(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<SingleResolveTarget> targets,
        ImmutableArray<RegisteredService> registrations,
        ImmutableArray<Candidate> candidates)
    {
        if (targets.IsDefaultOrEmpty)
        {
            return;
        }

        var openServices = ResolveServices(
            context,
            compilation,
            targets,
            out var declaringAssemblies);
        if (openServices.Count == 0)
        {
            return;
        }

        ReportUnregisteredImplementations(
            context,
            openServices,
            registrations,
            candidates);
        ReportDuplicateImplementations(
            context,
            compilation,
            openServices,
            declaringAssemblies);
    }

    /// <summary>
    /// Resolves configuration entries into open service definitions (reports DIREG007 when an entry does not resolve to an interface).
    /// </summary>
    private static HashSet<INamedTypeSymbol> ResolveServices(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<SingleResolveTarget> targets,
        out HashSet<IAssemblySymbol> declaringAssemblies)
    {
        var openServices = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        declaringAssemblies = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

        foreach (var target in targets)
        {
            var metadataName = target.MetadataName
                + (target.Arity == 0 ? string.Empty : "`" + target.Arity);

            var service = compilation.GetTypeByMetadataName(metadataName);
            if (service is null || service.TypeKind != TypeKind.Interface)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GeneratorDiagnostics.InvalidGeneratorConfiguration,
                    Location.None,
                    "AutoDiSingleResolveServices",
                    target.GetIdentity()));
                continue;
            }

            openServices.Add(service.OriginalDefinition);
            declaringAssemblies.Add(service.ContainingAssembly);
        }

        return openServices;
    }

    /// <summary>
    /// A configured service's source implementation must have a non-keyed generated registration (otherwise DIREG006).
    /// </summary>
    private static void ReportUnregisteredImplementations(
        SourceProductionContext context,
        HashSet<INamedTypeSymbol> openServices,
        ImmutableArray<RegisteredService> registrations,
        ImmutableArray<Candidate> candidates)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var candidate in candidates)
        {
            var implementation = candidate.Symbol;
            if (implementation is null
                || implementation.TypeKind != TypeKind.Class
                || implementation.IsAbstract
                || implementation.IsStatic
                || !seen.Add(implementation))
            {
                continue;
            }

            foreach (var service in implementation.AllInterfaces)
            {
                if (!openServices.Contains(service.OriginalDefinition))
                {
                    continue;
                }

                if (HasUnkeyedRegistration(registrations, implementation, service))
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    GeneratorDiagnostics.UnregisteredSingleResolveImplementation,
                    candidate.Location,
                    implementation.GetQualifiedName(),
                    service.GetQualifiedName()));
            }
        }
    }

    private static bool HasUnkeyedRegistration(
        ImmutableArray<RegisteredService> registrations,
        INamedTypeSymbol implementation,
        INamedTypeSymbol service)
    {
        foreach (var registration in registrations)
        {
            if (registration.Key is not null)
            {
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(
                registration.ImplementationSymbol,
                implementation))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(
                registration.ServiceSymbol,
                service))
            {
                return true;
            }

            if (registration.IsOpenGeneric
                && SymbolEqualityComparer.Default.Equals(
                    registration.ServiceSymbol.OriginalDefinition,
                    service.OriginalDefinition))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Within the dependency graph a single closed service may have only one implementation (otherwise DIREG002).
    /// </summary>
    private static void ReportDuplicateImplementations(
        SourceProductionContext context,
        Compilation compilation,
        HashSet<INamedTypeSymbol> openServices,
        HashSet<IAssemblySymbol> declaringAssemblies)
    {
        var dependencyMemo = new Dictionary<IAssemblySymbol, bool>(
            SymbolEqualityComparer.Default);

        var relevant = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        foreach (var assembly in CollectReachableAssemblies(compilation.Assembly))
        {
            if (SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly)
                || DependsOnAny(assembly, declaringAssemblies, dependencyMemo))
            {
                relevant.Add(assembly);
            }
        }

        // Symbols, not display names, carry the identity here: a same-named service or implementation in another assembly is a different type and must be counted separately.
        var implementations = new Dictionary<INamedTypeSymbol, HashSet<INamedTypeSymbol>>(
            SymbolEqualityComparer.Default);

        foreach (var assembly in relevant)
        {
            foreach (var type in assembly.GlobalNamespace.EnumerateDeclaredTypes())
            {
                if (!IsConcreteImplementationCandidate(type))
                {
                    continue;
                }

                foreach (var @interface in type.AllInterfaces)
                {
                    if (!openServices.Contains(@interface.OriginalDefinition))
                    {
                        continue;
                    }

                    if (!implementations.TryGetValue(@interface, out var types))
                    {
                        types = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                        implementations[@interface] = types;
                    }

                    types.Add(type);
                }
            }
        }

        foreach (var pair in implementations
            .OrderBy(pair => pair.Key.GetQualifiedName(), StringComparer.Ordinal))
        {
            if (pair.Value.Count <= 1)
            {
                continue;
            }

            var types = pair.Value
                .OrderBy(type => type.GetQualifiedName(), StringComparer.Ordinal)
                .ToArray();

            // Distinct types may share a display name when they come from different assemblies; qualify the list in that case so the report stays readable.
            var namesAreAmbiguous = types
                .Select(type => type.GetQualifiedName())
                .Distinct(StringComparer.Ordinal)
                .Count() != types.Length;
            var names = types.Select(type => namesAreAmbiguous
                ? string.Concat(type.GetQualifiedName(), " (", type.ContainingAssembly.Name, ")")
                : type.GetQualifiedName());

            context.ReportDiagnostic(Diagnostic.Create(
                GeneratorDiagnostics.DuplicateSingleResolveImplementations,
                Location.None,
                pair.Key.GetQualifiedName(),
                pair.Value.Count,
                string.Join(", ", names)));
        }
    }

    private static bool IsConcreteImplementationCandidate(INamedTypeSymbol type)
    {
        return type.TypeKind is TypeKind.Class or TypeKind.Struct
            && !type.IsAbstract
            && !type.IsStatic
            && !type.IsImplicitlyDeclared
            && !GenericShape.HasOpenTypeParameters(type);
    }

    private static HashSet<IAssemblySymbol> CollectReachableAssemblies(IAssemblySymbol root)
    {
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<IAssemblySymbol>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var assembly = pending.Pop();
            if (!visited.Add(assembly))
            {
                continue;
            }

            foreach (var module in assembly.Modules)
            {
                foreach (var reference in module.ReferencedAssemblySymbols)
                {
                    pending.Push(reference);
                }
            }
        }

        return visited;
    }

    private static bool DependsOnAny(
        IAssemblySymbol assembly,
        HashSet<IAssemblySymbol> targets,
        Dictionary<IAssemblySymbol, bool> memo)
    {
        if (targets.Contains(assembly))
        {
            return true;
        }

        if (memo.TryGetValue(assembly, out var cached))
        {
            return cached;
        }

        // Set false first to avoid infinite recursion on circular references.
        memo[assembly] = false;
        foreach (var module in assembly.Modules)
        {
            foreach (var reference in module.ReferencedAssemblySymbols)
            {
                if (DependsOnAny(reference, targets, memo))
                {
                    memo[assembly] = true;
                    return true;
                }
            }
        }

        return false;
    }
}
