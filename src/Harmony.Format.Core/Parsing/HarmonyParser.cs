using System.Text.Json;
using System.Text.RegularExpressions;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

/// <summary>
/// Provides functionality to parse structured conversation data from tokenized text input.
/// </summary>
/// <remarks>The <see cref="HarmonyParser"/>class is designed to process text-based input that 
/// follows a specific tokenized format, extracting messages and their associated metadata. 
/// It supports parsing headers, message content, and termination tokens to construct a structured 
/// representation of a conversation. This class is intended for use in scenarios where 
/// conversations are represented in a predefined tokenized format.
/// 
/// This version preserves JSON content faithfully:
/// - If a message is constrained with content type "json" or "harmony-script", the message body
///   between &lt;|message|&gt; and the terminator is parsed as JSON and stored as a JsonElement
///   with the original JSON structure.
/// - For all other content types (or no content type), the message body is preserved as raw
///   text and wrapped as a JSON string.
/// </remarks>
public class HarmonyParser
{

   /// <summary>
   /// Represents an array of predefined header tokens used to identify specific sections or
   /// delimiters in a message.
   /// </summary>
   /// <remarks>The array contains tokens such as <see cref="HarmonyTokens.Channel"/>, 
   /// <see cref="HarmonyTokens.Constrain"/>,  and 
   /// <see cref="HarmonyTokens.Message"/>. These tokens are used to parse or process headers 
   /// in a structured format.</remarks>
   private static readonly string[] HeaderBreakers = new[]
   {
      HarmonyTokens.Channel, HarmonyTokens.Constrain, HarmonyTokens.Message
   };

   /// <summary>
   /// Represents the set of terminator tokens used to identify the end of a message sequence.
   /// </summary>
   /// <remarks>This array contains predefined tokens that signify the conclusion of a message,
   /// such as  <see cref="HarmonyTokens.End"/>, <see cref="HarmonyTokens.Call"/>, and 
   /// <see cref="HarmonyTokens.Return"/>. These tokens are used to determine when processing 
   /// of a message should stop.</remarks>
   private static readonly string[] AfterMessageTerminators = new[]
   {
      HarmonyTokens.End, HarmonyTokens.Call, HarmonyTokens.Return
   };

   #region -- 4.00 - Is a...; Content Looks Like 

   private static bool IsTerminator(string token) =>
      token == HarmonyTokens.End ||
      token == HarmonyTokens.Call ||
      token == HarmonyTokens.Return;

   private static bool IsAssistantRole(string role) =>
      role.Equals(HarmonyConstants.RoleAssistant, StringComparison.OrdinalIgnoreCase);

   private static bool LooksLikeJsonObject(string s)
   {
      // Trim outer CRLFs first
      s = s.Trim('\r', '\n', ' ', '\t');
      return s.StartsWith("{", StringComparison.Ordinal);
   }

   private static bool LooksLikeHarmonyScript(string s)
   {
      // Heuristic: JSON object containing a "steps" array or typical script keys
      s = s.Trim('\r', '\n', ' ', '\t');
      if (!s.StartsWith("{", StringComparison.Ordinal)) return false;
      return s.Contains("\"steps\"", StringComparison.Ordinal) ||
             s.Contains("\"type\": \"tool-call\"", StringComparison.Ordinal) ||
             s.Contains("\"type\": \"if\"", StringComparison.Ordinal);
   }

   #endregion

   /// <summary>
   /// Channel resolution:
   /// - Channel is only meaningful for Assistant frames.
   /// - If channel is omitted for Assistant, default to Final (safest user-facing fallback),
   ///   unless the caller indicates the content is tool-related, then Commentary.
   /// - For non-Assistant roles, treat as Unspecified (channel not applicable).
   /// </summary>
   /// <param name="role">Parsed role.</param>
   /// <param name="parsedChannel">Channel parsed from the HRF header, or Unspecified if absent.
   /// </param>
   /// <param name="looksLikeToolCall">
   /// True if the frame body looks like a tool/function call envelope (your heuristics).
   /// </param>
   public static HarmonyChannel? ResolveChannel(
      string role, HarmonyChannel? parsedChannel, bool looksLikeToolCall = false)
   {
      // If the sender isn't the assistant, channel is not applicable in practice.
      if (!IsAssistantRole(role))
         return parsedChannel;

      // If channel is provided, trust it (but you could validate against allowed set).
      if (parsedChannel is not null)
         return parsedChannel;

      // Channel omitted: choose a safe default.
      return looksLikeToolCall ? HarmonyChannel.commentary : HarmonyChannel.final;
   }

