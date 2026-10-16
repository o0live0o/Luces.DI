# Luces.DI

面向 `Microsoft.Extensions.DependencyInjection` 的编译期注册方案，基于 Roslyn 增量生成器实现。
运行时不做任何扫描：程序集中的每一次 `AddGeneratedRegistrations` 调用，都是一份由源码直接生成
的、平铺的 `Add*` 调用列表。

## 注册服务

给具体的类或 record 标注生命周期特性即可：

```csharp
using Luces.DI;

public interface IClock;

[RegisterSingleton]
public sealed class SystemClock : IClock;
```

服务类型会被推断为该实现类所实现的、最具体的唯一非标记接口。若某接口被前面那个契约继承，
则不会被计入候选。没有实现任何服务接口的类型，会以自身类型注册。当实现了多个互不相关的接口时，
需要显式列出想要注册的服务：

```csharp
[RegisterScoped(typeof(IReader))]
[RegisterScoped(typeof(IWriter))]
public sealed class Store : IReader, IWriter;
```

可用特性包括 `RegisterScopedAttribute`、`RegisterSingletonAttribute`、`RegisterTransientAttribute`。
标记接口 `IAutoScoped`、`IAutoSingleton`、`IAutoTransient` 与之等价。匹配依据是配置命名空间内的
符号标识，因此仅同名的不相关类型绝不会被误选。

键控注册（keyed registration）接受任意编译期字符串常量：

```csharp
internal static class ServiceKeys
{
    public const string Primary = "primary";
}

[RegisterScoped(typeof(IStore), Key = ServiceKeys.Primary)]
public sealed class PrimaryStore : IStore;
```

随后在程序集的组合根（composition root）中接上即可：

```csharp
services.AddGeneratedRegistrations();
```

扩展方法所在的命名空间由使用方的程序集名派生而来：`[A-Za-z0-9._]` 之外的每个字符都会被替换为 `_`，
以点分隔的每一段都会成为合法标识符（前导数字会补上 `_` 前缀，保留字会用 `@` 转义），
若程序集名最终得不到任何段，则回退为 `GeneratedAssembly`。

## 泛型与嵌套

开放泛型实现会使用 DI 中非泛型的重载：

```csharp
[RegisterScoped]
public sealed class Repository<TEntity> : IRepository<TEntity>;

// 生成结果：
services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
```

服务类型与实现类型必须具备相同的元数（arity），并按相同顺序列出各自的类型参数。封闭泛型照常工作。
若开放泛型嵌套在同样是开放泛型的包含类型中，则会被拒绝：内置容器无法安全地表达这种参数映射。

可访问的嵌套类与 record 均受支持。为 record 合成的相等性接口从不参与推断。私有、受保护、
私有受保护以及 file-local 的实现类型或服务类型会被报告并跳过，因为生成的顶层扩展方法无法引用它们。

## 配置项

两个配置项都通过 `buildTransitive/Luces.DI.props` 传达，因此 NuGet 使用者无需额外操作即可生效。
若以分析器形式直接 `ProjectReference` 引用本项目，则不会导入包的 build 资源，此时需要自行添加
`CompilerVisibleProperty` 项。

### 定义命名空间

这些特性与标记接口只生成到一个命名空间：默认是 `Luces.DI`，也可由你指定。配置它表示**替换**默认命名空间 ——
不再生成 `Luces.DI` 那一份，此后必须从配置的命名空间使用这些特性与标记接口（混用两套无法编译）。你自己的代码
若位于该命名空间内，不得占用生成类型的名称；这类冲突会报告 `DIREG009`，并且不生成任何定义与注册。它适合用来
把特性纳入团队自有命名空间，或用于规避你自己在 `Luces.DI` 内的声明：

```xml
<PropertyGroup>
    <DiRegistrationNamespace>Contoso.DependencyInjection</DiRegistrationNamespace>
</PropertyGroup>

<ItemGroup>
    <CompilerVisibleProperty Include="DiRegistrationNamespace" />
</ItemGroup>
```

### 单解析完备性（single-resolve）

某些契约要求每个封闭服务类型必须恰好只有一个实现。通过列出完全限定的元数据名称（并显式标注泛型
元数）来启用该检查：

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

非泛型接口可省略后缀，或写作 `:0`。对于每个配置的契约，生成器会报告两类问题：某个具体的源码实现
最终没有对应的已生成、非键控（unkeyed）注册；以及在可见的程序集图中，某个封闭服务出现了多个具体实现。

该检查与具体框架无关。`Luces.DI` 不引用任何 mediator、command bus 或 handler 库；由使用方项目自行决定
要强制约束哪些契约。

## 诊断

错误以 `Luces.SourceGen.DI` 为分类上报。

| ID | 含义 |
| --- | --- |
| `DIREG001` | 同一个实现声明了相互冲突的生命周期。 |
| `DIREG002` | 配置的单解析服务存在多个实现。 |
| `DIREG003` | 实现类型或服务类型对生成的代码不可访问。 |
| `DIREG004` | 开放泛型服务无法被安全映射。 |
| `DIREG005` | 由于实现了多个接口，服务推断存在歧义。 |
| `DIREG006` | 配置的单解析实现缺少非键控的已生成注册。 |
| `DIREG007` | 某个 MSBuild 生成器配置项格式非法。 |
| `DIREG008` | 显式指定的服务类型无法由其实现类型赋值。 |
| `DIREG009` | 定义命名空间内已存在与生成类型同名的声明。 |

## 原生 AOT 与打包

生成的代码只包含直接的类型引用与内置的 DI API，因此不涉及反射标注或运行时发现。分析器以
`netstandard2.0` 为目标，并标记为不可发布（non-publishable）。以项目引用方式直接引用分析器时，
请让应用的发布属性与生成器构建隔离：

```xml
<ProjectReference Include="..\Luces.DI\Luces.DI.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false"
                  GlobalPropertiesToRemove="PublishAot;PublishTrimmed;TrimMode;RuntimeIdentifier;SelfContained" />
```

包会将生成器置于 `analyzers/dotnet/cs`，并通过 `buildTransitive/Luces.DI.props` 提供其 MSBuild 配置项。
