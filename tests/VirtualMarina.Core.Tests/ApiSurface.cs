using System.Reflection;
using System.Text;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Writes an assembly's public surface out as text, so a checked-in baseline can be compared against it and
/// any change to the published API has to be approved deliberately rather than slipping into a release.
/// </summary>
/// <remarks>
/// The text is everything a host application can see and bind to: public and protected members of public types,
/// with their signatures, the numeric value of every enum member, and the base type and interfaces of each type.
/// Documentation, parameter names of nothing but delegates, attributes and member order are left out, because
/// changing those breaks nobody.
/// </remarks>
internal static class ApiSurface
{
    private const BindingFlags Members =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>The public surface of an assembly, as sorted lines of text.</summary>
    public static string Of(Assembly assembly)
    {
        var text = new StringBuilder();
        var types = assembly.GetExportedTypes().OrderBy(Name, StringComparer.Ordinal);

        foreach (var type in types)
        {
            text.Append(Describe(type)).Append('\n');
            foreach (var line in MembersOf(type).OrderBy(line => line, StringComparer.Ordinal))
            {
                text.Append("    ").Append(line).Append('\n');
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static IEnumerable<string> MembersOf(Type type)
    {
        if (type.IsEnum)
        {
            // The numbers matter: they are what a host application stores in its own database.
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                yield return $"{field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture)}";
            }

            yield break;
        }

        foreach (var member in type.GetMembers(Members))
        {
            if (!IsVisible(member)) continue;
            if (Describe(member) is { } line) yield return line;
        }
    }

    /// <summary>True for members a host application can reach: public, or protected on a type it can derive from.</summary>
    private static bool IsVisible(MemberInfo member) => member switch
    {
        FieldInfo f => f.IsPublic || (f.IsFamily || f.IsFamilyOrAssembly) && !f.DeclaringType!.IsSealed,
        MethodBase m => m.IsPublic || (m.IsFamily || m.IsFamilyOrAssembly) && !m.DeclaringType!.IsSealed,
        PropertyInfo p => (p.GetMethod is { } g && IsVisible(g)) || (p.SetMethod is { } s && IsVisible(s)),
        EventInfo e => e.AddMethod is { } a && IsVisible(a),
        Type t => t.IsNestedPublic || (t.IsNestedFamily && !t.DeclaringType!.IsSealed),
        _ => false,
    };

    private static string? Describe(MemberInfo member) => member switch
    {
        // Backing members of properties and events appear as methods too; the property or event line covers them.
        MethodInfo { IsSpecialName: true } method when method.Name.StartsWith("get_", StringComparison.Ordinal)
            || method.Name.StartsWith("set_", StringComparison.Ordinal)
            || method.Name.StartsWith("add_", StringComparison.Ordinal)
            || method.Name.StartsWith("remove_", StringComparison.Ordinal) => null,

        // The compiler's record clone helper: real, but unspeakable from C# and never bound to by a consumer.
        MethodInfo { Name: "<Clone>$" } => null,

        ConstructorInfo ctor => $".ctor({Parameters(ctor)})",
        MethodInfo method => $"{Modifiers(method)}{Name(method.ReturnType)} {method.Name}{Generics(method)}({Parameters(method)})",
        PropertyInfo property => $"{((property.GetMethod ?? property.SetMethod)!.IsStatic ? "static " : string.Empty)}{Name(property.PropertyType)} {property.Name} {{ {Accessors(property)}}}",
        EventInfo ev => $"event {Name(ev.EventHandlerType!)} {ev.Name}",
        // A const is compiled into the consumer's own assembly, so its value is part of the promise too.
        FieldInfo { IsLiteral: true } field => $"const {Name(field.FieldType)} {field.Name} = {Literal(field.GetRawConstantValue())}",
        FieldInfo field => $"{(field.IsStatic ? "static " : string.Empty)}{Name(field.FieldType)} {field.Name}",
        Type nested => $"nested {Name(nested)}",
        _ => null,
    };

    private static string Modifiers(MethodInfo method) =>
        method.IsStatic ? "static " : method.IsAbstract ? "abstract " : method.IsVirtual && !method.IsFinal ? "virtual " : string.Empty;

    private static string Accessors(PropertyInfo property)
    {
        var text = new StringBuilder();
        if (property.GetMethod is { } get && IsVisible(get)) text.Append("get; ");
        if (property.SetMethod is { } set && IsVisible(set))
        {
            // An init-only setter is not a normal setter: assigning after construction is a compile error.
            var isInit = set.ReturnParameter.GetRequiredCustomModifiers()
                .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
            text.Append(isInit ? "init; " : "set; ");
        }

        return text.ToString();
    }

    private static string Parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(p =>
            $"{(p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : string.Empty)}{Name(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = " + Literal(p.RawDefaultValue) : string.Empty)}"));

    private static string Generics(MethodInfo method) =>
        method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">" : string.Empty;

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "?",
    };

    private static string Describe(Type type)
    {
        var kind = type.IsEnum ? "enum"
            : type.IsInterface ? "interface"
            : type.IsValueType ? "struct"
            : IsStatic(type) ? "static class"
            : type.IsAbstract ? "abstract class"
            : type.IsSealed ? "sealed class"
            : "class";

        var bases = new List<string>();
        if (type is { IsClass: true, BaseType: { } baseType } && baseType != typeof(object)) bases.Add(Name(baseType));
        bases.AddRange(type.GetInterfaces().Where(i => i.IsPublic).Select(Name).OrderBy(n => n, StringComparer.Ordinal));

        return $"{kind} {Name(type)}{(bases.Count > 0 ? " : " + string.Join(", ", bases) : string.Empty)}";
    }

    private static bool IsStatic(Type type) => type is { IsAbstract: true, IsSealed: true };

    /// <summary>A readable, stable name: "IReadOnlyList&lt;Berth&gt;" rather than the reflection spelling.</summary>
    private static string Name(Type type)
    {
        if (type.IsByRef) return Name(type.GetElementType()!);
        if (type.IsArray) return Name(type.GetElementType()!) + "[]";
        if (Nullable.GetUnderlyingType(type) is { } underlying) return Name(underlying) + "?";

        var name = Aliases.TryGetValue(type, out var alias) ? alias : type.Name;
        if (type.IsGenericType)
        {
            name = name[..name.IndexOf('`')] + "<" + string.Join(", ", type.GetGenericArguments().Select(Name)) + ">";
        }

        var prefix = type.Namespace is { } ns && ns.StartsWith("VirtualMarina", StringComparison.Ordinal) && !type.IsNested
            ? ns + "."
            : string.Empty;
        return prefix + name;
    }

    private static readonly Dictionary<Type, string> Aliases = new()
    {
        [typeof(void)] = "void", [typeof(bool)] = "bool", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte",
        [typeof(char)] = "char", [typeof(short)] = "short", [typeof(ushort)] = "ushort", [typeof(int)] = "int",
        [typeof(uint)] = "uint", [typeof(long)] = "long", [typeof(ulong)] = "ulong", [typeof(float)] = "float",
        [typeof(double)] = "double", [typeof(decimal)] = "decimal", [typeof(string)] = "string", [typeof(object)] = "object",
    };
}
