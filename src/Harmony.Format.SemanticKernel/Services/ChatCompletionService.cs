using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.SemanticKernel;

public sealed class ChatCompletionService : ILanguageModelChatService
{
   private readonly Kernel _kernel;
   private readonly IChatCompletionService _chat;

   public ChatCompletionService(Kernel kernel, IChatCompletionService chat)
   {
      _kernel = kernel;
      _chat = chat;
   }

   public async Task<string> GetAssistantReplyAsync(
      ChatConversation history, CancellationToken ct = default)
   {
      return await GetAssistantReplyAsync(history, null, ct);
   }

   public async Task<string> GetAssistantReplyAsync(
      ChatConversation history, Func<ChatMessage, bool> modelFilter, 
      CancellationToken ct = default)
   {
      var skHistory = new ChatHistory();

      foreach (var msg in history.Messages)
      {
         switch (msg.Role)
         {
            case "system": skHistory.AddSystemMessage(msg.Content); break;
            case "user": skHistory.AddUserMessage(msg.Content); break;
            case "assistant": skHistory.AddAssistantMessage(msg.Content); break;
         }
      }

      var result = await _chat.GetChatMessageContentsAsync(
         skHistory, kernel: _kernel, cancellationToken: ct);
      return string.Join("\n", result.Select(r => r.Content));
   }
}

