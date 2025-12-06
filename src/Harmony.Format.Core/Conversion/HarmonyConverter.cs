using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

// UNDER CONSTRUCTION **** TODO:

#region -- 4.00 - FormatToJsonConverter --

/// <summary>
/// Converts native HRF text into JSON HRF envelopes (and strongly-typed HarmonyEnvelope).
/// </summary>
public static class FormatToJsonConverter
{
   private static readonly JsonSerializerOptions JsonOpts = new()
   {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = true,
      Converters =
         {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
         }
   };

   /// <summary>
   /// Parses native HRF text (with &lt;|start|&gt;, &lt;|message|&gt;, etc.) into a JSON HRF 
   /// envelope string.</summary>
   /// <param name="hrfText">The raw HRF text.</param>
   /// <param name="hrfVersion">HRF version to stamp into the envelope (default: "1.0").</param>
   /// <returns>A JSON string representing a <see cref="HarmonyEnvelope"/>.</returns>
   public static string ConvertHarmonyTextToEnvelopeJson(string hrfText, string hrfVersion = "1.0")
   {
      if (string.IsNullOrWhiteSpace(hrfText))
         throw new ArgumentException("HRF text is empty.", nameof(hrfText));

      var parser = new HarmonyParser();
      var conversation = parser.ParseConversation(hrfText);

      var envelope = new HarmonyEnvelope
      {
         HRFVersion = hrfVersion,
         Messages = conversation.Messages
      };

      var json = JsonSerializer.Serialize(envelope, JsonOpts);
      return json;
   }

   /// <summary>
   /// Parses native HRF text into a strongly-typed <see cref="HarmonyEnvelope"/>.
   /// </summary>
   /// <param name="hrfText">The raw HRF text.</param>
   /// <param name="hrfVersion">HRF version to stamp into the envelope (default: "1.0").</param>
   /// <returns>A populated <see cref="HarmonyEnvelope"/> instance.</returns>
   public static HarmonyEnvelope ConvertHrfTextToEnvelope(string hrfText, string hrfVersion = "1.0")
   {
      var json = ConvertHarmonyTextToEnvelopeJson(hrfText, hrfVersion);
      return JsonSerializer.Deserialize<HarmonyEnvelope>(json, JsonOpts)
         ?? throw new InvalidOperationException("Failed to deserialize HRF envelope from JSON.");
   }
}

#endregion
#region -- 4.00 - JsonToFormatConverter --

/// <summary>
/// Converts JSON HRF envelopes (and HarmonyEnvelope instances) back into native HRF text.
/// </summary>
public static class JsonToFormatConverter
{
   private static readonly JsonSerializerOptions JsonOpts = new()
   {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      WriteIndented = true
   };

   /// <summary>
   /// Converts a JSON HRF envelope string back into native HRF text representation.
   /// </summary>
   /// <param name="envelopeJson">The JSON string representing a <see cref="HarmonyEnvelope"/>.
   /// </param>
   /// <returns>Native HRF text using &lt;|start|&gt; / &lt;|message|&gt; / &lt;|end|&gt; etc.
   /// </returns>
   public static string ConvertEnvelopeJsonToHarmonyText(string envelopeJson)
   {
      if (string.IsNullOrWhiteSpace(envelopeJson))
         throw new ArgumentException("Envelope JSON is empty.", nameof(envelopeJson));

      var envelope = JsonSerializer.Deserialize<HarmonyEnvelope>(envelopeJson, JsonOpts)
         ?? throw new InvalidOperationException("Failed to deserialize HarmonyEnvelope from JSON.");

      return ConvertEnvelopeToHarmonyText(envelope);
   }

   /// <summary>
   /// Converts a <see cref="HarmonyEnvelope"/> instance back into native HRF text.
   /// </summary>
   /// <param name="envelope">The envelope to serialize.</param>
   /// <returns>Native HRF text.</returns>
   public static string ConvertEnvelopeToHarmonyText(HarmonyEnvelope envelope)
   {
      if (envelope is null)
         throw new ArgumentNullException(nameof(envelope));

      var sb = new StringBuilder();

      foreach (var msg in envelope.Messages)
      {
         AppendMessageAsNativeHrf(sb, msg);
         sb.AppendLine(); // blank line between messages for readability
      }

      return sb.ToString();
   }

   /// <summary>
   /// Renders a single <see cref="HarmonyMessage"/> as native HRF text into the given
   /// StringBuilder. Format is the inverse of what <see cref="HarmonyParser"/> expects.
   /// </summary>
   private static void AppendMessageAsNativeHrf(StringBuilder sb, HarmonyMessage msg)
   {
      if (msg is null) throw new ArgumentNullException(nameof(msg));

      // <|start|>
      sb.AppendLine(HarmonyTokens.Start);

      // ROLE line (raw string)
      sb.AppendLine(msg.Role ?? string.Empty);

      // Optional <|channel|> header
      if (msg.Channel != null)
      {
         var channelName = 
            msg.Channel.ToString().ToLowerInvariant(); // analysis | commentary | final
         sb.AppendLine(HarmonyTokens.Channel);
         sb.Append(channelName);

         if (!string.IsNullOrWhiteSpace(msg.Recipient))
         {
            sb.Append(" to=");
            sb.Append(msg.Recipient);
         }

         sb.AppendLine();
      }

      // Optional <|constrain|> contentType
      if (!string.IsNullOrWhiteSpace(msg.ContentType))
      {
         sb.AppendLine(HarmonyTokens.Constrain);
         sb.AppendLine(msg.ContentType);
      }

      // <|message|>
      sb.AppendLine(HarmonyTokens.Message);

      // Content: depends on contentType
      string contentText = RenderContentForNative(msg);
      sb.AppendLine(contentText);

      // Termination token
      var termToken = msg.Termination switch
      {
         HarmonyTermination.End => HarmonyTokens.End,
         HarmonyTermination.Call => HarmonyTokens.Call,
         HarmonyTermination.Return => HarmonyTokens.Return,
         null => HarmonyTokens.End, // default to <|end|> if not specified
         _ => HarmonyTokens.End
      };

      sb.AppendLine(termToken);
   }

   /// <summary>
   /// Renders the content field of a message as plain text for HRF.
   /// If contentType is json or harmony-script, we emit JSON; otherwise, we emit the raw string.
   /// </summary>
   private static string RenderContentForNative(HarmonyMessage msg)
   {
      var contentType = msg.ContentType?.Trim().ToLowerInvariant();

      if (!string.IsNullOrWhiteSpace(contentType) &&
          (contentType == HarmonyConstants.ContentTypeJson || 
          contentType == HarmonyConstants.ContentTypeScript))
      {
         // Serialize the JsonElement back to JSON text
         return JsonSerializer.Serialize(msg.Content, JsonOpts);
      }

      // Plain-text: content is expected to be a JSON string
      if (msg.Content.ValueKind == JsonValueKind.String)
      {
         return msg.Content.GetString() ?? string.Empty;
      }

      // Fallback: serialize as JSON for unexpected kinds
      return JsonSerializer.Serialize(msg.Content, JsonOpts);
   }

}

#endregion
