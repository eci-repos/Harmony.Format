using System;
using System.Collections.Generic;
using System.Linq;
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
   /// Extracts a distinct collection of recipient identifiers from assistant messages in the 
   /// specified Harmony envelope.
   /// </summary>
   /// <remarks>This method filters messages to include only those from the assistant role 
   /// that have a termination type of 'call' and a non-empty recipient field.</remarks>
   /// <param name="envelope">The HarmonyEnvelope containing messages from which recipients 
   /// will be extracted. Must not be null.</param>
   /// <returns>A read-only collection of strings representing the unique recipients identified 
   /// in the assistant messages. Thecollection will be empty if no valid recipients are found.
   /// </returns>
   private static IReadOnlyCollection<string> ExtractRecipients(
      HarmonyEnvelope envelope)
   {
      // MVP version:
      // Detect assistant messages with termination=call + recipient
      return envelope.Messages
         .Where(m =>
            string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
            m.Termination == HarmonyTermination.call &&
            !string.IsNullOrWhiteSpace(m.Recipient))
         .Select(m => m.Recipient!)
         .Distinct(StringComparer.OrdinalIgnoreCase)
         .ToList();
   }

}
