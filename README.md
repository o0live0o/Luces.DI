# Luces.DI

Compile-time registration for `Microsoft.Extensions.DependencyInjection`, powered by an
incremental Roslyn generator. Nothing is scanned at runtime: every `AddGeneratedRegistrations`
call in an assembly is a straight-line list of `Add*` calls produced from source.

## Registering services

Annotate a concrete class or record with a lifetime attribute:

```csharp
using Luces.DI;

public interface IClock;

[RegisterSingleton]
public sealed class SystemClock : IClock;
```

The service type is inferred as the single most-specific non-marker interface. Interfaces that are
inherited by that contract are not counted as rival candidates. A type that implements no service
interface is registered as itself. When several unrelated interfaces are implemented, name each
service you want:

```csharp
[RegisterScoped(typeof(IReader))]
[RegisterScoped(typeof(IWriter))]
public sealed class Store : IReader, IWriter;
```

Available attributes: `RegisterScopedAttribute`, `RegisterSingletonAttribute`,
`RegisterTransientAttribute`. The marker interfaces `IAutoScoped`, `IAutoSingleton` and
`IAutoTransient` are equivalent. Matching is by symbol identity inside the configured namespace, so
an unrelated type that only shares a short name is never picked up.

Keyed registration takes any compile-time string constant:

```csharp
internal static class ServiceKeys
{
    public const string Primary = "primary";
}

[RegisterScoped(typeof(IStore), Key = ServiceKeys.Primary)]
public sealed class PrimaryStore : IStore;
```

Then wire it up from the assembly's composition root:

```csharp
services.AddGeneratedRegistrations();
```

The extension lives in a namespace derived from the consuming assembly name. Every character outside
`[A-Za-z0-9._]` is replaced by `_`, each dot-separated segment becomes a legal identifier (a leading
digit gains a `_` prefix, a reserved keyword is escaped with `@`), and an assembly name that yields
no segment at all falls back to `GeneratedAssembly`.

## Generics and nesting

Open generic implementations use the non-generic DI overloads:

```csharp
[RegisterScoped]
public sealed class Repository<TEntity> : IRepository<TEntity>;

// generated:
services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
```

Service and implementation must share the same arity and list their type parameters in the same
order. Closed generics work as usual. An open type nested inside an open generic containing type is
rejected: the built-in container cannot express that parameter mapping safely.

Accessible nested classes and records are supported. The equality interface synthesized for records
never takes part in inference. Private, protected, private-protected and file-local implementation or
service types are reported and skipped, because the generated top-level extension cannot name them.

## Options

Both options travel through `buildTransitive/Luces.DI.props`, so NuGet consumers get them for free.
A direct analyzer `ProjectReference` does not import package build assets, so add the
`CompilerVisibleProperty` items yourself in that case.

### Definition namespace

The attributes and markers are emitted into exactly one namespace: `Luces.DI` by default, or the one
you configure. Configuring it **replaces** the default namespace, the `Luces.DI` copy is not emitted
any more, so the attributes and markers must then be taken from the configured namespace — mixing the
two sets does not compile. Code of yours that lives in that namespace must not reuse a generated type
name; such a clash is reported as `DIREG009` and no definitions or registrations are produced. Use it
to keep a house namespace, or to avoid a `Luces.DI` declaration of your own:

```xml
<PropertyGroup>
    <DiRegistrationNamespace>Contoso.DependencyInjection</DiRegistrationNamespace>
</PropertyGroup>

<ItemGroup>
    <CompilerVisibleProperty Include="DiRegistrationNamespace" />
</ItemGroup>
```

### Single-resolve completeness

Some contracts must have exactly one implementation per closed service type. Opt in by listing the
fully qualified metadata names, with explicit generic arity:

```xml
<PropertyGroup>
    <AutoDiSingleResolveServices>
        Contoso.Commands.ICommandHandler:2;
        Contoso.Queries.IQueryHandler:2
    </AutoDiSingleResolveServices>
</PropertyGroup>

<ItemGroup>
    <CompilerVisibleProperty Include="AutoDiSingleResolveServices" />
</ItemGroup>
```

A non-generic interface omits the suffix or writes `:0`. For each configured contract the generator
reports a concrete source implementation that ends up without a generated, unkeyed registration, and
also reports several concrete implementations of one closed service across the visible assembly
graph.

The check is framework-neutral. `Luces.DI` references no mediator, command bus or handler library; the
consuming project decides which contracts to enforce.

## Diagnostics

Errors are reported in the `Luces.SourceGen.DI` category.

| ID | Meaning |
| --- | --- |
| `DIREG001` | One implementation declares conflicting lifetimes. |
| `DIREG002` | A configured single-resolve service has multiple implementations. |
| `DIREG003` | The implementation or service type is inaccessible to generated code. |
| `DIREG004` | An open generic service cannot be mapped safely. |
| `DIREG005` | Service inference is ambiguous because multiple interfaces are implemented. |
| `DIREG006` | A configured single-resolve implementation lacks an unkeyed generated registration. |
| `DIREG007` | An MSBuild generator option is malformed. |
| `DIREG008` | An explicit service type is not assignable from its implementation. |
| `DIREG009` | The definition namespace already declares a generated type name. |

## Native AOT and packaging

The generated code contains only direct type references and built-in DI APIs, so no reflection
annotations or runtime discovery are involved. The analyzer targets `netstandard2.0` and is marked
non-publishable. When referencing the analyzer project directly, keep the app's publish properties
away from the generator build:

```xml
<ProjectReference Include="..\Luces.DI\Luces.DI.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false"
                  GlobalPropertiesToRemove="PublishAot;PublishTrimmed;TrimMode;RuntimeIdentifier;SelfContained" />
```

The package ships the generator under `analyzers/dotnet/cs` and its MSBuild options through
`buildTransitive/Luces.DI.props`.
