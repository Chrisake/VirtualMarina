using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace VirtualMarina.Docs.ApiDocGen;

/// <summary>
/// The XML documentation of the documented assemblies, looked up by member, with <c>&lt;inheritdoc/&gt;</c> followed to
/// the member it inherits from.
/// </summary>
internal sealed partial class XmlDocumentation
{
    /// <summary>What is left when an inherited comment cannot be found in any of the loaded documentation files.</summary>
    public const string InheritedPlaceholder = "*(See the base or interface member.)*";

    // A chain of inheritdoc longer than this is a cycle, not a hierarchy.
    private const int MaxInheritanceDepth = 16;

    private readonly Dictionary<string, XElement> _members;

    private XmlDocumentation(Dictionary<string, XElement> members) => _members = members;

    /// <summary>Reads the XML documentation file next to each assembly; the libraries have to be built first.</summary>
    public static XmlDocumentation Load(IEnumerable<Assembly> assemblies)
    {
        var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
            if (!File.Exists(xmlPath)) throw new FileNotFoundException("XML documentation not found; build the libraries first.", xmlPath);
            foreach (var member in XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace).Descendants("member"))
            {
                members[(string)member.Attribute("name")!] = member;
            }
        }

        return new XmlDocumentation(members);
    }

    /// <summary>The summary and remarks of a type or member, flattened to one line of Markdown each.</summary>
    public (string Summary, string Remarks) For(MemberInfo member) =>
        !_members.ContainsKey(DocIds.Of(member)) ? ("", "") : Resolve(member, 0) ?? (InheritedPlaceholder, "");

    /// <summary>The comment on a member, following inheritdoc; null when neither it nor what it inherits from has one.</summary>
    private (string Summary, string Remarks)? Resolve(MemberInfo member, int depth)
    {
        if (depth > MaxInheritanceDepth || !_members.TryGetValue(DocIds.Of(member), out var element)) return null;
        if (element.Element("inheritdoc") is not { } inherit || element.Element("summary") is not null)
        {
            return (Flatten(element.Element("summary")), Flatten(element.Element("remarks")));
        }

        if ((string?)inherit.Attribute("cref") is { } cref)
        {
            return ResolveId(cref, depth + 1);
        }

        // No cref: the comment comes from the member this one overrides or implements, the same search the C#
        // compiler and IntelliSense make. The first ancestor that has documentation of its own wins.
        foreach (var ancestor in InheritedFrom(member))
        {
            if (Resolve(ancestor, depth + 1) is { } inherited) return inherited;
        }

        return null;
    }

    /// <summary>Follows an explicit <c>cref</c>, which names the member by documentation id instead of by reflection.</summary>
    private (string Summary, string Remarks)? ResolveId(string id, int depth)
    {
        if (depth > MaxInheritanceDepth || !_members.TryGetValue(id, out var element)) return null;
        if (element.Element("inheritdoc") is { } inherit && element.Element("summary") is null)
        {
            return (string?)inherit.Attribute("cref") is { } cref && cref != id ? ResolveId(cref, depth + 1) : null;
        }

        return (Flatten(element.Element("summary")), Flatten(element.Element("remarks")));
    }

    /// <summary>
    /// The members a comment can be inherited from, nearest first: the member it overrides in each base class, then
    /// the interface members it implements. For a type, its base types and then its interfaces.
    /// </summary>
    private static IEnumerable<MemberInfo> InheritedFrom(MemberInfo member)
    {
        switch (member)
        {
            case Type type:
                for (var baseType = type.BaseType; baseType is not null && baseType != typeof(object); baseType = baseType.BaseType)
                {
                    yield return Definition(baseType);
                }

                foreach (var implemented in type.GetInterfaces()) yield return Definition(implemented);
                break;

            case MethodInfo method:
                foreach (var ancestor in Overridden(method)) yield return Definition(ancestor);
                foreach (var ancestor in Implemented(method)) yield return Definition(ancestor);
                break;

            case PropertyInfo property when (property.GetMethod ?? property.SetMethod) is { } accessor:
                foreach (var ancestor in Overridden(accessor).Concat(Implemented(accessor)))
                {
                    if (PropertyOf(ancestor) is { } inherited) yield return Definition(inherited);
                }

                break;

            case EventInfo ev when ev.AddMethod is { } adder:
                foreach (var ancestor in Overridden(adder).Concat(Implemented(adder)))
                {
                    if (EventOf(ancestor) is { } inherited) yield return Definition(inherited);
                }

                break;

            case ConstructorInfo ctor when ctor.DeclaringType?.BaseType is { } baseType:
                // A constructor inherits from the base constructor with the same parameters.
                var parameters = ctor.GetParameters().Select(p => p.ParameterType).ToArray();
                if (baseType.GetConstructor(DocIds.AllDeclared, parameters) is { } baseCtor) yield return Definition(baseCtor);
                break;

            default:
                break;
        }
    }

    /// <summary>The declarations a virtual method overrides, from the nearest base class up to the one that introduced it.</summary>
    private static IEnumerable<MethodInfo> Overridden(MethodInfo method)
    {
        if (!method.IsVirtual) yield break;
        var root = method.GetBaseDefinition();
        if (root.DeclaringType == method.DeclaringType) yield break;

        for (var type = method.DeclaringType?.BaseType; type is not null; type = type.BaseType)
        {
            var declared = type.GetMethods(DocIds.AllDeclared)
                .FirstOrDefault(m => m.Name == method.Name && SameDefinition(m.GetBaseDefinition(), root));
            if (declared is not null) yield return declared;
            if (type == root.DeclaringType) yield break;
        }
    }

    /// <summary>The interface methods a method implements, found through the interface maps of its declaring type.</summary>
    private static IEnumerable<MethodInfo> Implemented(MethodInfo method)
    {
        if (method.DeclaringType is not { IsInterface: false } type) yield break;
        foreach (var implemented in type.GetInterfaces())
        {
            var map = type.GetInterfaceMap(implemented);
            for (var i = 0; i < map.TargetMethods.Length; i++)
            {
                if (SameDefinition(map.TargetMethods[i], method)) yield return map.InterfaceMethods[i];
            }
        }
    }

    private static bool SameDefinition(MethodInfo a, MethodInfo b) => a.MetadataToken == b.MetadataToken && a.Module == b.Module;

    private static PropertyInfo? PropertyOf(MethodInfo accessor) =>
        accessor.DeclaringType?.GetProperties(DocIds.AllDeclared).FirstOrDefault(p => (p.GetMethod is { } get && SameDefinition(get, accessor)) || (p.SetMethod is { } set && SameDefinition(set, accessor)));

    private static EventInfo? EventOf(MethodInfo accessor) =>
        accessor.DeclaringType?.GetEvents(DocIds.AllDeclared).FirstOrDefault(e => (e.AddMethod is { } add && SameDefinition(add, accessor)) || (e.RemoveMethod is { } remove && SameDefinition(remove, accessor)));

    /// <summary>
    /// A member of a constructed generic type (IEquatable&lt;Berth&gt;.Equals) as declared on its generic definition
    /// (IEquatable&lt;T&gt;.Equals), which is the form a documentation id names.
    /// </summary>
    private static MemberInfo Definition(MemberInfo member) => member switch
    {
        Type { IsConstructedGenericType: true } type => type.GetGenericTypeDefinition(),
        { DeclaringType.IsConstructedGenericType: true } => member.DeclaringType.GetGenericTypeDefinition().GetMemberWithSameMetadataDefinitionAs(member),
        _ => member,
    };

    // ---- Flattening the comment to Markdown ----------------------------------------------------------

    private static string Flatten(XElement? element)
    {
        if (element is null) return "";
        var text = new StringBuilder();
        foreach (var node in element.Nodes()) Append(node, text);
        return Whitespace().Replace(text.ToString(), " ").Trim();

        static void Append(XNode node, StringBuilder text)
        {
            switch (node)
            {
                case XText t:
                    text.Append(t.Value);
                    break;
                case XElement e when e.Name == "see" || e.Name == "seealso":
                    var cref = (string?)e.Attribute("cref") ?? (string?)e.Attribute("langword") ?? "";
                    text.Append('`').Append(ShortCref(cref)).Append('`');
                    break;
                case XElement e when e.Name == "paramref" || e.Name == "typeparamref":
                    text.Append('`').Append((string?)e.Attribute("name")).Append('`');
                    break;
                case XElement e when e.Name == "c":
                    text.Append('`').Append(e.Value).Append('`');
                    break;
                case XElement e when e.Name == "code":
                    break; // examples are in the guides and IntelliSense
                case XElement e when e.Name == "para":
                    foreach (var child in e.Nodes()) Append(child, text);
                    text.Append(' ');
                    break;
                case XElement e:
                    foreach (var child in e.Nodes()) Append(child, text);
                    break;
                default:
                    break;
            }
        }
    }

    private static string ShortCref(string cref)
    {
        var withoutPrefix = cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref;
        var name = withoutPrefix.Split('(')[0];
        var parts = name.Split('.');
        var shortName = cref.StartsWith("T:", StringComparison.Ordinal) ? parts[^1] : parts.Length >= 2 ? parts[^2] + "." + parts[^1] : parts[^1];
        return GenericArity().Replace(shortName.Replace("#ctor", "ctor", StringComparison.Ordinal), "");
    }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Whitespace();

    [GeneratedRegex("`+\\d+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GenericArity();
}
