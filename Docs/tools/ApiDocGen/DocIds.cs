using System.Reflection;

namespace VirtualMarina.Docs.ApiDocGen;

/// <summary>
/// The documentation ids the C# compiler writes into the XML documentation file ("M:Namespace.Type.Method(System.Int32)"),
/// computed from reflection so a member can be looked up by what it is rather than by name.
/// </summary>
internal static class DocIds
{
    /// <summary>
    /// Every member a type declares itself. The reference documents protected members too, which is what the
    /// non-public flag is for: this reads metadata to describe it, and never invokes what it finds.
    /// </summary>
#pragma warning disable S3011 // Reflection over non-public members: needed to see protected members, which are published API.
    public const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>The compiler-generated members that give a record away (EqualityContract, PrintMembers).</summary>
    public const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
#pragma warning restore S3011

    /// <summary>The documentation id of a type or member.</summary>
    public static string Of(MemberInfo member) => member switch
    {
        Type type => "T:" + TypeId(type),
        FieldInfo field => "F:" + TypeId(field.DeclaringType!) + "." + field.Name,
        PropertyInfo property => "P:" + TypeId(property.DeclaringType!) + "." + MemberName(property.Name) + IndexIds(property.GetIndexParameters()),
        EventInfo ev => "E:" + TypeId(ev.DeclaringType!) + "." + MemberName(ev.Name),
        ConstructorInfo ctor => "M:" + TypeId(ctor.DeclaringType!) + (ctor.IsStatic ? ".#cctor" : ".#ctor") + ParameterIds(ctor),
        MethodInfo method => "M:" + TypeId(method.DeclaringType!) + "." + MemberName(method.Name)
            + (method.IsGenericMethodDefinition ? "``" + method.GetGenericArguments().Length : "")
            + ParameterIds(method)
            + (method.Name is "op_Implicit" or "op_Explicit" ? "~" + TypeIdOf(method.ReturnType) : ""),
        _ => throw new ArgumentException($"No documentation id for a {member.MemberType}.", nameof(member)),
    };

    /// <summary>A type's name as it appears in an id: namespace-qualified, nested types joined by a dot.</summary>
    public static string TypeId(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    // An explicit interface implementation is named after the interface ("IBar.Method"); the id writes those dots as #.
    private static string MemberName(string name) => name.Replace('.', '#');

    private static string ParameterIds(MethodBase method) => IndexIds(method.GetParameters());

    private static string IndexIds(ParameterInfo[] parameters) =>
        parameters.Length == 0 ? "" : "(" + string.Join(",", parameters.Select(p => TypeIdOf(p.ParameterType))) + ")";

    private static string TypeIdOf(Type type)
    {
        if (type.IsByRef) return TypeIdOf(type.GetElementType()!) + "@";
        if (type.IsArray) return TypeIdOf(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter) return type.DeclaringMethod is not null ? "``" + type.GenericParameterPosition : "`" + type.GenericParameterPosition;
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var baseName = TypeId(definition);
            baseName = baseName[..baseName.IndexOf('`', StringComparison.Ordinal)];
            return baseName + "{" + string.Join(",", type.GetGenericArguments().Select(TypeIdOf)) + "}";
        }

        return TypeId(type);
    }
}
