using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UPBazaar.Api.OpenApi;

/// <summary>
/// Narrows numeric schemas from <c>["integer","string"]</c> to <c>integer</c>.
///
/// .NET 10 describes an <c>int</c> as accepting either form, which is true of the *input*:
/// System.Text.Json will read <c>"42"</c> as well as <c>42</c>. It is not true of the output —
/// the API always writes a JSON number — and the union makes every generated client type a
/// <c>number | string</c> that each caller then has to coerce.
///
/// Describing the response accurately is worth more here than describing the input leniency,
/// so the string half is dropped along with the pattern that only applied to it.
/// </summary>
internal sealed class NumericSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (schema.Type is not { } type)
        {
            return Task.CompletedTask;
        }

        var isNumeric = type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number);

        if (!isNumeric || !type.HasFlag(JsonSchemaType.String))
        {
            return Task.CompletedTask;
        }

        schema.Type = type & ~JsonSchemaType.String;
        schema.Pattern = null;

        return Task.CompletedTask;
    }
}
