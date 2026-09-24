using System.Globalization;
using System.Reflection;
using System.Text;

namespace VirtualMarina.TestSupport;

/// <summary>
/// Writes an assembly's public surface out as text, so a checked-in baseline can be compared against it and
/// any change to the published API has to be approved deliberately rather than slipping into a release.
/// </summary>
/// <remarks>
/// <para>
/// The text is everything a host application can see and bind to: public and protected members of public types,
/// with their signatures, the numeric value of every enum member, and the base type and interfaces of each type.
/// A signature carries what the compiler holds a caller to: accessibility (protected is written out), nullability
/// (<c>string</c> against <c>string?</c>), static/abstract/virtual/override/sealed on methods, properties and
/// events, <c>readonly</c> fields, <c>params</c>/<c>in</c>/<c>ref</c>/<c>out</c> parameters, <c>required</c>
/// members, generic constraints, and whether a type is a record, a readonly struct or a ref struct.
/// Documentation, attributes other than those, and member order are left out, because changing those breaks nobody.
/// </para>
/// <para>
/// Everything is read through metadata (attribute names, never attribute instances), so the same text comes out of
/// an assembly loaded for execution and one loaded into a <c>MetadataLoadContext</c>. That is how the Windows-only
/// WinForms baseline can be produced on any OS.
/// </para>
/// </remarks>
public static class ApiSurface
{
    private const BindingFlags Members =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private const string CompilerServices = "System.Runtime.CompilerServices.";

    /// <summary>The public surface of an assembly, as sorted lines of text.</summary>
    public static string Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var text = new StringBuilder();
        var types = assembly.GetExportedTypes()
            // The Razor SDK compiles _Imports.razor into a public class of that name. Nobody binds to it, and its
            // shape depends on the Razor compiler that built it, so a baseline that recorded it would change with
            // the SDK rather than with the library.
            .Where(type => type.Name != "_Imports")
            .OrderBy(type => Name(type), StringComparer.Ordinal);

