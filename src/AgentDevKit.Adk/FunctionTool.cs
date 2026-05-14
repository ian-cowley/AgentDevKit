using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glacier.AgentDevKit.Adk;

public class FunctionTool<TArgs, TResult> : ITool
{
    private readonly Func<TArgs, TResult> _execute;
    public string Name { get; }
    public string Description { get; }

    public FunctionTool(string name, string description, Func<TArgs, TResult> execute)
    {
        Name = name;
        Description = description;
        _execute = execute;
    }

    public JsonNode GetParametersSchema()
    {
        // Simple manual schema generation for common types. 
        // A production SDK would use reflection and System.Text.Json.Schema
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["city"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The name of the city."
                }
            },
            ["required"] = new JsonArray("city")
        };
        return schema;
    }

    public Task<string> ExecuteAsync(string arguments)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var args = JsonSerializer.Deserialize<TArgs>(arguments, options);
        if (args == null) throw new ArgumentException("Invalid arguments");
        
        var result = _execute(args);
        return Task.FromResult(JsonSerializer.Serialize(result));
    }
}
