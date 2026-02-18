using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Execution.Session;

/// <summary>
/// Selector used by APIs to target the next message to process.
/// </summary>
public sealed class HarmonyMessageSelector
{
   public int? Index { get; init; }
   public string? MessageId { get; init; }

   public static HarmonyMessageSelector AtIndex(int index) => new() { Index = index };
}
