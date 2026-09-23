using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Docs/schema/marina.schema.json against the classes that write the file: every property they write is in the schema, in
/// the right place, with the right enum names, and the schema lists nothing they don't write. So the two cannot drift apart.
/// </summary>
public class MarinaSchemaTests
{
    [Fact]
    public void TheSchema_DescribesExactlyWhatTheFileWrites()
    {
        var schema = LoadSchema();
        var problems = new List<string>();
        CompareObject(typeof(DocumentDto), schema, schema, "$", problems);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheSchema_IsForTheCurrentVersion()
    {
        var schema = LoadSchema();
        Assert.Equal(MarinaDocument.FormatName, (string?)schema["properties"]!["format"]!["const"]);
        Assert.Contains($"format {MarinaDocument.CurrentVersion.ToString(2)}", (string?)schema["title"], StringComparison.Ordinal);
    }

    private static void CompareObject(Type dto, JsonObject node, JsonObject root, string path, List<string> problems)
    {
        var written = MarinaJson.Indented.GetTypeInfo(dto)!.Properties.Where(property => !property.IsExtensionData).ToList();
        if (node["properties"] is not JsonObject described)
        {
            problems.Add($"{path}: the schema has no properties for {dto.Name}");
            return;
        }

        foreach (var property in written)
        {
            if (described[property.Name] is not JsonObject child)
            {
                problems.Add($"{path}.{property.Name}: written by {dto.Name} but missing from the schema");
                continue;
            }

            CompareValue(property.PropertyType, Resolve(child, root), root, $"{path}.{property.Name}", problems);
        }

        foreach (var (name, _) in described)
        {
            if (!written.Exists(property => property.Name == name)) problems.Add($"{path}.{name}: in the schema but not written by {dto.Name}");
        }
    }

    private static void CompareValue(Type type, JsonObject node, JsonObject root, string path, List<string> problems)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (typeof(ExtensibleDto).IsAssignableFrom(type))
        {
            CompareObject(type, node, root, path, problems);
        }
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            if ((string?)node["type"] != "array" || node["items"] is not JsonObject items)
            {
                problems.Add($"{path}: a list, but the schema does not describe an array of items");
                return;
            }

            CompareValue(type.GetGenericArguments()[0], Resolve(items, root), root, path + "[]", problems);
        }
        else if (type.IsEnum)
        {
            var names = (node["enum"] as JsonArray ?? []).Select(value => (string?)value).ToHashSet();
            if (!names.SetEquals(Enum.GetNames(type))) problems.Add($"{path}: the schema's names for {type.Name} are not the enum's");
        }
        else if (type == typeof(Dictionary<string, string>) && (string?)node["type"] != "object")
        {
            problems.Add($"{path}: a string map, but the schema does not describe an object");
        }
    }

    private static JsonObject Resolve(JsonObject node, JsonObject root)
    {
        while ((string?)node["$ref"] is { } reference && reference.StartsWith("#/$defs/", StringComparison.Ordinal))
        {
            node = root["$defs"]![reference["#/$defs/".Length..]]!.AsObject();
        }

        return node;
    }

    private static JsonObject LoadSchema([CallerFilePath] string testFile = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "..", "Docs", "schema", "marina.schema.json"));
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }
}