        var nullability = new NullabilityInfoContext();
        foreach (var type in types)
        {
            text.Append(Describe(type)).Append('\n');
            foreach (var line in MembersOf(type, nullability).OrderBy(line => line, StringComparer.Ordinal))
            {
                text.Append("    ").Append(line).Append('\n');
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static IEnumerable<string> MembersOf(Type type, NullabilityInfoContext nullability)
    {
        if (type.IsEnum)
        {
            // The numbers matter: they are what a host application stores in its own database.
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                yield return $"{field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)}";
            }

            yield break;
        }

        foreach (var member in type.GetMembers(Members))
        {
            if (!IsVisible(member)) continue;
            if (Describe(member, nullability) is { } line) yield return line;
        }
    }

    /// <summary>True for members a host application can reach: public, or protected on a type it can derive from.</summary>
    private static bool IsVisible(MemberInfo member) => member switch
    {
        FieldInfo f => f.IsPublic || (f.IsFamily || f.IsFamilyOrAssembly) && !f.DeclaringType!.IsSealed,
        MethodBase m => m.IsPublic || (m.IsFamily || m.IsFamilyOrAssembly) && !m.DeclaringType!.IsSealed,
        PropertyInfo p => (p.GetMethod is { } g && IsVisible(g)) || (p.SetMethod is { } s && IsVisible(s)),
        EventInfo e => e.AddMethod is { } a && IsVisible(a),
        Type t => t.IsNestedPublic || (t.IsNestedFamily || t.IsNestedFamORAssem) && !t.DeclaringType!.IsSealed,
        _ => false,
    };

    private static bool IsProtected(MemberInfo member) => member switch
    {
        FieldInfo f => !f.IsPublic,
        MethodBase m => !m.IsPublic,
        Type t => !t.IsNestedPublic,
        _ => false,
    };

    private static string? Describe(MemberInfo member, NullabilityInfoContext nullability) => member switch
    {
        // Backing members of properties and events appear as methods too; the property or event line covers them.
        MethodInfo { IsSpecialName: true } method when method.Name.StartsWith("get_", StringComparison.Ordinal)
            || method.Name.StartsWith("set_", StringComparison.Ordinal)
            || method.Name.StartsWith("add_", StringComparison.Ordinal)
            || method.Name.StartsWith("remove_", StringComparison.Ordinal) => null,

        // The compiler's record clone helper: real, but unspeakable from C# and never bound to by a consumer.
        MethodInfo { Name: "<Clone>$" } => null,

        ConstructorInfo ctor => $"{Access(ctor)}{(ctor.IsStatic ? "static " : string.Empty)}.ctor({Parameters(ctor, nullability)})",
        MethodInfo method =>
            $"{Access(method)}{Modifiers(method)}{Returns(method, nullability)} {method.Name}{Generics(method)}({Parameters(method, nullability)}){Constraints(method.IsGenericMethodDefinition ? method.GetGenericArguments() : [])}",
        PropertyInfo property =>
            $"{Access(property)}{Required(property)}{Modifiers((property.GetMethod ?? property.SetMethod)!)}{Name(property.PropertyType, nullability.Create(property), property.GetMethod is null)} {PropertyName(property)} {{ {Accessors(property)}}}",
        EventInfo ev => $"{Access(ev)}{Modifiers(ev.AddMethod!)}event {Name(ev.EventHandlerType!, nullability.Create(ev))} {ev.Name}",
        // A const is compiled into the consumer's own assembly, so its value is part of the promise too.
        FieldInfo { IsLiteral: true } field => $"{Access(field)}const {Name(field.FieldType)} {field.Name} = {Literal(field.GetRawConstantValue())}",
        FieldInfo field =>
            $"{Access(field)}{Required(field)}{(field.IsStatic ? "static " : string.Empty)}{(field.IsInitOnly ? "readonly " : string.Empty)}{Name(field.FieldType, nullability.Create(field))} {field.Name}",
        Type nested => $"{Access(nested)}nested {Name(nested)}",
        _ => null,
    };

    private const string Protected = "protected ";

    /// <summary>"protected " for a member a derived class can reach but a caller cannot; nothing for public ones.</summary>
    private static string Access(MemberInfo member) => member switch
    {
        // A property is as visible as its most visible accessor; a less visible one is marked on the accessor.
        PropertyInfo p => p.GetMethod is { IsPublic: true } || p.SetMethod is { IsPublic: true } ? string.Empty : Protected,
        EventInfo e => IsProtected(e.AddMethod!) ? Protected : string.Empty,
        _ => IsProtected(member) ? Protected : string.Empty,
    };

    private static string Modifiers(MethodInfo method)
    {
        if (method.IsStatic) return method.IsAbstract ? "static abstract " : method.IsVirtual ? "static virtual " : "static ";
        if (method.DeclaringType!.IsInterface) return string.Empty;
        if (method.IsAbstract) return IsOverride(method) ? "abstract override " : "abstract ";
        if (!method.IsVirtual) return string.Empty;
        if (IsOverride(method)) return method.IsFinal ? "sealed override " : "override ";

        // Virtual and final without overriding anything is an interface implementation the compiler had to make
        // virtual; to C# it is an ordinary method.
        return method.IsFinal ? string.Empty : "virtual ";
    }

    /// <summary>
    /// A virtual method that reuses its base's vtable slot overrides it; one with a new slot starts a chain (or
    /// implements an interface). Read from the method's flags because MetadataLoadContext has no GetBaseDefinition.
    /// </summary>
    private static bool IsOverride(MethodInfo method) =>
        method.IsVirtual && (method.Attributes & MethodAttributes.VtableLayoutMask) == MethodAttributes.ReuseSlot;

    private static string Required(MemberInfo member) =>
        HasAttribute(member.GetCustomAttributesData(), CompilerServices + "RequiredMemberAttribute") ? "required " : string.Empty;

    /// <summary>Indexers are written with their parameters, since the name alone ("Item") does not identify them.</summary>
    private static string PropertyName(PropertyInfo property)
    {
        var index = property.GetIndexParameters();
        return index.Length == 0
            ? property.Name
            : $"this[{string.Join(", ", index.Select(p => $"{Name(p.ParameterType)} {p.Name}"))}]";
    }

    private static string Accessors(PropertyInfo property)
    {
        var text = new StringBuilder();
        var propertyIsProtected = Access(property).Length > 0;
        if (property.GetMethod is { } get && IsVisible(get))
        {
            text.Append(IsProtected(get) && !propertyIsProtected ? "protected get; " : "get; ");
        }

        if (property.SetMethod is { } set && IsVisible(set))
        {
            if (IsProtected(set) && !propertyIsProtected) text.Append(Protected);

            // An init-only setter is not a normal setter: assigning after construction is a compile error.
            var isInit = set.ReturnParameter.GetRequiredCustomModifiers()
                .Any(m => m.FullName == CompilerServices + "IsExternalInit");
            text.Append(isInit ? "init; " : "set; ");
        }

        return text.ToString();
    }

    private static string Returns(MethodInfo method, NullabilityInfoContext nullability)
    {
        var returnType = method.ReturnParameter.ParameterType;
        var byRef = returnType.IsByRef
            ? HasAttribute(method.ReturnParameter.GetCustomAttributesData(), CompilerServices + "IsReadOnlyAttribute") ? "ref readonly " : "ref "
            : string.Empty;
        return byRef + Name(returnType, nullability.Create(method.ReturnParameter));
    }

    private static string Parameters(MethodBase method, NullabilityInfoContext nullability) =>
        string.Join(", ", method.GetParameters().Select(p => Parameter(p, nullability)));

    private static string Parameter(ParameterInfo parameter, NullabilityInfoContext nullability)
    {
        var attributes = parameter.GetCustomAttributesData();
        var modifier = parameter.IsOut ? "out "
            : parameter.ParameterType.IsByRef && parameter.IsIn ? "in "
            : parameter.ParameterType.IsByRef ? "ref "
            : HasAttribute(attributes, "System.ParamArrayAttribute") ? "params "
            : string.Empty;

        // What a caller may pass is the write state; for an out parameter it is what the caller gets back.
        var info = nullability.Create(parameter);
        var name = Name(parameter.ParameterType, info, write: !parameter.IsOut);
        var defaultValue = parameter.HasDefaultValue ? " = " + Literal(parameter.RawDefaultValue) : string.Empty;
        return $"{modifier}{name} {parameter.Name}{defaultValue}";
    }

    /// <summary>
    /// The state that matters to the caller: what it may assign (write) for an input, what it gets back (read)
    /// otherwise. They differ only under attributes such as [AllowNull] or [MaybeNull].
    /// </summary>
    private static NullabilityState State(NullabilityInfo info, bool write) =>
        write && info.WriteState != NullabilityState.Unknown ? info.WriteState : info.ReadState;

    private static string Generics(MethodInfo method) =>
        method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">" : string.Empty;

    /// <summary>The where-clauses of a generic type or method, in declaration order.</summary>
    private static string Constraints(Type[] parameters)
    {
        var clauses = new List<string>();
        foreach (var parameter in parameters)
        {
            var constraints = new List<string>();
            var flags = parameter.GenericParameterAttributes;
            var isUnmanaged = HasAttribute(parameter.GetCustomAttributesData(), CompilerServices + "IsUnmanagedAttribute");
            if ((flags & GenericParameterAttributes.ReferenceTypeConstraint) != 0) constraints.Add("class");
            if ((flags & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0) constraints.Add(isUnmanaged ? "unmanaged" : "struct");
            constraints.AddRange(parameter.GetGenericParameterConstraints()
                .Where(c => c.FullName != "System.ValueType")
                .Select(c => Name(c))
                .OrderBy(n => n, StringComparer.Ordinal));
            if ((flags & GenericParameterAttributes.DefaultConstructorConstraint) != 0
                && (flags & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
            {
                constraints.Add("new()");
            }

            if (constraints.Count > 0) clauses.Add($" where {parameter.Name} : {string.Join(", ", constraints)}");
        }

        return string.Concat(clauses);
    }

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?",
    };

    private static string Describe(Type type)
    {
        var kind = Kind(type);

        var bases = new List<string>();
        if (type is { IsClass: true, BaseType: { } baseType } && baseType.FullName != "System.Object") bases.Add(Name(baseType));
        bases.AddRange(type.GetInterfaces().Where(i => i.IsPublic).Select(i => Name(i)).OrderBy(n => n, StringComparer.Ordinal));

        var constraints = type.IsGenericTypeDefinition ? Constraints(type.GetGenericArguments()) : string.Empty;
        return $"{kind} {Name(type)}{(bases.Count > 0 ? " : " + string.Join(", ", bases) : string.Empty)}{constraints}";
    }

    /// <summary>What the type is declared as, the way C# writes it: "sealed record", "readonly struct", "static class".</summary>
    private static string Kind(Type type)
    {
        if (type.IsEnum) return "enum";
        if (type.IsInterface) return "interface";

        var isRecord = IsRecord(type);
        if (type.IsValueType) return StructKind(type, type.GetCustomAttributesData()) + (isRecord ? "record struct" : "struct");
        if (IsStatic(type)) return "static class";

        var noun = isRecord ? "record" : "class";
        if (type.IsAbstract) return "abstract " + noun;
        return type.IsSealed ? "sealed " + noun : noun;
    }

    private static string StructKind(Type type, IList<CustomAttributeData> attributes) =>
        (HasAttribute(attributes, CompilerServices + "IsReadOnlyAttribute") ? "readonly " : string.Empty)
        + (type.IsByRefLike ? "ref " : string.Empty);

    /// <summary>
    /// A record (class or struct) is recognised by the PrintMembers method the compiler generates for it, which an
    /// ordinary type does not have. Turning a record into a class, or back, changes its equality: a breaking change.
    /// </summary>
    private static bool IsRecord(Type type) =>
        type.GetMethods(Members).Any(m => m.Name == "PrintMembers"
            && m.GetParameters() is [{ ParameterType.FullName: "System.Text.StringBuilder" }]
            && HasAttribute(m.GetCustomAttributesData(), CompilerServices + "CompilerGeneratedAttribute"));

    private static bool IsStatic(Type type) => type is { IsAbstract: true, IsSealed: true };

    private static bool HasAttribute(IList<CustomAttributeData> attributes, string fullName) =>
        attributes.Any(a => a.AttributeType.FullName == fullName);

    /// <summary>A readable, stable name: "IReadOnlyList&lt;Berth?&gt;" rather than the reflection spelling.</summary>
    private static string Name(Type type, NullabilityInfo? nullability = null, bool write = false) =>
        Name(type, nullability, nullability is null ? null : State(nullability, write));

    private static string Name(Type type, NullabilityInfo? nullability, NullabilityState? state)
    {
        if (type.IsByRef) return Name(type.GetElementType()!, nullability, state);
        if (type.IsArray) return Name(type.GetElementType()!, nullability?.ElementType) + "[]" + Question(state);
        if (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "System.Nullable`1")
        {
            return Name(type.GetGenericArguments()[0]) + "?";
        }

        var name = Aliases.TryGetValue(type.FullName ?? string.Empty, out var alias) ? alias : type.Name;
        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            var argumentNullability = nullability?.GenericTypeArguments;
            name = name[..name.IndexOf('`', StringComparison.Ordinal)] + "<"
                + string.Join(", ", arguments.Select((a, i) =>
                    Name(a, argumentNullability is { } infos && i < infos.Length ? infos[i] : null)))
                + ">";
        }

        var prefix = type.Namespace is { } ns && ns.StartsWith("VirtualMarina", StringComparison.Ordinal) && !type.IsNested
            ? ns + "."
            : string.Empty;
        // A bare type parameter is left unannotated: NullabilityInfoContext reports an unconstrained T as nullable
        // whether or not it was written T?, so a "?" there would record the tool's guess rather than the source.
        return prefix + name + (type.IsValueType || type.IsGenericParameter ? string.Empty : Question(state));
    }

    private static string Question(NullabilityState? state) => state == NullabilityState.Nullable ? "?" : string.Empty;

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["System.Void"] = "void",
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Char"] = "char",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Single"] = "float",
        ["System.Double"] = "double",
        ["System.Decimal"] = "decimal",
        ["System.String"] = "string",
        ["System.Object"] = "object",
    };
}