   /// <summary>
   /// Parses a conversation from the specified text input, extracting messages and their 
   /// associated metadata.
   /// </summary>
   /// <remarks>
   /// Each message in the conversation includes metadata such as the role, channel, recipient, 
   /// content type, and termination type. The method expects the input text to follow a specific
   /// tokenized format, and it will throw exceptions if the format is invalid.
   /// 
   /// JSON preservation behavior:
   /// - When the header includes <c>&lt;|constrain|&gt; json</c> or <c>harmony-script</c>, the
   ///   message block is parsed as JSON and stored as a structured <see cref="JsonElement"/>.
   /// - Otherwise, the message block is stored as a JSON string with the raw text preserved.
   /// </remarks>
   /// <param name="text">The input text containing the conversation data to parse. 
   /// Must not be null, empty, or whitespace.</param>
   /// <returns>
   /// A <see cref="HarmonyConversation"/> object containing the parsed messages and 
   /// their metadata.
   /// </returns>
   /// <exception cref="ArgumentException">Thrown if <paramref name="text"/> is null, empty, 
   /// or consists only of whitespace.</exception>
   /// <exception cref="FormatException">
   /// Thrown if the input text is missing required tokens, 
   /// such as &lt;|message|&gt; or a valid terminator token 
   /// (&lt;|end|&gt;, &lt;|call|&gt;, or &lt;|return|&gt;), or if a message
   /// constrained as JSON contains invalid JSON.
   /// </exception>
   public HarmonyConversation ParseConversation(string text)
   {
      if (string.IsNullOrWhiteSpace(text))
         throw new ArgumentException("Input is empty", nameof(text));

      var convo = new HarmonyConversation();
      int pos = 0;
      while (TryFind(text, HarmonyTokens.Start, pos, out int startIdx))
      {
         pos = startIdx + HarmonyTokens.Start.Length;

         // Parse header: role, then (syntactically) optional <|channel|> + optional to=…,
         // optional <|constrain|> contentType
         var (role, channel, recipient, contentType, nextPosAfterHeader) =
             ParseHeader(text, pos);

         // Validate channel presence (previous implementation assumed channel.Value)
         // if role is "assistant" then channel is required - default to "final" if missing
         HarmonyChannel? resolvedChannel = ResolveChannel(role, channel);

         // Next must be <|message|>
         if (!TryFind(text, HarmonyTokens.Message, nextPosAfterHeader, out int msgIdx))
            throw new FormatException("Missing <|message|> token");
         int contentStart = msgIdx + HarmonyTokens.Message.Length;

         // Read content until any of the terminators
         var (contentRaw, termToken, nextPosAfterContent) =
            ReadUntilAny(text, contentStart, AfterMessageTerminators);
         if (termToken is null)
            throw new FormatException("Missing terminator token (<|end|>|<|call|>|<|return|>)");

         // Remove outer CRLFs only (do not touch inner whitespace)
         contentRaw = contentRaw.Trim('\r', '\n');

         // Decide default contentType when <|constrain|> is absent
         string effectiveContentType;

         if (!string.IsNullOrWhiteSpace(contentType))
         {
            // Honor explicit <|constrain|> value
            effectiveContentType = contentType;
         }
         else
         {
            // No explicit constrain: infer from role/channel/termination
            if (IsAssistantRole(role))
            {
               if (resolvedChannel == HarmonyChannel.commentary)
               {
                  // Commentary + call/return => JSON
                  if (termToken == HarmonyTokens.Call || termToken == HarmonyTokens.Return)
                  {
                     effectiveContentType = HarmonyConstants.ContentTypeJson; // "json"
                  }
                  else
                  {
                     // No semantic termination (end): pick based on body heuristics
                     // If you have a detector, use it; otherwise conservative default:
                     // e.g., if looksLikeToolCall => "json";
                     //       if looksLikePlan => "harmony-script"; else "text"
                     bool looksLikePlan = LooksLikeHarmonyScript(contentRaw);
                     bool looksLikeArgs = LooksLikeJsonObject(contentRaw);

                     effectiveContentType = looksLikePlan
                         ? HarmonyConstants.ContentTypeScript        // "harmony-script"
                         : looksLikeArgs
                             ? HarmonyConstants.ContentTypeJson      // "json"
                             : HarmonyConstants.ContentTypeText;     // "text"
                  }
               }
               else
               {
                  // assistant + analysis/final => text
                  effectiveContentType = HarmonyConstants.ContentTypeText; // "text"
               }
            }
            else
            {
               // user/system => text
               effectiveContentType = HarmonyConstants.ContentTypeText; // "text"
            }
         }

         // Now use effectiveContentType everywhere
         var contentElement = ParseContentElement(contentRaw, effectiveContentType);

         // Create message
         var message = new HarmonyMessage
         {
            Role = role,
            Channel = (resolvedChannel.HasValue ? resolvedChannel.Value : null),
            Recipient = recipient,
            ContentType = effectiveContentType,
            Content = contentElement,

            // Map termination only for CALL/RETURN; <|end|> is just a delimiter,
            // not semantic termination.  Only allow Termination
            // when resolvedChannel == HarmonyChannel.commentary (tool calls)
            Termination = (IsAssistantRole(role) && 
               resolvedChannel == HarmonyChannel.commentary)
            ?  termToken switch
            {
               var t when t == HarmonyTokens.Call => HarmonyTermination.call,
               var t when t == HarmonyTokens.Return => HarmonyTermination.@return,
               var t when t == HarmonyTokens.End => HarmonyTermination.end,
               _ => (HarmonyTermination?)null
            }
            : (HarmonyTermination?)null
         };

         convo.Messages.Add(message);
         pos = nextPosAfterContent; // continue
      }

      return convo;
   }

