using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

public sealed class HarmonyConversation
{
   [JsonPropertyName("messages")]
   public List<HarmonyMessage> Messages { get; set; } = new();
}
