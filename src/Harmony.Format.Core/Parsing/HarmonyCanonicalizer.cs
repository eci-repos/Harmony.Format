
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

// TODO: Consider making this an instance with configuration options if needed.
//       Under current design and consideration...

/// <summary>
/// Produces a schema-shaped JSON instance from a parsed HarmonyConversation.
/// Ensures explicit defaults (contentType, termination) and removes layout artifacts.
/// </summary>
public static class HarmonyCanonicalizer
{

   /// <summary>
   /// Canonical DTOs that match your JSON Schema "Message" shape.
   /// </summary>
   public sealed class CanonicalConversation
   {
      public List<CanonicalMessage> messages { get; } = new();
   }

   public sealed class CanonicalMessage
   {
      public string role { get; set; } = string.Empty;    // required
      public string channel { get; set; } = string.Empty; // required
      public string? recipient { get; set; }              // only for assistant/commentary
      public string? contentType { get; set; }            // present in all canonical outputs
      public string? termination { get; set; }            // only for assistant/commentary
      public object? content { get; set; }         // string | object (JSON) | HarmonyScript object
   }

   /// <summary>
   /// Canonicalize a parsed conversation to the schema instance.
   /// </summary>
   public static CanonicalConversation ToCanonical(HarmonyConversation convo)
   {
      if (convo == null) throw new ArgumentNullException(nameof(convo));

      var canon = new CanonicalConversation();

      foreach (var m in convo.Messages)
      {
         // Normalize role & channel
         var role = NormalizeRole(m.Role);
         var channel = MapChannel(m.Channel) ?? InferChannelFallback(role);

         // Decide termination for assistant/commentary
         var termination = DeriveTerminationString(role, channel, m.Termination);

         // Decide contentType (explicit <|constrain|> wins; else infer)
         var effectiveContentType = DeriveContentType(role, channel, m.ContentType, m.Content);

         // Normalize content shape to match discriminator
         var contentValue = NormalizeContentValue(effectiveContentType, m.Content);

         // Build canonical message
         var cm = new CanonicalMessage
         {
            role = role,
            channel = channel,
            // recipient only when present (schema requires it for assistant/commentary tool calls)
            recipient = string.IsNullOrWhiteSpace(m.Recipient) ? null : m.Recipient,
            contentType = effectiveContentType,
            // termination only for assistant/commentary; omit elsewhere
            termination = (role == "assistant" && channel == "commentary") ? termination : null,
            content = contentValue
         };

         // Ensure assistant/commentary compliance: recipient + termination required by
         // your schema branch
         if (role == "assistant" && channel == "commentary")
         {
            if (string.IsNullOrWhiteSpace(cm.recipient))
               throw new FormatException(
                  "Assistant commentary requires a recipient (plugin.function).");

            if (string.IsNullOrWhiteSpace(cm.termination))
               throw new FormatException(
                  "Assistant commentary requires termination {call, return, end}.");
         }

         // For non-assistant frames, recipient/termination must be omitted to
         // avoid unevaluatedProperties issues.
         if (role != "assistant" || channel != "commentary")
         {
            cm.recipient = cm.recipient; // allowed but typically null
            cm.termination = null;       // force omission
         }

         canon.messages.Add(cm);
      }

      return canon;
   }

   /// <summary>
   /// Serialize the canonical conversation with relaxed escaping (no \u003E).
   /// </summary>
   public static string Serialize(CanonicalConversation canonical)
   {
      var options = new JsonSerializerOptions
      {
         Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
         WriteIndented = true
      };
      return JsonSerializer.Serialize(canonical, options);
   }

   #region -- 4.00 - Helpers

   private static string NormalizeRole(string role)
       => (role ?? string.Empty).Trim().ToLowerInvariant();

   private static string? MapChannel(HarmonyChannel? ch) => ch switch
   {
      HarmonyChannel.analysis => HarmonyConstants.ChannelAnalysis,
      HarmonyChannel.commentary => HarmonyConstants.ChannelCommentary,
      HarmonyChannel.final => HarmonyConstants.ChannelFinal,
      null => null,
      _ => null
   };

   /// <summary>
   /// If a channel is missing (should be rare), produce a safe default to satisfy "required".
   /// </summary>
   private static string InferChannelFallback(string role)
   {
      // In HRF, non-assistant frames often use "analysis"; assistant without explicit channel -> "final".
      return role == "assistant"
          ? HarmonyConstants.ChannelFinal
          : HarmonyConstants.ChannelAnalysis;
   }

