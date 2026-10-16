using Luces.DI.Generators.Analysis;
using Luces.DI.Generators.Configuration;
using Luces.DI.Generators.Diagnostics;
using Luces.DI.Generators.Discovery;
using Luces.DI.Generators.Emission;
using Luces.DI.Generators.Registration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Text;

namespace Luces.DI.Generators;

/// <summary>
/// The compile-time DI registration generator for Luces.DI: it scans <c>[Register*]</c> and <c>IAuto*</c> targets, then validates them and generates <c>GeneratedDiRegistrations.AddGeneratedRegistrations</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class DiRegistrationGenerator : IIncrementalGenerator
{
    private const string DefinitionsHintName = "DiRegistration.Definitions.g.cs";
    private const string RegistrationHintName = "GeneratedDiRegistrations.g.cs";

    private const string SingleResolvePropertyName = "AutoDiSingleResolveServices";
    private const string NamespacePropertyName = "DiRegistrationNamespace";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var configuration = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
                GeneratorOptions.Read(provider.GlobalOptions));

        // Exactly one namespace receives the definitions, and the configuration decides which one: the default namespace
        // when nothing is configured, otherwise the configured one, which then replaces the default instead of adding a copy.
        // The compilation is needed here because a name the consumer itself declares in that namespace makes the definitions
        // unusable, and such a conflict is only visible in the compilation.
        context.RegisterSourceOutput(
            configuration.Combine(context.CompilationProvider),
            static (productionContext, value) =>
            {
                var (options, compilation) = value;

                foreach (var invalid in options.InvalidValues)
                {
                    productionContext.ReportDiagnostic(Diagnostic.Create(
                        GeneratorDiagnostics.InvalidGeneratorConfiguration,
                        Location.None,
                        SingleResolvePropertyName,
                        invalid));
                }

                if (!GeneratorOptions.IsValidNamespace(options.DefinitionNamespace))
                {
                    productionContext.ReportDiagnostic(Diagnostic.Create(
                        GeneratorDiagnostics.InvalidGeneratorConfiguration,
                        Location.None,
                        NamespacePropertyName,
                        options.DefinitionNamespace));
                    return;
                }

                var conflicts = DefinitionSymbols.FindConflicts(compilation, options.DefinitionNamespace);
                if (!conflicts.IsEmpty)
                {
                    productionContext.ReportDiagnostic(Diagnostic.Create(
                        GeneratorDiagnostics.DefinitionNamespaceConflict,
                        Location.None,
                        options.DefinitionNamespace,
                        string.Join(", ", conflicts)));
                    return;
                }

                productionContext.AddSource(
                    DefinitionsHintName,
                    SourceText.From(
                        RegistrationEmitter.BuildDefinitions(options.DefinitionNamespace),
                        Encoding.UTF8));
            });

        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => CandidateSyntaxFilter.IsCandidate(node),
                transform: static (syntaxContext, cancellationToken) =>
                    CandidateFactory.Create(syntaxContext, cancellationToken))
            .Where(static candidate => candidate.Symbol is not null)
            .Collect();

        // Binding, collecting, analysis, and emission happen only once candidates, configuration, and the compilation are all available.
        var input = candidates
            .Combine(configuration)
            .Combine(context.CompilationProvider);

        context.RegisterSourceOutput(input, static (productionContext, value) =>
        {
            var ((foundCandidates, options), compilation) = value;

            // The definitions output has already reported why the definitions are unusable in these cases, and without
            // definitions nothing can be bound, so no registration is produced either.
            if (!GeneratorOptions.IsValidNamespace(options.DefinitionNamespace)
                || !DefinitionSymbols.FindConflicts(compilation, options.DefinitionNamespace).IsEmpty)
            {
                return;
            }

            var binding = CandidateBinder.Bind(
                compilation,
                foundCandidates,
                options.DefinitionNamespace);

            var registrations = RegistrationCollector.Collect(
                productionContext,
                binding.Compilation,
                binding.Candidates,
                binding.Symbols);

            SingleResolveAnalysis.Validate(
                productionContext,
                binding.Compilation,
                options.SingleResolveServices,
                registrations,
                binding.Candidates);

            productionContext.AddSource(
                RegistrationHintName,
                SourceText.From(
                    RegistrationEmitter.Emit(binding.Compilation, registrations),
                    Encoding.UTF8));
        });
    }
}
