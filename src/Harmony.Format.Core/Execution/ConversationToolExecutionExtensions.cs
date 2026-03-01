// /Harmony.Format/Execution/ConversationToolExecutionExtensions.cs
using System.Text.Json;
using Harmony.Tooling.Contracts;
using Harmony.Tooling.Models;

// -----------------------------------------------------------------------------
namespace Harmony.Format.Execution;

public static class ConversationToolExecutionExtensions
{
   /// <summary>
   /// Executes assistant tool-call messages in a HarmonyConversation by
   /// resolving tools from an IToolRegistry (no SK dependency).
   /// Appends tool results as tool-role messages on the commentary channel.
   /// </summary>
   public static async Task ExecuteToolCallsAsync(
       this HarmonyConversation convo,
       IToolRegistry registry,
       ToolExecutionContext? execCtx = null,
       CancellationToken ct = default)
   {
      if (convo is null) throw new ArgumentNullException(nameof(convo));
      var snapshot = (convo.Messages ?? 
         Enumerable.Empty<HarmonyMessage>()).ToList();

      foreach (var m in snapshot)
      {
         ct.ThrowIfCancellationRequested();

         // Only assistant commentary with a recipient (tool call)
         if (!string.Equals(
               m.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                  || m.Channel != HarmonyChannel.commentary
                  || string.IsNullOrWhiteSpace(m.Recipient))
            continue;

         var recipient = m.Recipient!;
         var tool = registry.Resolve(recipient);

         if (convo.Messages is null) 
            convo.Messages = new List<HarmonyMessage>();

         if (tool is null)
         {
            // Missing tool → normalized error payload
            convo.Messages.Add(new HarmonyMessage
            {
               Role = recipient,
               Channel = HarmonyChannel.commentary,
               ContentType = "json",
               Content = JsonSerializer.SerializeToElement(new
               {
                  error = "tool_not_found",
                  tool = recipient
               }),
               Termination = HarmonyTermination.end
            });
            continue;
         }

         // Build JSON input from message content (preserve nested JSON)
         JsonDocument input;
         if (string.Equals(
             m.ContentType, "json", StringComparison.OrdinalIgnoreCase) &&
             m.Content.ValueKind != JsonValueKind.Undefined)
         {
            input = JsonDocument.Parse(m.Content.GetRawText());
         }
         else
         {
            input = JsonDocument.Parse(@"{ ""value"": " 
               + JsonSerializer.Serialize(m.Content.ToString()) + " }");
         }

         var result = await tool.ExecuteAsync(
            input, execCtx, ct).ConfigureAwait(false);

         convo.Messages.Add(result.Ok
             ? new HarmonyMessage
             {
                Role = recipient,
                Channel = HarmonyChannel.commentary,
                Content = result.Data,
                Termination = HarmonyTermination.end
             }
             : new HarmonyMessage
             {
                Role = recipient,
                Channel = HarmonyChannel.commentary,
                ContentType = "json",
                Content = JsonSerializer.SerializeToElement(new
                {
                   error = result.Error?.Code ?? "tool_execution_failed",
                   tool = recipient,
                   message = result.Error?.Message
                }),
                Termination = HarmonyTermination.end
             });
      }
   }
}