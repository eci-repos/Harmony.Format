using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Tooling;

/// <summary>
/// Analyzes a HarmonyEnvelope for tool dependencies
/// and verifies their availability.
/// </summary>
public sealed class HarmonyPreflightAnalyzer
{
   private readonly IHarmonyToolAvailability _toolAvailability;

   public HarmonyPreflightAnalyzer(IHarmonyToolAvailability toolAvailability)
   {
      _toolAvailability = toolAvailability
         ?? throw new ArgumentNullException(nameof(toolAvailability));
   }

   /// <summary>
   /// Analyzes the specified envelope and determines the availability of required tools for each
   /// recipient.
   /// </summary>
   /// <remarks>The method checks the availability of tools for all recipients extracted from the 
   /// envelope. If
   /// any required tools are unavailable, they are added to the MissingRecipients collection in
   /// the result.</remarks>
   /// <param name="envelope">The envelope containing the data and recipients to be analyzed. 
   /// Cannot be null.</param>
   /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.
   /// </param>
   /// <returns>A HarmonyPreflightResult that contains the analysis results, including any missing 
   /// recipients and a readiness message.</returns>
   /// <exception cref="ArgumentNullException">Thrown if <paramref name="envelope"/> is null.
   /// </exception>
   public async Task<HarmonyPreflightResult> AnalyzeAsync(
      HarmonyEnvelope envelope,
      CancellationToken ct = default)
   {
      if (envelope is null)
         throw new ArgumentNullException(nameof(envelope));

      var result = new HarmonyPreflightResult();

      var recipients = ExtractRecipients(envelope);

      result.RequiredRecipients.AddRange(recipients);

      foreach (var r in recipients)
      {
         if (!await _toolAvailability
               .IsAvailableAsync(r, ct)
               .ConfigureAwait(false))
         {
            result.MissingRecipients.Add(r);
         }
      }

      if (!result.IsReady)
      {
         result.Message =
            "One or more required tools are not available.";
      }

      return result;
   }

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
   private static IReadOnlyCollection<string> ExtractRecipients(HarmonyEnvelope envelope)
   {
      var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      // 1) Existing MVP: assistant messages with termination=call
      foreach (var r in envelope.Messages
         .Where(m =>
            string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
            m.Termination == HarmonyTermination.call &&
            !string.IsNullOrWhiteSpace(m.Recipient))
         .Select(m => m.Recipient!))
      {
         recipients.Add(r);
      }

      // 2) NEW: scan harmony-script steps for ToolCallStep recipients
      foreach (var msg in envelope.Messages)
      {
         if (!IsHarmonyScriptMessage(msg))
            continue;

         // Deserialize into HarmonyScript (validation already happened at registration)
         HarmonyScript? script = null;
         try
         {
            script = msg.Content.Deserialize<HarmonyScript>(
               new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
         }
         catch
         {
            // If script can't deserialize, ignore here; execution will surface the error later.
            continue;
         }

         if (script?.Steps is null)
            continue;

         CollectStepRecipients(script.Steps, recipients);
      }

      return recipients.ToList();
   }

   private static bool IsHarmonyScriptMessage(HarmonyMessage msg)
      => msg.ContentType?.Equals("harmony-script", StringComparison.OrdinalIgnoreCase) == true
         && msg.Content.ValueKind == JsonValueKind.Object;

   private static void CollectStepRecipients(
      IEnumerable<HarmonyStep> steps,
      HashSet<string> recipients)
   {
      foreach (var step in steps)
      {
         if (step is null) continue;

         // Tool-call step
         if (step is ToolCallStep tc)
         {
            if (!string.IsNullOrWhiteSpace(tc.Recipient))
               recipients.Add(tc.Recipient);
            continue;
         }

         // If step (walk branches)
         if (step is IfStep iff)
         {
            if (iff.Then is not null) CollectStepRecipients(iff.Then, recipients);
            if (iff.Else is not null) CollectStepRecipients(iff.Else, recipients);
            continue;
         }

         // Future-proofing: if you add other container steps later, handle them here.
      }
   }

}
