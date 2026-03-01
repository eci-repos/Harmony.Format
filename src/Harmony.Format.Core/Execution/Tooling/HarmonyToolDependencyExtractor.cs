using System.Text.Json;
using Harmony.Tooling.Preflight;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Preflight;

/// <summary>
/// HRF-specific implementation of IToolDependencyExtractor.
/// Extracts unique tool recipients (e.g., "plugin.function") from a HarmonyEnvelope:
///  - assistant messages with termination = call
///  - recipients in harmony-script ToolCallStep steps (including nested If/Then/Else)
/// </summary>
public sealed class HarmonyToolDependencyExtractor : IToolDependencyExtractor
{
   /// <inheritdoc />
   public Task<IReadOnlyCollection<string>> ExtractRecipientsAsync(
      object script, CancellationToken ct = default)
   {
      if (script is null) throw new ArgumentNullException(nameof(script));
      ct.ThrowIfCancellationRequested();

      if (script is not HarmonyEnvelope envelope)
         throw new ArgumentException("Script must be a HarmonyEnvelope instance.", nameof(script));

      var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      // 1) Assistant messages with termination = call
      foreach (var r in envelope.Messages
               .Where(m =>
                   string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                   m.Termination == HarmonyTermination.call &&
                   !string.IsNullOrWhiteSpace(m.Recipient))
               .Select(m => m.Recipient!))
      {
         recipients.Add(r);
      }

      // 2) harmony-script blocks → ToolCallStep recipients
      foreach (var msg in envelope.Messages)
      {
         if (!IsHarmonyScriptMessage(msg)) continue;

         HarmonyScript? scriptBlock = null;
         try
         {
            scriptBlock = msg.Content.Deserialize<HarmonyScript>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
         }
         catch
         {
            // Ignore deserialization failures here;
            // execution/validation will surface them elsewhere.
            continue;
         }

         if (scriptBlock?.Steps is null) continue;
         CollectStepRecipients(scriptBlock.Steps, recipients);
      }

      // Return as Task
      return Task.FromResult<IReadOnlyCollection<string>>(recipients);

      // ---- local helpers ----
      static bool IsHarmonyScriptMessage(HarmonyMessage msg) =>
          msg.ContentType?.Equals("harmony-script", StringComparison.OrdinalIgnoreCase) == true
          && msg.Content.ValueKind == JsonValueKind.Object;

      static void CollectStepRecipients(IEnumerable<HarmonyStep> steps, HashSet<string> acc)
      {
         foreach (var step in steps)
         {
            if (step is null) continue;

            if (step is ToolCallStep tc && !string.IsNullOrWhiteSpace(tc.Recipient))
            {
               acc.Add(tc.Recipient);
               continue;
            }

            if (step is IfStep iff)
            {
               if (iff.Then is not null) CollectStepRecipients(iff.Then, acc);
               if (iff.Else is not null) CollectStepRecipients(iff.Else, acc);
            }

            // Future: add other container steps here when introduced.
         }
      }
   }
}
