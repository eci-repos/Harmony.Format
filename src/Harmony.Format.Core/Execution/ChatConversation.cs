using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

public sealed class ChatConversation
{
   private readonly List<(string Role, string Content)> _messages = new();

   public void AddSystemMessage(string content) => _messages.Add(("system", content));
   public void AddUserMessage(string content) => _messages.Add(("user", content));
   public void AddAssistantMessage(string content) => _messages.Add(("assistant", content));

   public IReadOnlyList<(string Role, string Content)> Messages => _messages;
}