   /// <summary>
   /// Parses a header from the specified text starting at the given position.
   /// </summary>
   /// <param name="text">The input text containing the header to parse.</param>
   /// <param name="pos">The starting position in the text where the header parsing begins.</param>
   /// <returns>A tuple containing the following elements: 
   ///    <list type="bullet"> 
   ///       <item><description>The role as a non-empty, trimmed string.</description></item>
   ///       <item><description>The channel as a <see cref="HarmonyChannel"/> object, or
   ///          <see langword="null"/> if no channel is specified.</description></item> 
   ///       <item><description>The recipient as a string, or <see langword="null"/> if no recipient
   ///          is specified.</description></item> 
   ///       <item><description>The content type as a string, or <see langword="null"/> 
   ///          if no content type is specified.</description></item>
   ///       <item><description>The position in the text immediately following the parsed header.
   ///          </description></item> 
   ///    </list>
   /// </returns>
   /// <exception cref="FormatException">Thrown if the header is missing a role, or if the header
   /// does not lead to the expected <c>&lt;|message|&gt;</c> token.</exception>
   private static (string role, HarmonyChannel? channel, string? recipient, string? contentType,
      int nextPos) ParseHeader(string text, int pos)
   {
      // ROLE is the next raw text until we see <|channel|>, <|constrain|>, or <|message|>
      var (roleRaw, nextToken, p1) = ReadUntilAny(text, pos, HeaderBreakers);

      if (string.IsNullOrWhiteSpace(roleRaw))
         throw new FormatException("Missing role in header");
      string role = roleRaw.Trim();

      HarmonyChannel? channel = null;
      string? recipient = null;
      string? contentType = null;

      int p = p1;

      // If nextToken is <|channel|>, read channel name and optional "to=…"
      if (nextToken == HarmonyTokens.Channel)
      {
         // Channel name until next token (<|constrain|> or <|message|>)
         var (chanRaw, next2, p2) = ReadUntilAny(text, p, new[]
         {
            HarmonyTokens.Constrain, HarmonyTokens.Message
         });
         (channel, recipient) = ParseChannelAndRecipient(chanRaw);
         p = p2;
         nextToken = next2;
      }

      // If we now see <|constrain|>, the next tokenized word (up to <|message|>) is
      // the content type (e.g., "json")
      if (nextToken == HarmonyTokens.Constrain)
      {
         // Move past the sentinel before reading
         p += HarmonyTokens.Constrain.Length;

         var (ctype, next3, p3) = ReadUntilAny(text, p, new[] { HarmonyTokens.Message });
         contentType = ctype.Trim();
         p = p3;
         nextToken = next3; // will be <|message|>
      }

      // Must be positioned before <|message|>
      if (nextToken != HarmonyTokens.Message)
         throw new FormatException("Header did not lead to <|message|>");

      return (role, channel, recipient, contentType, p);
   }

