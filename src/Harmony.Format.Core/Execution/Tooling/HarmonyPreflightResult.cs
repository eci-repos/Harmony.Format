using System;
using System.Collections.Generic;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Tooling;

public sealed class HarmonyPreflightResult
{
   public bool IsReady => MissingRecipients.Count == 0;

   public List<string> RequiredRecipients { get; } = new();
   public List<string> MissingRecipients { get; } = new();

   public string? Message { get; set; }
}
