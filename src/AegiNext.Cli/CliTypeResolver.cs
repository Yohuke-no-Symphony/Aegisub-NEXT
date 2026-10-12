using Spectre.Console.Cli;

namespace AegiNext.Cli;

internal sealed class CliTypeResolver(IReadOnlyDictionary<Type, List<Type>> implementations,
    IReadOnlyDictionary<Type, List<Func<object>>> factories) : ITypeResolver
{
    public object? Resolve(Type? type)
    {
        if (type is null)
        {
            return null;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var elementType = type.GetGenericArguments()[0];
            var values = ResolveAll(elementType).ToArray();
            var array = Array.CreateInstance(elementType, values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                array.SetValue(values[index], index);
            }
            return array;
        }
        if (factories.TryGetValue(type, out var valuesForType))
        {
            return valuesForType[^1]();
        }
        return Create(implementations.TryGetValue(type, out var types) ? types[^1] : type);
    }

    private IEnumerable<object> ResolveAll(Type type)
    {
        if (implementations.TryGetValue(type, out var types))
        {
            foreach (var implementation in types)
            {
                yield return Create(implementation) ?? throw new InvalidOperationException($"无法解析命令服务：{implementation.Name}");
            }
        }
        if (factories.TryGetValue(type, out var values))
        {
            foreach (var factory in values)
            {
                yield return factory();
            }
        }
    }

    private object? Create(Type implementation)
    {
        var constructors = implementation.GetConstructors();
        if (constructors.Length == 0)
        {
            return null;
        }
        var constructor = constructors.MaxBy(candidate => candidate.GetParameters().Length)!;
        var parameters = constructor.GetParameters().Select(parameter => Resolve(parameter.ParameterType)
            ?? throw new InvalidOperationException($"无法解析命令依赖：{parameter.ParameterType.Name}")).ToArray();
        return constructor.Invoke(parameters);
    }
}
