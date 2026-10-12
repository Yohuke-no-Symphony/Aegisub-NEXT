using Spectre.Console.Cli;

namespace AegiNext.Cli;

/// <summary>轻量命令注册器；命令依赖由实例和明确类型映射组成。</summary>
public sealed class CliTypeRegistrar : ITypeRegistrar
{
    private readonly Dictionary<Type, List<Type>> implementations = [];
    private readonly Dictionary<Type, List<Func<object>>> factories = [];

    /// <summary>登记服务对应的实现类型。</summary>
    public void Register(Type service, Type implementation)
    {
        if (!implementations.TryGetValue(service, out var types))
        {
            types = [];
            implementations.Add(service, types);
        }
        types.Add(implementation);
    }

    /// <summary>登记由宿主持有的服务实例。</summary>
    public void RegisterInstance(Type service, object implementation)
    {
        RegisterLazy(service, () => implementation);
    }

    /// <summary>登记延迟创建的服务。</summary>
    public void RegisterLazy(Type service, Func<object> factory)
    {
        if (!factories.TryGetValue(service, out var values))
        {
            values = [];
            factories.Add(service, values);
        }
        values.Add(factory);
    }

    /// <summary>为本次命令执行创建解析器。</summary>
    public ITypeResolver Build()
    {
        return new CliTypeResolver(implementations, factories);
    }
}