   /// <summary>
   /// Map parser termination to schema string; default "end" for assistant/commentary when null.
   /// </summary>
   private static string? DeriveTerminationString(
      string role, string channel, HarmonyTermination? term)
   {
      if (role != "assistant" || channel != "commentary")
         return null;

      // Your schema allows "call","return","end"
      return term switch
      {
         HarmonyTermination.call => "call",
         HarmonyTermination.@return => "return",
         // Parser did not carry "end": schema still requires a non-null termination;
         // default to "end"
         null => "end",
         _ => "end"
      };
   }

   /// <summary>
   /// Decide the effective contentType
   /// (explicit wins; else infer from role/channel/termination/body).
   /// </summary>
   private static string DeriveContentType(
      string role, string channel, string? contentType, JsonElement body)
   {
      if (!string.IsNullOrWhiteSpace(contentType))
         return contentType.Trim().ToLowerInvariant();

      // No <|constrain|>: choose sensible defaults.
      if (role == "assistant" && channel == "commentary")
      {
         // If body looks like Harmony Script, prefer "harmony-script";
         // else if object, "json"; else "text".
         if (LooksLikeHarmonyScript(body)) return HarmonyConstants.ContentTypeScript;
         if (IsJsonObjectOrArray(body)) return HarmonyConstants.ContentTypeJson;
         return HarmonyConstants.ContentTypeText;
      }

      // Final/analysis/system/user -> text by default
      return HarmonyConstants.ContentTypeText;
   }

   /// <summary>
   /// Normalize content to match discriminator: "text" =>
   /// string; "json"/"harmony-script" => object/array.
   /// Trims only outer CRLFs for string content; keeps internal whitespace.
   /// </summary>
   private static object? NormalizeContentValue(string effectiveType, JsonElement content)
   {
      switch (effectiveType)
      {
         case "text":
            // Parser stores text as a JSON string element; get raw string and trim outer CRLFs
            var s = content.ValueKind == JsonValueKind.String
                ? content.GetString() ?? string.Empty
                : content.GetRawText(); // fallback if something odd passed through

            // Trim only outer CRLFs injected by token layout
            s = s.Trim('\r', '\n');
            return s;

         case "json":
         case "harmony-script":
            // Preserve structured JSON exactly—deserialize to object for the schema instance
            return DeserializeUntyped(content);

         default:
            // Unknown type: fall back to string; safer for schema anyOf branch
            // when contentType is omitted
            var fallback = content.ValueKind == JsonValueKind.String
                ? content.GetString() ?? string.Empty
                : content.GetRawText();
            return fallback.Trim('\r', '\n');
      }
   }

   private static bool IsJsonObjectOrArray(JsonElement e)
       => e.ValueKind == JsonValueKind.Object || e.ValueKind == JsonValueKind.Array;

   /// <summary>
   /// Lightweight heuristic: Harmony Script often has a top-level "steps" array and "type" keys.
   /// </summary>
   private static bool LooksLikeHarmonyScript(JsonElement e)
   {
      if (e.ValueKind != JsonValueKind.Object) return false;

      // Peek for "steps" property
      if (e.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
         return true;

      // Otherwise scan for common orchestration markers
      foreach (var prop in e.EnumerateObject())
      {
         if (prop.NameEquals("type") ||
             prop.NameEquals("recipient") ||
             prop.NameEquals("args") ||
             prop.NameEquals("then") ||
             prop.NameEquals("else"))
            return true;
      }
      return false;
   }

   /// <summary>
   /// Turn a JsonElement into an untyped .NET object that JsonSerializer can emit as 
   /// JSON object/array.
   /// </summary>
   private static object? DeserializeUntyped(JsonElement element)
   {
      // Use GetRawText -> re-parse into Dictionary<object> / List<object> for clean emission
      using var doc = JsonDocument.Parse(element.GetRawText());
      return ToUntyped(doc.RootElement);
   }

   private static object? ToUntyped(JsonElement el)
   {
      switch (el.ValueKind)
      {
         case JsonValueKind.Object:
            var dict = new Dictionary<string, object?>();
            foreach (var p in el.EnumerateObject())
               dict[p.Name] = ToUntyped(p.Value);
            return dict;

         case JsonValueKind.Array:
            var list = new List<object?>();
            foreach (var item in el.EnumerateArray())
               list.Add(ToUntyped(item));
            return list;

         case JsonValueKind.String:
            return el.GetString();

         case JsonValueKind.Number:
            if (el.TryGetInt64(out var i64)) return i64;
            if (el.TryGetDouble(out var dbl)) return dbl;
            return JsonSerializer.Deserialize<object>(el.GetRawText());

         case JsonValueKind.True:
         case JsonValueKind.False:
            return el.GetBoolean();

         case JsonValueKind.Null:
            return null;

         default:
            return JsonSerializer.Deserialize<object>(el.GetRawText());
      }
   }

   #endregion

}
