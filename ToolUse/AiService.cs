using OpenAI.Chat;
using OpenAI;

namespace ToolUse;
public class AiService
{
    private readonly string _endPoint;
    private readonly string _apiKey;
    private readonly string _model;

    public AiService(string endPoint, string model, string apiKey)
    {
        _endPoint = endPoint;
        _model = model;
        _apiKey = apiKey;
    }

    public ChatTool GetTool(string name, string description, string schema)
    {
        return ChatTool.CreateFunctionTool(
            functionName: name,
            functionDescription: description,
            functionParameters: BinaryData.FromString(schema)
        );
    }


    public async Task<string> CompleteAsync(string prompt, string? system)
    {
        var messages = new ChatMessage[] { new SystemChatMessage(system), new UserChatMessage(prompt) };
        return await CompleteAsync(messages);
    }

    /// <summary>
    /// Basic Completion function for testing purposes
    /// it doesnt handle normal responses and tool calling at the same time
    /// also it doesnt check usages or anything besides whats needed to be tested
    /// </summary>
    /// <param name="messages"></param>
    /// <param name="chatCompletionOptions"></param>
    /// <returns></returns>
    public async Task<string> CompleteAsync(ChatMessage[] messages, ChatCompletionOptions? chatCompletionOptions = null)
    {
        var options = new OpenAIClientOptions()
        {
            Endpoint = new Uri(_endPoint)
        };
        string apiKey = _apiKey;
        //lmstudio: "lmstudio-community/Llama-3.2-3B-Instruct-GGUF"
        //ollama: llama3.2
        //togetherai: "meta-llama/Meta-Llama-3.1-8B-Instruct-Turbo"
        //string model = "llama3.2";
        ChatClient chatClient = new(_model, apiKey, options);
        var requestResponse = await chatClient.CompleteChatAsync(messages, options: chatCompletionOptions);
        if (requestResponse is not null)
        {

            var response = requestResponse.Value;

            if (response.FinishReason == ChatFinishReason.ToolCalls)
            {
                foreach (var toolCall in response.ToolCalls)
                {
                    return toolCall.FunctionArguments;
                }
            }


            if (response.FinishReason == ChatFinishReason.Stop)
            {
                if (response.Content.Count > 0)
                {
                    return (response.Content[0].Text);
                }
            }
        }

        return string.Empty;
    }
}