   /// <summary>
   /// Parses the specified header text to extract the channel and recipient information.
   /// </summary>
   /// <remarks>The method expects the header text to be a whitespace-separated string where 
   /// the first part represents the channel and an optional "to=recipientName" part specifies
   /// the recipient. Supported channel names are "analysis", "commentary", and "final". 
   /// The channel name is case-insensitive.</remarks>
   /// <param name="headerText">The header text to parse. This should be a string containing a 
   /// channel name optionally followed by a recipient specified using the format 
   /// "to=recipientName".</param>
   /// <returns>A tuple containing the parsed channel and recipient: 
   ///    <list type="bullet"> 
   ///       <item><description>The first item is a <see cref="HarmonyChannel"/> representing 
   ///          the parsed channel, or <see langword="null"/> if no channel is
   ///          found.</description></item> 
   ///       <item><description>The second item is a <see cref="string"/> representing the
   ///          recipient, or <see langword="null"/> if no recipient is specified.
   ///          </description></item> 
   ///    </list>
   /// </returns>
   /// <exception cref="FormatException">Thrown if the channel specified in the header text is
   /// unrecognized.</exception>
   private static (HarmonyChannel? channel, string? recipient) ParseChannelAndRecipient(string headerText)
   {
      // Accept headerText like:
      //   "<|channel|>commentary to=functions.getweather"
      //   "<|channel|>\ncommentary\nto=functions.getweather"
      //   "commentary to=functions.getweather"
      //   "analysis" | "final"
      var text = (headerText ?? string.Empty).Trim();
      if (text.Length == 0) return (null, null);

      // Normalize whitespace (incl. newlines/tabs)
      var parts = Regex.Split(text, @"\s+");

      string? chanToken = null;
      string? recipient = null;

      for (int i = 0; i < parts.Length; i++)
      {
         var part = parts[i];

         // If the channel sentinel appears as its own token, take the next token as channel.
         if (string.Equals(part, HarmonyTokens.Channel, StringComparison.Ordinal))
         {
            if (i + 1 < parts.Length)
            {
               chanToken = parts[i + 1];
               i++; // skip next; we consumed it as channel value
            }
            continue;
         }

         // If the sentinel is stuck to the value (e.g. "<|channel|>commentary"), strip it.
         if (part.StartsWith(HarmonyTokens.Channel, StringComparison.Ordinal) ||
             part.StartsWith("<|channel|>", StringComparison.Ordinal))
         {
            var stripped = part
                .Replace(HarmonyTokens.Channel, "", StringComparison.Ordinal)
                .Replace("<|channel|>", "", StringComparison.Ordinal)
                .Trim();

            if (!string.IsNullOrEmpty(stripped))
               chanToken = stripped;

            continue;
         }

         if (part.StartsWith("to=", StringComparison.OrdinalIgnoreCase))
         {
            recipient = part.Substring("to=".Length).Trim();
            continue;
         }

         // If we still don't have a channel token, the first non-"to=" token is the channel.
         if (chanToken is null)
            chanToken = part.Trim();
      }

      // Best practice: be tolerant—unknown => Unspecified (don’t explode on new channels)
      HarmonyChannel? channel = (chanToken ?? string.Empty).Trim().ToLowerInvariant() switch
      {
         HarmonyConstants.ChannelAnalysis => HarmonyChannel.analysis,
         HarmonyConstants.ChannelCommentary => HarmonyChannel.commentary,
         HarmonyConstants.ChannelFinal => HarmonyChannel.final,
         "" => null,
         _ => null
      };

      return (channel, recipient);
   }

   /// <summary>
   /// Attempts to find the first occurrence of a specified token in the given text, starting from 
   /// a specified index.</summary>
   /// <param name="text">The text to search within. Cannot be <see langword="null"/>.</param>
   /// <param name="token">The token to search for. Cannot be <see langword="null"/> or empty.
   /// </param>
   /// <param name="start">The zero-based starting index in the text at which to begin the search.
   /// Must be within the bounds of the text.</param>
   /// <param name="idx">When this method returns, contains the zero-based index of the first
   /// occurrence of the token in the text, if found; otherwise, contains -1. This parameter is 
   /// passed uninitialized.</param>
   /// <returns><see langword="true"/> if the token is found in the text; otherwise, 
   /// <see langword="false"/>.
   /// </returns>
   private static bool TryFind(string text, string token, int start, out int idx)
   {
      if (text is null) throw new ArgumentNullException(nameof(text));
      if (string.IsNullOrEmpty(token)) 
         throw new ArgumentException("Token must not be null or empty", nameof(token));
      if (start < 0 || start > text.Length) 
         throw new ArgumentOutOfRangeException(
            nameof(start), "Start must be within the string bounds.");

      idx = text.IndexOf(token, start, StringComparison.Ordinal);
      return idx >= 0;
   }

