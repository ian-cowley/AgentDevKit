using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glacier.AgentDevKit.Adk;

public class MockMemoryLlmService : ILlmService
{
    public async Task<LlmResponse> GenerateContentAsync(LlmRequest request)
    {
        var lastMessage = request.Contents.LastOrDefault();
        
        // If the last message was a function response, we provide the final text
        if (lastMessage?.Role == "function")
        {
            return new LlmResponse { Content = "The current time in London is 10:30 AM (Mocked Response)" };
        }

        // If the user message asks for time, we return a function call
        if (lastMessage?.Role == "user" && lastMessage.Parts.Any(p => p.Text?.Contains("time in London") == true))
        {
            return new LlmResponse
            {
                FunctionCalls = new List<LlmFunctionCall>
                {
                    new LlmFunctionCall { Name = "get_current_time", Args = JsonNode.Parse("{\"city\": \"London\"}") }
                }
            };
        }

        return new LlmResponse { Content = "I am a helpful assistant." };
    }
    public async IAsyncEnumerable<LlmResponse> StreamGenerateContentAsync(LlmRequest request)
    {
        var response = await GenerateContentAsync(request);
        yield return response;
    }
}
