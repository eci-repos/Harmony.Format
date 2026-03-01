using Harmony.Format.Execution.Preflight;
using Harmony.Tooling.Contracts;
using Harmony.Tooling.Discovery;
using Harmony.Tooling.Preflight;

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
   private readonly IToolAvailability _toolAvailability;
   private readonly IToolDependencyExtractor _toolDependencyExtractor = 
      new HarmonyToolDependencyExtractor();

   public HarmonyPreflightAnalyzer(IToolAvailability toolAvailability,
      IToolDependencyExtractor? toolDependencyExtractor = null)
   {
      _toolAvailability = toolAvailability
         ?? throw new ArgumentNullException(nameof(toolAvailability));
      _toolDependencyExtractor = toolDependencyExtractor
         ?? _toolDependencyExtractor; // Use default extractor if none provided
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
   public async Task<PreflightReport> AnalyzeAsync(
      HarmonyEnvelope envelope,
      CancellationToken ct = default)
   {
      if (envelope is null)
         throw new ArgumentNullException(nameof(envelope));

      var recipients = await _toolDependencyExtractor.ExtractRecipientsAsync(envelope);

      ToolPreflightAnalyzer analyzer = new(_toolAvailability);
      return await analyzer.AnalyzeAsync(
         recipients, mode: PreflightReport.DefaultPreflightMode, ct);
   }

}
