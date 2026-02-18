using System;
using System.Threading;
using System.Threading.Tasks;

namespace Harmony.Format;

public interface ILanguageModelChatService
{
   Task<string> GetAssistantReplyAsync(
      ChatConversation history,
      CancellationToken ct = default);

   Task<string> GetAssistantReplyAsync(
      ChatConversation history,
      Func<ChatMessage, bool> modelFilter,
      CancellationToken ct = default);
}
