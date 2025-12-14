// -------------------------------------------------------------------------------------------------
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harmony.Format.Core;


/// <summary>
/// Raw Harmony roles include fixed roles and tool-named roles (e.g., "functions.getweather").
/// We keep Role as string to preserve tool role names exactly as emitted. (Spec) 
/// </summary>
public sealed class HarmonyMessage
{

   /// <summary>
   /// Gets the role associated with the entity, indicating its function or identity within the
   /// system. For example, standard roles include "user", "assistant", and "system".
   /// </summary>
   /// <remarks>The role determines the context in which the entity operates. Custom roles, such 
   /// as tool names, may also be used.</remarks>
   [JsonPropertyName("role")]
   public required string Role { get; set; }

   /// <summary>
   /// Gets the communication channel associated with the current instance. 
   /// Required for assistant/tool messages per spec.
   /// </summary>
   [JsonPropertyName("channel")]
   public HarmonyChannel? Channel { get; set; }

   /// <summary>
   /// Gets the recipient identifier associated with the current context.
   /// e.g., "functions.getweather" (when role=assistant & channel=commentary)
   /// </summary>
   [JsonPropertyName("recipient")]
   public string? Recipient { get; init; }

   /// <summary>
   /// Gets the content type associated with the current instance, such as "json".
   /// </summary>
   [JsonPropertyName("contentType")]
   public string? ContentType { get; init; }

   /// <summary>
   /// Allow either string or object content:
   /// - If the content is a simple text message, it can be represented as a string.
   /// - If the content is structured data (e.g., JSON), it can be represented as a JsonElement.
   /// </summary>
   [JsonPropertyName("content")]
   public JsonElement Content { get; init; }

   /// <summary>
   /// Gets the termination status of the harmony process.
   /// from <|return|> | <|call|> | <|end|>
   /// </summary>
   [JsonPropertyName("termination")]
   public HarmonyTermination? Termination { get; set; }

   /// <summary>
   /// Returns a string representation of the object, including the role, channel, and recipient 
   /// information if available.
   /// </summary>
   /// <returns>A string that represents the current object, formatted as 
   /// "Role [Channel] to=Recipient" if a recipient is specified, or "Role [Channel]"
   /// otherwise.</returns>
   public override string ToString() => 
      $"{Role} [{Channel}] {(Recipient != null ? "to=" + Recipient : "")}";

}


