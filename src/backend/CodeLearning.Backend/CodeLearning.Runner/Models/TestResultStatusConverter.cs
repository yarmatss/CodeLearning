using System.Text.Json;
using System.Text.Json.Serialization;
using CodeLearning.Core.Enums;

namespace CodeLearning.Runner.Models;

public class TestResultStatusConverter : JsonConverter<TestResultStatus>
{
    public override TestResultStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            // Handle int: 1, 2, 3
            return (TestResultStatus)reader.GetInt32();
        }
        
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            
            // Handle string: "CompilationError" from Python wrapper
            return value switch
            {
                "CompilationError" => TestResultStatus.RuntimeError, // Map to RuntimeError with special handling
                "Passed" => TestResultStatus.Passed,
                "Failed" => TestResultStatus.Failed,
                "RuntimeError" => TestResultStatus.RuntimeError,
                _ => TestResultStatus.RuntimeError
            };
        }
        
        throw new JsonException($"Unexpected token type: {reader.TokenType}");
    }

    public override void Write(
        Utf8JsonWriter writer,
        TestResultStatus value,
        JsonSerializerOptions options)
    {
        writer.WriteNumberValue((int)value);
    }
}
