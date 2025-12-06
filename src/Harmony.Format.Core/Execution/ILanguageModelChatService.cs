using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

public interface ILanguageModelChatService
{
   Task<string> GetAssistantReplyAsync(
       ChatConversation history,
       CancellationToken ct = default);
}