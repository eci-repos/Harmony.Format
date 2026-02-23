using Harmony.Format;

internal sealed class FakeChatService : ILanguageModelChatService
{
   private readonly string _response;

   public FakeChatService(string response)
   {
      _response = response;
   }

   public Task<string> GetAssistantReplyAsync(
       ChatConversation history,
       CancellationToken ct = default)
       => Task.FromResult(_response);

   public Task<string> GetAssistantReplyAsync(
       ChatConversation history,
       Func<ChatMessage, bool> modelFilter,
       CancellationToken ct = default)
       => Task.FromResult(_response);
}

internal sealed class FakeToolExecutionService : IToolExecutionService
{
   private readonly object? _result;

   public FakeToolExecutionService(object? result)
   {
      _result = result;
   }

   public Task<object?> InvokeToolAsync(
       string recipient,
       IReadOnlyDictionary<string, object?> args,
       CancellationToken ct = default)
       => Task.FromResult(_result);
}
