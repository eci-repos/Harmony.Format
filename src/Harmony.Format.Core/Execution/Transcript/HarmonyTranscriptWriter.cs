using System;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Transcript;

public static class HarmonyTranscriptWriter
{
   public static string NormalizeRole(string role)
   {
      if (string.IsNullOrWhiteSpace(role)) return "system";
      return role.Trim().ToLowerInvariant();
   }

   public static string ToolSummary(string recipient, bool succeeded, TimeSpan? duration = null)
   {
      var status = succeeded ? "ok" : "failed";
      var ms = duration.HasValue ? $" ({(int)duration.Value.TotalMilliseconds}ms)" : string.Empty;
      return $"[tool:{recipient}] {status}{ms}";
   }

   public static string PreflightBlockedSummary(int missingCount)
      => $"[preflight] blocked: missing {missingCount} required tool(s)";
}
