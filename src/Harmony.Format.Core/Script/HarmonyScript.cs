using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format;

/// <summary>
/// Represents a harmony-script payload containing variables and a sequence of steps for execution 
/// within the Harmony framework.
/// </summary>
/// <remarks>A HarmonyScript defines the structure and data required to execute a scripted workflow,
/// including named variables and ordered steps. Instances of this class are typically deserialized 
/// from system messages with a content type of "harmony-script". The class is immutable after 
/// deserialization and is not thread-safe for modification.</remarks>
public sealed class HarmonyScript
{
   [JsonPropertyName("vars")]
   public Dictionary<string, JsonElement>? Vars { get; set; }

   [JsonPropertyName("steps")]
   public List<HarmonyStep> Steps { get; set; } = new();
}

