using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyTranslate;

internal sealed class MyMemoryResponse
{
    [JsonPropertyName("responseStatus")] public JsonElement ResponseStatus { get; set; }
    [JsonPropertyName("responseDetails")] public string? ResponseDetails { get; set; }
    [JsonPropertyName("responseData")] public ResponseData? ResponseData { get; set; }
}

internal sealed class ResponseData
{
    [JsonPropertyName("translatedText")] public string? TranslatedText { get; set; }
}

