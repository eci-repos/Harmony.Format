using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format;

/// <summary>
/// In-memory conversation used as input to ILanguageModelChatService.
/// This is the "prompt-ready" view; durable session transcripts live elsewhere.
/// </summary>
public sealed class ChatConversation
{
   private readonly List<ChatMessage> _messages = new();

   /// <summary>
   /// Full message list (may include metadata). Provider adapters can filter as needed.
   /// </summary>
   public IReadOnlyList<ChatMessage> Messages => _messages;

   // Backward-compatible helpers (existing call sites keep working)
   // ----------------------------------------------------------------------------------------------

   public void AddSystemMessage(string content) =>
      Add(new ChatMessage(role: "system", content: content));

   public void AddUserMessage(string content) =>
      Add(new ChatMessage(role: "user", content: content));

   public void AddAssistantMessage(string content) =>
      Add(new ChatMessage(role: "assistant", content: content));

   // New structured APIs
   // ----------------------------------------------------------------------------------------------

   public void Add(ChatMessage message)
   {
      if (message is null) throw new ArgumentNullException(nameof(message));
      _messages.Add(message);
   }

   public void AddRange(IEnumerable<ChatMessage> messages)
   {
      if (messages is null) throw new ArgumentNullException(nameof(messages));
      foreach (var m in messages)
      {
         if (m is not null) _messages.Add(m);
      }
   }

   public void Clear() => _messages.Clear();

   /// <summary>
   /// Converts an HRF HarmonyMessage into a ChatMessage and appends it.
   /// If content is not a JSON string, it is serialized to a JSON string representation.
   /// </summary>
   public void AddFromHarmonyMessage(HarmonyMessage msg, int? sourceIndex = null)
   {
      if (msg is null) throw new ArgumentNullException(nameof(msg));

      string content = msg.Content.ValueKind switch
      {
         JsonValueKind.String => msg.Content.GetString() ?? string.Empty,
         JsonValueKind.Null => string.Empty,
         JsonValueKind.Undefined => string.Empty,
         _ => msg.Content.ToString()
      };

      Add(new ChatMessage(
         role: msg.Role,
         content: content,
         channel: msg.Channel?.ToString(),
         contentType: msg.ContentType,
         recipient: msg.Recipient,
         termination: msg.Termination?.ToString(),
         sourceIndex: sourceIndex
      ));
   }

   /// <summary>
   /// Returns only messages intended to be sent to the language model, applying a default
   /// filtering policy. You may pass a custom predicate to override the policy.
   /// </summary>
   public IReadOnlyList<ChatMessage> GetModelInputMessages(Func<ChatMessage, bool>? predicate = null)
   {
      var filter = predicate ?? DefaultModelFilter;
      return _messages.Where(filter).ToList();
   }

   /// <summary>
   /// Default filter: keep system/user/assistant with non-empty content.
   /// Excludes analysis-only messages by convention if Channel == "analysis".
   /// (You can relax/tighten this policy later.)
   /// </summary>
   private static bool DefaultModelFilter(ChatMessage m)
   {
      if (m is null) return false;
      if (string.IsNullOrWhiteSpace(m.Role)) return false;
      if (string.IsNullOrWhiteSpace(m.Content)) return false;

      // Common policy: don't send analysis-only content to providers
      if (string.Equals(m.Channel, "analysis", StringComparison.OrdinalIgnoreCase))
         return false;

      return true;
   }
}

/// <summary>
/// A single chat message with optional HRF metadata.
/// This enables ChatConversation to preserve HRF semantics (channel/recipient/termination)
/// without coupling Harmony.Format to any provider.
/// </summary>
public sealed class ChatMessage
{
   public ChatMessage(
      string role,
      string content,
      string? channel = null,
      string? contentType = null,
      string? recipient = null,
      string? termination = null,
      int? sourceIndex = null)
   {
      Role = role ?? throw new ArgumentNullException(nameof(role));
      Content = content ?? string.Empty;

      Channel = channel;
      ContentType = contentType;
      Recipient = recipient;
      Termination = termination;

      SourceIndex = sourceIndex;
      Timestamp = DateTimeOffset.UtcNow;
   }

   public string Role { get; }
   public string Content { get; }

   public string? Channel { get; }
   public string? ContentType { get; }
   public string? Recipient { get; }
   public string? Termination { get; }

   public int? SourceIndex { get; }
   public DateTimeOffset Timestamp { get; }
}
