using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace StickerSnap.Tests.Api.Contract;

/// <summary>
/// Finds where the server's OpenAPI document departs from the contract's: operations by method,
/// path and operationId, their parameters, request bodies and response codes, and the schemas of
/// bodies and successful answers by property names, required fields, types, enum values and the
/// discriminator values of polymorphic types. Schemas are compared by shape from each operation
/// down, not by component name, so the server may name its types as C# does. Bodies of errors are
/// the Problem the Problems tests check; here only their codes count.
/// </summary>
/// <param name="notYetOnServer">
/// The operationIds of the contract the server does not serve yet. The list only shrinks: an
/// operation that arrives on the server while still listed is a difference too.
/// </param>
public sealed class ContractComparer(OpenApiDocument contract, OpenApiDocument server, IReadOnlySet<string> notYetOnServer)
{
    /// <summary>Deeper than any schema of the contract: a guard against a schema that holds itself.</summary>
    private const int MaxDepth = 32;

    private readonly List<string> _differences = [];

    /// <summary>Every difference as one line that names the operation and the place in it.</summary>
    public IReadOnlyList<string> Compare()
    {
        _differences.Clear();
        var contractOperations = Operations(contract);
        var serverOperations = Operations(server);

        foreach (var (key, expected) in contractOperations)
        {
            var id = expected.Operation.OperationId ?? key;
            if (serverOperations.TryGetValue(key, out var actual))
            {
                if (notYetOnServer.Contains(id))
                    Add($"{id} ({key}) is on the server: strike it from the operations not yet on the server");
                CompareOperation(id, expected, actual);
            }
            else if (!notYetOnServer.Contains(id))
            {
                Add($"{id} ({key}) is in the contract but not on the server, nor among the operations not yet on the server");
            }
        }

        foreach (var (key, actual) in serverOperations.Where(entry => !contractOperations.ContainsKey(entry.Key)))
            Add($"{actual.Operation.OperationId ?? key} ({key}) is on the server but not in the contract");

        var contractIds = contractOperations.Values.Select(entry => entry.Operation.OperationId).ToHashSet();
        foreach (var id in notYetOnServer.Where(id => !contractIds.Contains(id)))
            Add($"{id} is among the operations not yet on the server but not in the contract");

        return _differences.ToList();
    }

    private void Add(string difference) => _differences.Add(difference);

    private sealed record Entry(OpenApiOperation Operation, IList<IOpenApiParameter> Parameters);

    /// <summary>Every operation by `GET /api/v1/app`, with the parameters of its path.</summary>
    private static Dictionary<string, Entry> Operations(OpenApiDocument document) =>
        (document.Paths ?? [])
        .SelectMany(path => (path.Value.Operations ?? []).Select(operation => (
            Key: $"{operation.Key.Method.ToUpperInvariant()} {path.Key}",
            Entry: new Entry(operation.Value, [.. path.Value.Parameters ?? [], .. operation.Value.Parameters ?? []]))))
        .ToDictionary(item => item.Key, item => item.Entry);

    private void CompareOperation(string id, Entry expected, Entry actual)
    {
        if (expected.Operation.OperationId != actual.Operation.OperationId)
            Add($"{id}: operationId is {actual.Operation.OperationId ?? "missing"} on the server");

        CompareSet($"{id} parameters", Parameters(expected.Parameters), Parameters(actual.Parameters));
        CompareRequestBody(id, expected.Operation.RequestBody, actual.Operation.RequestBody);
        CompareResponses(id, expected.Operation.Responses, actual.Operation.Responses);
    }

    private static IEnumerable<string> Parameters(IEnumerable<IOpenApiParameter> parameters) =>
        parameters.Select(parameter => $"{parameter.In} {parameter.Name}{(parameter.Required ? "" : "?")}".ToLowerInvariant());

    private void CompareRequestBody(string id, IOpenApiRequestBody? expected, IOpenApiRequestBody? actual)
    {
        var where = $"{id} request body";
        if (expected is null || actual is null)
        {
            if (expected is not null || actual is not null)
                Add($"{where}: {(expected is null ? "not in the contract" : "missing on the server")}");
            return;
        }

        if (expected.Required != actual.Required)
            Add($"{where}: required is {expected.Required} in the contract, {actual.Required} on the server");

        // The server may accept more media types (application/*+json) than the contract names.
        foreach (var (type, media) in expected.Content ?? new Dictionary<string, OpenApiMediaType>())
        {
            if (actual.Content?.TryGetValue(type, out var served) == true)
                CompareSchema($"{where} {type}", "", media.Schema, served.Schema, depth: 0);
            else
                Add($"{where}: the server does not accept {type}");
        }
    }

    private void CompareResponses(string id, OpenApiResponses? expected, OpenApiResponses? actual)
    {
        expected ??= [];
        actual ??= [];
        CompareSet($"{id} responses", expected.Keys, actual.Keys);

        foreach (var (status, response) in expected.Where(entry => entry.Key.StartsWith('2')))
        {
            if (!actual.TryGetValue(status, out var served)) continue;
            var where = $"{id} {status}";
            var types = response.Content ?? new Dictionary<string, OpenApiMediaType>();
            var servedTypes = served.Content ?? new Dictionary<string, OpenApiMediaType>();
            CompareSet($"{where} media types", types.Keys, servedTypes.Keys);
            foreach (var (type, media) in types.Where(entry => servedTypes.ContainsKey(entry.Key)))
                CompareSchema(where, "", media.Schema, servedTypes[type].Schema, depth: 0);
        }
    }

