using Luces.DI.Generators.Analysis;
using Luces.DI.Generators.Diagnostics;
using Luces.DI.Generators.Discovery;
using Luces.DI.Generators.Symbols;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Luces.DI.Generators.Registration;

/// <summary>
/// Candidate to registration: recognizes <c>[Register*]</c> and <c>IAuto*</c>, infers the service type, validates reachability, assignability, and open generic shape per entry, and reports diagnostics in place. Finally deduplicates and sorts.
/// </summary>
internal static class RegistrationCollector
{
    public static ImmutableArray<RegisteredService> Collect(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<Candidate> candidates,
        DefinitionSymbols definitions)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var collected = ImmutableArray.CreateBuilder<RegisteredService>();

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

            var attributes = ReadAttributeRegistrations(context, implementation, definitions);
            var markerLifetimes = ReadMarkerLifetimes(implementation, definitions);
            if (attributes.IsDefaultOrEmpty && markerLifetimes.IsDefaultOrEmpty)
            {
                continue;
            }

            if (!TypeAccessibility.IsReferencableFromGeneratedCode(implementation))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GeneratorDiagnostics.InaccessibleRegistrationType,
                    candidate.Location,
                    implementation.GetQualifiedName()));
                continue;
            }

            // On a lifetime conflict the whole type produces no registration at all (a settled decision consistent with the other "error and skip" cases).
            if (HasConflictingLifetimes(
                context,
                candidate,
                implementation,
                attributes,
                markerLifetimes))
            {
                continue;
            }

            foreach (var attribute in attributes)
            {
                AddRegistration(
                    context,
                    compilation,
                    collected,
                    implementation,
                    attribute.ExplicitService,
                    definitions,
                    attribute.Lifetime,
                    attribute.Key,
                    attribute.Location);
            }

            foreach (var lifetime in markerLifetimes)
            {
                AddRegistration(
                    context,
                    compilation,
                    collected,
                    implementation,
                    explicitService: null,
                    definitions,
                    lifetime,
                    key: null,
                    candidate.Location);
            }
        }

        return DeduplicateAndSort(collected);
    }

    private static bool HasConflictingLifetimes(
        SourceProductionContext context,
        Candidate candidate,
        INamedTypeSymbol implementation,
        ImmutableArray<AttributeRegistration> attributes,
        ImmutableArray<ServiceLifetime> markerLifetimes)
    {
        var lifetimes = attributes
            .Select(attribute => attribute.Lifetime)
            .Concat(markerLifetimes)
            .Distinct()
            .OrderBy(lifetime => lifetime.GetMethodSuffix(), StringComparer.Ordinal)
            .ToArray();
        if (lifetimes.Length <= 1)
        {
            return false;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            GeneratorDiagnostics.LifetimeConflict,
            candidate.Location,
            implementation.GetQualifiedName(),
            string.Join(", ", lifetimes.Select(lifetime => lifetime.GetMethodSuffix()))));
        return true;
    }

    private static void AddRegistration(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<RegisteredService>.Builder collected,
        INamedTypeSymbol implementation,
        INamedTypeSymbol? explicitService,
        DefinitionSymbols definitions,
        ServiceLifetime lifetime,
        string? key,
        Location location)
    {
        var service = ResolveService(
            context,
            implementation,
            explicitService,
            definitions,
            location);
        if (service is null)
        {
            return;
        }

        if (!TypeAccessibility.IsReferencableFromGeneratedCode(service))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratorDiagnostics.InaccessibleRegistrationType,
                location,
                service.GetQualifiedName()));
            return;
        }

        if (explicitService is not null
            && !GenericShape.IsAssignableTo(compilation, implementation, service))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GeneratorDiagnostics.IncompatibleServiceType,
                location,
                implementation.GetQualifiedName(),
                service.GetQualifiedName()));
            return;
        }

        var implementationIsOpen = GenericShape.HasOpenTypeParameters(implementation);
        var serviceIsOpen = GenericShape.HasOpenTypeParameters(service)
            || service.IsUnboundGenericType;

        if (implementationIsOpen || serviceIsOpen)
        {
            if (!GenericShape.CanRegisterAsOpenGeneric(implementation, service))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GeneratorDiagnostics.UnsupportedOpenGenericRegistration,
                    location,
                    implementation.GetQualifiedName(),
                    service.GetQualifiedName()));
                return;
            }

            collected.Add(new RegisteredService(
                service,
                implementation,
                service.GetUnboundQualifiedName(),
                implementation.GetUnboundQualifiedName(),
                lifetime,
                key,
                IsOpenGeneric: true));
            return;
        }

        collected.Add(new RegisteredService(
            service,
            implementation,
            service.GetQualifiedName(),
            implementation.GetQualifiedName(),
            lifetime,
            key,
            IsOpenGeneric: false));
    }

    private static INamedTypeSymbol? ResolveService(
        SourceProductionContext context,
        INamedTypeSymbol implementation,
        INamedTypeSymbol? explicitService,
        DefinitionSymbols definitions,
        Location location)
    {
        if (explicitService is not null)
        {
            return explicitService;
        }

        var inference = ServiceInference.Infer(implementation, definitions);
        if (inference.Service is not null)
        {
            return inference.Service;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            GeneratorDiagnostics.AmbiguousServiceInference,
            location,
            implementation.GetQualifiedName(),
            string.Join(", ", inference.AmbiguousCandidates.Select(
                candidate => candidate.GetQualifiedName()))));
        return null;
    }

    private static ImmutableArray<AttributeRegistration> ReadAttributeRegistrations(
        SourceProductionContext context,
        INamedTypeSymbol implementation,
        DefinitionSymbols definitions)
    {
        var registrations = ImmutableArray.CreateBuilder<AttributeRegistration>();
        foreach (var attribute in implementation.GetAttributes())
        {
            var lifetime = MatchLifetime(attribute.AttributeClass, definitions);
            if (lifetime is null)
            {
                continue;
            }

            var location = attribute.ApplicationSyntaxReference
                ?.GetSyntax()
                .GetLocation()
                ?? implementation.Locations.FirstOrDefault()
                ?? Location.None;

            // The service-type slot exists on every [Register*] overload that takes a constructor argument, so it must always be honored: a null value or a non-named type such as typeof(int[]) is a user error, never "no service specified".
            INamedTypeSymbol? explicitService = null;
            if (attribute.ConstructorArguments.Length > 0)
            {
                var serviceArgument = attribute.ConstructorArguments[0];
                if (serviceArgument.Value is INamedTypeSymbol namedService)
                {
                    explicitService = namedService;
                }
                else
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        GeneratorDiagnostics.IncompatibleServiceType,
                        location,
                        implementation.GetQualifiedName(),
                        FormatServiceArgument(serviceArgument)));
                    continue;
                }
            }

            registrations.Add(new AttributeRegistration(
                lifetime.Value,
                explicitService,
                ReadKey(attribute),
                location));
        }

        return registrations.ToImmutable();
    }

    /// <summary>
    /// Display text for a service-type argument that did not resolve to a named type, e.g. <c>global::System.Int32[]</c> or <c>null</c>.
    /// </summary>
    private static string FormatServiceArgument(TypedConstant serviceArgument)
    {
        return serviceArgument.Value is ITypeSymbol typeSymbol
            ? typeSymbol.GetQualifiedName()
            : "null";
    }

    private static ImmutableArray<ServiceLifetime> ReadMarkerLifetimes(
        INamedTypeSymbol implementation,
        DefinitionSymbols definitions)
    {
        var lifetimes = ImmutableArray.CreateBuilder<ServiceLifetime>();
        foreach (var @interface in implementation.AllInterfaces)
        {
            var lifetime = MatchLifetime(@interface, definitions);
            if (lifetime is not null)
            {
                lifetimes.Add(lifetime.Value);
            }
        }

        return lifetimes.ToImmutable();
    }

    private static ServiceLifetime? MatchLifetime(
        INamedTypeSymbol? type,
        DefinitionSymbols definitions)
    {
        if (type is null)
        {
            return null;
        }

        if (SymbolEqualityComparer.Default.Equals(type, definitions.ScopedAttribute)
            || SymbolEqualityComparer.Default.Equals(type, definitions.ScopedMarker))
        {
            return ServiceLifetime.Scoped;
        }

        if (SymbolEqualityComparer.Default.Equals(type, definitions.SingletonAttribute)
            || SymbolEqualityComparer.Default.Equals(type, definitions.SingletonMarker))
        {
            return ServiceLifetime.Singleton;
        }

        if (SymbolEqualityComparer.Default.Equals(type, definitions.TransientAttribute)
            || SymbolEqualityComparer.Default.Equals(type, definitions.TransientMarker))
        {
            return ServiceLifetime.Transient;
        }

        return null;
    }

    private static string? ReadKey(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == "Key" && argument.Value.Value is string key)
            {
                // The key is kept verbatim: an empty string is a keyed registration whose key is "", not an unkeyed one.
                return key;
            }
        }

        return null;
    }

    private static ImmutableArray<RegisteredService> DeduplicateAndSort(
        ImmutableArray<RegisteredService>.Builder collected)
    {
        var unique = new Dictionary<string, RegisteredService>(StringComparer.Ordinal);
        foreach (var registration in collected)
        {
            unique[registration.GetIdentity()] = registration;
        }

        return unique.Values
            .OrderBy(registration => registration.ImplementationTypeName, StringComparer.Ordinal)
            .ThenBy(registration => registration.ServiceTypeName, StringComparer.Ordinal)
            .ThenBy(registration => registration.Lifetime.GetMethodSuffix(), StringComparer.Ordinal)
            // Keys order after the unkeyed registrations (null sorts first), which keeps keyed and unkeyed entries of the same service stable.
            .ThenBy(registration => registration.Key, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
