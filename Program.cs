using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<JsonToJavaScriptConverter>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/api/convert", async (HttpRequest request, JsonToJavaScriptConverter converter) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    var rawBody = await reader.ReadToEndAsync();

    if (string.IsNullOrWhiteSpace(rawBody))
    {
        return Results.BadRequest(new { error = "Request body cannot be empty." });
    }

    try
    {
        var result = converter.Convert(rawBody);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = $"Invalid JSON input: {ex.Message}" });
    }
});

app.Run();

internal sealed class JsonToJavaScriptConverter
{
    public ConversionResponse Convert(string rawBody)
    {
        var nodes = ParseInput(rawBody);
        if (nodes.Count == 0)
        {
            throw new InvalidOperationException("No JSON object found in the request body.");
        }

        var lines = new List<string>();

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var suffix = i == 0 ? string.Empty : $"_{i + 1}";
            ConvertSingleReport(node, suffix, lines);
        }

        return new ConversionResponse
        {
            ObjectCount = nodes.Count,
            Lines = lines,
            JavaScript = string.Join(Environment.NewLine, lines)
        };
    }

    private static List<JsonNode> ParseInput(string rawBody)
    {
        var cleaned = rawBody.Trim();

        // Accept input that arrives as an escaped JSON string.
        if (cleaned.StartsWith('"') && cleaned.EndsWith('"'))
        {
            cleaned = JsonSerializer.Deserialize<string>(cleaned) ?? string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(cleaned);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var result = new List<JsonNode>();
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                continue;
            }

            using var document = JsonDocument.ParseValue(ref reader);
            result.Add(JsonNode.Parse(document.RootElement.GetRawText())!);
        }

        return result;
    }

    private static void ConvertSingleReport(JsonNode root, string suffix, List<string> lines)
    {
        var reportRequest = root["reportRequest"];
        if (reportRequest is null)
        {
            throw new InvalidOperationException("Each JSON object must contain reportRequest.");
        }

        var reportData = reportRequest["reportData"];
        if (reportData is null)
        {
            throw new InvalidOperationException("reportRequest.reportData is required.");
        }

        AddTexts(reportData["texts"]?.AsArray(), suffix, lines);
        AddImages(reportData["images"]?.AsArray(), suffix, lines);
        AddTableMetaData(reportData["tables"]?.AsArray(), suffix, lines);
    }

    private static void AddTexts(JsonArray? texts, string suffix, List<string> lines)
    {
        if (texts is null || texts.Count == 0)
        {
            return;
        }

        var variableName = $"reportTextsJson{suffix}";
        lines.Add($"var {variableName} = {{");

        var mapped = texts
            .Select(t => (Name: t?["name"]?.GetValue<string>(), Value: t?["value"]?.GetValue<string>()))
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToList();

        for (var i = 0; i < mapped.Count; i++)
        {
            var item = mapped[i];
            var comma = i == mapped.Count - 1 ? string.Empty : ",";
            lines.Add($"    {ToJsIdentifier(item.Name!)}: \"{EscapeJs(item.Value)}\"{comma}");
        }

        lines.Add("};");
        lines.Add(string.Empty);

        var pascalSuffix = suffix.Replace("_", string.Empty);
        lines.Add($"execution.setVariable(\"ReportTextsJson{pascalSuffix}\", {variableName});");
        lines.Add($"execution.setVariable(\"ReportTextsJson{pascalSuffix}Text\", JSON.stringify({variableName}));");
        lines.Add(string.Empty);
    }

    private static void AddImages(JsonArray? images, string suffix, List<string> lines)
    {
        if (images is null || images.Count == 0)
        {
            return;
        }

        var variableName = $"imageMetaDataJson{suffix}";
        lines.Add($"var {variableName} = [");

        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            var comma = i == images.Count - 1 ? string.Empty : ",";
            var url = image?["Url"]?.GetValue<string>() ?? image?["url"]?.GetValue<string>() ?? string.Empty;

            lines.Add("    {");
            lines.Add($"        name: \"{EscapeJs(image?["name"]?.GetValue<string>())}\",");
            lines.Add($"        url: \"{EscapeJs(url)}\",");
            lines.Add($"        width: \"{EscapeJs(image?["width"]?.GetValue<string>())}\",");
            lines.Add($"        height: \"{EscapeJs(image?["height"]?.GetValue<string>())}\",");
            lines.Add($"        altText: \"{EscapeJs(image?["altText"]?.GetValue<string>())}\"");
            lines.Add($"    }}{comma}");
        }

        lines.Add("];\n");

        var pascalSuffix = suffix.Replace("_", string.Empty);
        lines.Add($"execution.setVariable(\"ImageMetaDataJson{pascalSuffix}\", {variableName});");
        lines.Add($"execution.setVariable(\"ImageMetaDataJson{pascalSuffix}Text\", JSON.stringify({variableName}));");
        lines.Add(string.Empty);
    }

    private static void AddTableMetaData(JsonArray? tables, string suffix, List<string> lines)
    {
        if (tables is null || tables.Count == 0)
        {
            return;
        }

        for (var index = 0; index < tables.Count; index++)
        {
            var table = tables[index];
            var tableSuffix = tables.Count > 1 ? $"_{index + 1}" : string.Empty;
            var variableName = $"tableMetaDataJson{suffix}{tableSuffix}";

            lines.Add($"var {variableName} = {{");
            lines.Add($"    name: \"{EscapeJs(table?["name"]?.GetValue<string>())}\",");
            lines.Add($"    tableMetaData: \"{EscapeJs(table?["tableMetaData"]?.GetValue<string>())}\",");
            lines.Add($"    headerRowMetaData: \"{EscapeJs(table?["headerRowMetaData"]?.GetValue<string>())}\",");
            lines.Add($"    headerCellMetaData: \"{EscapeJs(table?["headerCellMetaData"]?.GetValue<string>())}\",");
            lines.Add($"    rowMetaData: \"{EscapeJs(table?["rowMetaData"]?.GetValue<string>())}\",");
            lines.Add($"    cellMetaData: \"{EscapeJs(table?["cellMetaData"]?.GetValue<string>())}\"");
            lines.Add("};");
            lines.Add(string.Empty);

            var pascalSuffix = $"{suffix}{tableSuffix}".Replace("_", string.Empty);
            lines.Add($"execution.setVariable(\"TableMetaDataJson{pascalSuffix}\", {variableName});");
            lines.Add($"execution.setVariable(\"TableMetaDataJson{pascalSuffix}Text\", JSON.stringify({variableName}));");
            lines.Add(string.Empty);
        }
    }

    private static string EscapeJs(string? value) => (value ?? string.Empty)
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"");

    private static string ToJsIdentifier(string value)
    {
        var first = value[0];
        if ((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z') || first == '_' || first == '$')
        {
            return value.Replace("-", "_");
        }

        return $"\"{EscapeJs(value)}\"";
    }
}

internal sealed class ConversionResponse
{
    public int ObjectCount { get; init; }
    public required List<string> Lines { get; init; }
    public required string JavaScript { get; init; }
}
