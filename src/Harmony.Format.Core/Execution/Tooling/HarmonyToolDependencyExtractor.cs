// Harmony.Format.Execution.Preflight/HarmonyToolDependencyExtractor.cs
using System.Text.Json;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Preflight;

public static class HarmonyToolDependencyExtractor
{
   /// <summary>
   /// Extracts a collection of unique recipient identifiers from the specified HarmonyEnvelope, 
   /// focusing on assistant messages and ToolCallStep recipients.
   /// </summary>
   /// <remarks>This method processes messages marked as 'assistant' with a termination type of 
   /// 'call' and also scans for recipients within ToolCallStep messages in harmony scripts. 
   /// It ignores any messages that cannot be deserialized into a HarmonyScript.</remarks>
   /// <param name="envelope">The HarmonyEnvelope containing messages from which recipient 
   /// identifiers are extracted.</param>
   /// <returns>A read-only collection of strings representing the unique recipient identifiers
   /// found in the envelope.</returns>
   public static IReadOnlyCollection<string> ExtractRecipients(HarmonyEnvelope envelope)
   {
      var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      // Assistant messages with termination=call
      foreach (var r in envelope.Messages
          .Where(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                      && m.Termination == HarmonyTermination.call
                      && !string.IsNullOrWhiteSpace(m.Recipient))
          .Select(m => m.Recipient!))
      {
         recipients.Add(r);
      }

      // harmony-script blocks → ToolCallStep recipients
      foreach (var msg in envelope.Messages)
      {
         if (!IsHarmonyScriptMessage(msg)) continue;

         HarmonyScript? script = null;
         try
         {
            script = msg.Content.Deserialize<HarmonyScript>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
         }
         catch { continue; }

         if (script?.Steps is null) continue;
         CollectStepRecipients(script.Steps, recipients);
      }

      return recipients;

      static bool IsHarmonyScriptMessage(HarmonyMessage msg) =>
          msg.ContentType?.Equals("harmony-script", StringComparison.OrdinalIgnoreCase) == true
          && msg.Content.ValueKind == JsonValueKind.Object;

      static void CollectStepRecipients(IEnumerable<HarmonyStep> steps, HashSet<string> acc)
      {
         foreach (var step in steps)
         {
            if (step is ToolCallStep tc && !string.IsNullOrWhiteSpace(tc.Recipient))
            { acc.Add(tc.Recipient); continue; }
            if (step is IfStep iff)
            {
               if (iff.Then is not null) CollectStepRecipients(iff.Then, acc);
               if (iff.Else is not null) CollectStepRecipients(iff.Else, acc);
            }
         }
      }
   }
}