   /// <summary>
   /// Extracts a segment of the input string up to the first occurrence of any specified token, 
   /// and identifies the matched token and the position of the next character after the match.
   /// </summary>
   /// <remarks>The search is case-sensitive and uses ordinal string comparison. If multiple 
   /// tokens are found, the method returns the segment and token corresponding to the earliest
   /// occurrence.</remarks>
   /// <param name="text">The input string to search.</param>
   /// <param name="start">The starting position in the input string from which to begin the 
   /// search.</param>
   /// <param name="tokens">An array of tokens to search for in the input string.</param>
   /// <returns>A tuple containing the following:
   ///    <list type="bullet"> 
   ///       <item> <description>The segment of the input string from the starting position up to
   ///          the first matched token, or the remainder of the string if no tokens are 
   ///          found.</description></item> 
   ///       <item> <description>The token that was matched, or <see langword="null"/> if no
   ///          tokens were found.</description> </item> 
   ///       <item> <description>The position of the next character after the matched token, or
   ///          the length of the input string if no tokens were found.</description> </item> 
   ///    </list>
   /// </returns>
   private static (string segment, string? matchedToken, int nextPos)
      ReadUntilAny(string text, int start, string[] tokens)
   {
      if (text is null) throw new ArgumentNullException(nameof(text));
      if (tokens is null) throw new ArgumentNullException(nameof(tokens));
      if (start < 0 || start > text.Length) 
         throw new ArgumentOutOfRangeException(
            nameof(start), "Start must be within the string bounds.");

      // Find the earliest occurrence of any token
      int bestIdx = -1;
      string? which = null;
      foreach (var tok in tokens)
      {
         if (string.IsNullOrEmpty(tok)) continue;
         int i = text.IndexOf(tok, start, StringComparison.Ordinal);
         if (i >= 0 && (bestIdx < 0 || i < bestIdx))
         {
            bestIdx = i;
            which = tok;
         }
      }
      if (bestIdx < 0)
      {
         // none found -> rest of string
         return (text.Substring(start), null, text.Length);
      }
      return (text.Substring(start, bestIdx - start), which, bestIdx);
   }

   /// <summary>
   /// Converts a raw message body string into a <see cref="JsonElement"/> according to the
   /// message content type.
   /// </summary>
   /// <remarks>
   /// - If <paramref name="contentType"/> is "json" or "harmony-script" (case-insensitive),
   ///   the raw body is parsed as JSON, preserving the JSON structure.
   /// - Otherwise, the raw body is preserved exactly as text and wrapped as a JSON string.
   /// </remarks>
   /// <param name="rawContent">The raw text between &lt;|message|&gt; and the terminator.</param>
   /// <param name="contentType">The content type specified in the header, if any.</param>
   /// <returns>A <see cref="JsonElement"/> representing either structured JSON or a JSON string.
   /// </returns>
   /// <exception cref="FormatException">
   /// Thrown if the content is marked as JSON but is not valid JSON.
   /// </exception>
   private static JsonElement ParseContentElement(string rawContent, string? contentType)
   {
      if (!string.IsNullOrWhiteSpace(contentType) &&
          (contentType.Equals(
             HarmonyConstants.ContentTypeJson, StringComparison.OrdinalIgnoreCase) ||
           contentType.Equals(
              HarmonyConstants.ContentTypeScript, StringComparison.OrdinalIgnoreCase)))
      {
         try
         {
            // Parse as JSON; this preserves the data shape (object/array/primitive)
            using var doc = JsonDocument.Parse(rawContent);
            return doc.RootElement.Clone();
         }
         catch (JsonException ex)
         {
            throw new FormatException(
               $"Content for contentType='{contentType}' is not valid JSON.", ex);
         }
      }

      // For all other content types (or none), treat the body as plain text and
      // preserve it exactly as given, wrapped as a JSON string.
      return JsonSerializer.SerializeToElement(rawContent);
   }

}