    /// <param name="operation">The operation and the body: `getApp 200`</param>
    /// <param name="path">The place in the body: `ffmpeg.version`, `recentEmoji[]`, `source&lt;path&gt;`</param>
    private void CompareSchema(string operation, string path, IOpenApiSchema? expected, IOpenApiSchema? actual, int depth)
    {
        var where = path.Length > 0 ? $"{operation} {path}" : operation;
        if (expected is null || actual is null)
        {
            if (expected is not null || actual is not null)
                Add($"{where}: {(expected is null ? "has no schema in the contract" : "has no schema on the server")}");
            return;
        }
        if (depth > MaxDepth) return;

        if (Branches(expected).Count > 0 || Branches(actual).Count > 0)
        {
            var (variants, servedVariants) = (Variants(expected), Variants(actual));
            CompareSet($"{where} variants", variants.Keys, servedVariants.Keys);
            foreach (var (value, variant) in variants.Where(entry => servedVariants.ContainsKey(entry.Key)))
                CompareSchema(operation, $"{path}<{value}>", variant, servedVariants[value], depth + 1);
            return;
        }

        var (expectedType, actualType) = (TypeOf(expected), TypeOf(actual));
        if (expectedType != actualType)
            Add($"{where}: type is {Name(expectedType)} in the contract, {Name(actualType)} on the server");

        CompareSet($"{where} enum", EnumOf(expected), EnumOf(actual));
        CompareSet($"{where} required", expected.Required ?? new HashSet<string>(), actual.Required ?? new HashSet<string>());

        var properties = expected.Properties ?? new Dictionary<string, IOpenApiSchema>();
        var served = actual.Properties ?? new Dictionary<string, IOpenApiSchema>();
        CompareSet($"{where} properties", properties.Keys, served.Keys);
        foreach (var (name, property) in properties.Where(entry => served.ContainsKey(entry.Key)))
            CompareSchema(operation, path.Length > 0 ? $"{path}.{name}" : name, property, served[name], depth + 1);

        if (expected.Items is not null || actual.Items is not null)
            CompareSchema(operation, $"{path}[]", expected.Items, actual.Items, depth + 1);

        if (expected.AdditionalProperties is not null || actual.AdditionalProperties is not null)
            CompareSchema(operation, $"{path}{{}}", expected.AdditionalProperties, actual.AdditionalProperties, depth + 1);
    }

    private static IList<IOpenApiSchema> Branches(IOpenApiSchema schema) =>
        schema.OneOf is { Count: > 0 } oneOf ? oneOf : schema.AnyOf ?? [];

    /// <summary>
    /// The branches of a polymorphic schema by the value of their discriminator: the property the
    /// schema's discriminator names, or else the property a branch fixes to one value
    /// (`kind: { const: path }`, or the single-value enum System.Text.Json writes for a derived type).
    /// </summary>
    private static Dictionary<string, IOpenApiSchema> Variants(IOpenApiSchema schema)
    {
        var property = schema.Discriminator?.PropertyName;
        var variants = new Dictionary<string, IOpenApiSchema>();
        foreach (var (branch, index) in Branches(schema).Select((branch, index) => (branch, index)))
        {
            var fixedValues = (branch.Properties ?? new Dictionary<string, IOpenApiSchema>())
                .Where(entry => property is null || entry.Key == property)
                .Select(entry => EnumOf(entry.Value))
                .Where(values => values.Count == 1)
                .Select(values => $"{values[0]}");
            variants[fixedValues.FirstOrDefault() ?? $"#{index}"] = branch;
        }
        return variants;
    }

    /// <summary>The type without the format (int32, double); an enum alone is a string, properties alone an object.</summary>
    private static JsonSchemaType? TypeOf(IOpenApiSchema schema) => schema.Type switch
    {
        { } type => type,
        null when EnumOf(schema).Count > 0 => JsonSchemaType.String,
        null when schema.Properties is { Count: > 0 } => JsonSchemaType.Object,
        _ => null,
    };

    private static string Name(JsonSchemaType? type) => type?.ToString().ToLowerInvariant() ?? "unset";

    /// <summary>The enum values, or the one value of a const.</summary>
    private static List<string> EnumOf(IOpenApiSchema schema) =>
        schema.Const is { } value ? [value] : (schema.Enum ?? []).Select(Text).ToList();

    private static string Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node?.ToJsonString() ?? "null";

    private void CompareSet(string where, IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var (contractSet, serverSet) = (expected.ToHashSet(), actual.ToHashSet());
        var missing = contractSet.Except(serverSet).Order(StringComparer.Ordinal).ToList();
        var extra = serverSet.Except(contractSet).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0) Add($"{where}: missing on the server: {string.Join(", ", missing)}");
        if (extra.Count > 0) Add($"{where}: not in the contract: {string.Join(", ", extra)}");
    }
}
