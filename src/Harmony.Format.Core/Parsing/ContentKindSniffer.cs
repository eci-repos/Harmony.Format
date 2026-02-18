using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format;

public sealed record ContentTypeResult(
    ContentKind Kind,
    double Confidence,
    string? Notes = null
);

public static class ContentTypeSniffer
{
   // Public entry point
   public static ContentTypeResult Detect(string? content)
   {
      if (string.IsNullOrWhiteSpace(content))
         return new(ContentKind.Empty, 1.0, "Empty/whitespace");

      var raw = content!;
      var s = raw.Trim();

      // 1) Strong/validated detections first
      if (LooksLikeJson(s, out var jsonNote))
         return new(ContentKind.Json, 0.98, jsonNote);

      if (LooksLikeXml(s, out var xmlNote))
         return new(ContentKind.Xml, 0.95, xmlNote);

      // 2) High-signal heuristics
      if (LooksLikeHtml(s))
         return new(ContentKind.Html, 0.90, "HTML-like tags detected");

      if (LooksLikeUrlEncodedForm(s))
         return new(ContentKind.UrlEncodedForm, 0.85, "key=value&key2=value2 pattern detected");

      if (LooksLikeCsvOrTsv(raw, out var csvNote))
         return new(ContentKind.CsvTsv, 0.80, csvNote);

      if (LooksLikeYaml(s, out var yamlNote))
         return new(ContentKind.Yaml, 0.75, yamlNote);

      if (LooksLikeMarkdown(raw, out var mdNote))
         return new(ContentKind.Markdown, 0.72, mdNote);

      if (LooksLikeBase64(s, out var b64Note))
         return new(ContentKind.Base64, 0.70, b64Note);

      // 3) Fallbacks
      if (LooksPlainText(raw, out var textNote))
         return new(ContentKind.PlainText, 0.60, textNote);

      return new(ContentKind.Unknown, 0.40, "No strong indicators");
   }

   // ---------- JSON ----------
   private static bool LooksLikeJson(string s, out string note)
   {
      note = "JSON parse succeeded";
      if (s.Length < 2) { note = "Too short for JSON"; return false; }

      // Must start like JSON and actually parse
      var c0 = s[0];
      if (c0 != '{' && c0 != '[') { note = "Does not start with { or ["; return false; }

      try
      {
         using var _ = JsonDocument.Parse(s);
         return true;
      }
      catch (JsonException ex)
      {
         note = $"JSON parse failed: {ex.Message}";
         return false;
      }
   }

   // ---------- XML ----------
   private static bool LooksLikeXml(string s, out string note)
   {
      note = "XML-like";
      if (s.Length < 3) { note = "Too short for XML"; return false; }
      if (!s.StartsWith("<", StringComparison.Ordinal))
      { 
         note = "Does not start with <"; return false;
      }

      // Avoid misclassifying HTML as XML: we still call it XML if it looks like an XML
      // declaration or typical XML doc
      if (s.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
      {
         note = "XML declaration found";
         return true;
      }

      // Basic well-formed-ish check without full XML parsing (fast + safe):
      // - has a closing '>'
      // - has some tag-like structure
      var idx = s.IndexOf('>');
      if (idx < 0) { note = "No closing > found"; return false; }

      // If it starts with "<tag" or "</tag" or "<tag>"
      if (Regex.IsMatch(s, @"^\s*<\?xml\b|^\s*</?[A-Za-z_][\w:\-\.]*\b", 
         RegexOptions.CultureInvariant))
      {
         note = "Tag-like prefix detected";
         return true;
      }

      note = "No clear tag name detected";
      return false;
   }

   // ---------- HTML ----------
   private static bool LooksLikeHtml(string s)
   {
      // Common HTML signals
      if (Regex.IsMatch(s, 
         @"<!doctype\s+html\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
         return true;

      // Strong tag set signals (html/head/body/div/span/script/style)
      if (Regex.IsMatch(s, 
         @"<\s*(html|head|body|div|span|script|style|meta|link|p|a|table|ul|ol|li)\b",
          RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
         return true;

      // If it's tag-heavy and has attributes
      if (Regex.IsMatch(s, @"<\s*[a-z][a-z0-9]*\b[^>]*\b(href|src|class|id|style)\s*=",
          RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
         return true;

      return false;
   }

   // ---------- URL-encoded form ----------
   private static bool LooksLikeUrlEncodedForm(string s)
   {
      // Quick reject: spaces/newlines usually not present
      if (s.Contains('\n') || s.Contains('\r')) return false;

      // Pattern: key=value(&key=value)...
      // allow percent-encoding and plus for spaces
      return Regex.IsMatch(s,
          @"^\s*[^=&\s]+=[^&]*(&[^=&\s]+=[^&]*)*\s*$",
          RegexOptions.CultureInvariant);
   }

   // ---------- CSV/TSV ----------
   private static bool LooksLikeCsvOrTsv(string raw, out string note)
   {
      note = "Delimited text";
      var lines = SplitNonEmptyLines(raw).Take(20).ToArray();
      if (lines.Length < 2) { note = "Not enough lines"; return false; }

      // Detect delimiter by consistency of field counts
      var candidates = new[] { ',', '\t', ';', '|' };

      (char delim, int consistent, int fields)? best = null;

      foreach (var d in candidates)
      {
         // ignore if delimiter never appears
         if (!lines.Any(l => l.Contains(d))) continue;

         int? expected = null;
         int consistent = 0;

         foreach (var line in lines)
         {
            // naive split; best-effort (handles common cases, not full RFC CSV)
            var count = line.Count(ch => ch == d) + 1;
            if (expected is null) expected = count;

            if (count == expected) consistent++;
         }

         if (expected is not null && consistent >= Math.Max(2, (int)(lines.Length * 0.7)))
         {
            if (best is null || consistent > best.Value.consistent)
               best = (d, consistent, expected.Value);
         }
      }

      if (best is null) { note = "No consistent delimiter found"; return false; }

      note = $"Delimiter '{EscapeDelim(best.Value.delim)}' with ~{best.Value.fields} "
         + "fields across {best.Value.consistent}/{lines.Length} lines";
      return true;
   }

   private static string EscapeDelim(char d) => d switch { '\t' => "\\t", _ => d.ToString() };

   // ---------- YAML ----------
   private static bool LooksLikeYaml(string s, out string note)
   {
      note = "YAML-like";
      // YAML often starts with --- or has key: value patterns, lists "- item", or indentation
      // structures.
      if (s.StartsWith("---", StringComparison.Ordinal)) { note = "Starts with ---"; return true; }

      var lines = SplitNonEmptyLines(s).Take(30).ToArray();
      if (lines.Length == 0) { note = "No lines"; return false; }

      int keyValue = 0, listLines = 0, indentLines = 0;

      foreach (var line in lines)
      {
         var t = line.TrimEnd();
         if (Regex.IsMatch(t, @"^\s*#")) continue; // comments
         if (Regex.IsMatch(t, @"^\s*-\s+\S")) listLines++;
         if (Regex.IsMatch(t, @"^\s{2,}\S")) indentLines++;
         if (Regex.IsMatch(t, @"^\s*[A-Za-z0-9_\-""']+\s*:\s*\S?")) keyValue++;
      }

      // Avoid confusing markdown "key: value" in prose: require multiple YAML-ish signals
      var score = (keyValue >= 2 ? 1 : 0) + (listLines >= 2 ? 1 : 0) + (indentLines >= 2 ? 1 : 0);
      if (score >= 2)
      {
         note = $"Signals: keyValue={keyValue}, list={listLines}, indent={indentLines}";
         return true;
      }

      note = $"Weak YAML signals: keyValue={keyValue}, list={listLines}, indent={indentLines}";
      return false;
   }

   // ---------- Markdown ----------
   private static bool LooksLikeMarkdown(string raw, out string note)
   {
      note = "Markdown-like";

      // Check first N lines for common MD constructs
      var lines = SplitNonEmptyLines(raw).Take(40).ToArray();
      if (lines.Length == 0) { note = "No lines"; return false; }

      int hits = 0;

      // headings
      if (lines.Any(l => Regex.IsMatch(l, @"^\s{0,3}#{1,6}\s+\S"))) hits++;

      // fenced code blocks
      if (raw.Contains("```")) hits++;

      // lists
      if (lines.Any(l => Regex.IsMatch(l, @"^\s*([-*+]|\d+\.)\s+\S"))) hits++;

      // blockquotes
      if (lines.Any(l => Regex.IsMatch(l, @"^\s*>\s+\S"))) hits++;

      // links/images
      if (raw.Contains("](") || Regex.IsMatch(raw, @"!\[[^\]]*\]\([^)]+\)")) hits++;

      // tables (pipe tables)
      if (lines.Any(l => l.Count(ch => ch == '|') >= 2) &&
          lines.Any(l => Regex.IsMatch(l, @"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)+\|?\s*$")))
         hits++;

      if (hits >= 2)
      {
         note = $"Markdown signals detected (score={hits})";
         return true;
      }

      note = $"Weak markdown signals (score={hits})";
      return false;
   }

   // ---------- Base64 ----------
   private static bool LooksLikeBase64(string s, out string note)
   {
      note = "Base64-like";
      // Base64 strings are typically long, mostly [A-Za-z0-9+/] with optional '=' padding
      if (s.Length < 40) { note = "Too short for typical base64 payload"; return false; }
      if (s.Any(ch => char.IsWhiteSpace(ch))) { note = "Contains whitespace"; return false; }

      // Quick alphabet check
      if (!Regex.IsMatch(s, @"^[A-Za-z0-9+/]*={0,2}$", RegexOptions.CultureInvariant))
      {
         note = "Alphabet/padding mismatch";
         return false;
      }

      // Try decode a small slice safely
      try
      {
         // Convert.FromBase64String throws if invalid; might be expensive for huge strings.
         // Limit to first ~4KB while keeping multiple of 4 length.
         var sample = s.Length > 4096 ? s[..4096] : s;
         sample = sample[..(sample.Length - (sample.Length % 4))];
         _ = Convert.FromBase64String(sample);
         note = "Base64 decode succeeded on sample";
         return true;
      }
      catch
      {
         note = "Base64 decode failed";
         return false;
      }
   }

   // ---------- Plain text heuristic ----------
   private static bool LooksPlainText(string raw, out string note)
   {
      // If it has lots of natural-language spaces/words and no strong syntax cues, call it plain
      // text. Also tolerate code snippets that aren't clearly markdown fenced.
      var s = raw.Trim();
      int letters = s.Count(char.IsLetter);
      int spaces = s.Count(char.IsWhiteSpace);
      int total = s.Length;

      var letterRatio = total == 0 ? 0 : (double)letters / total;
      var spaceRatio = total == 0 ? 0 : (double)spaces / total;

      note = $"letterRatio={letterRatio:F2}, spaceRatio={spaceRatio:F2}";
      return letterRatio > 0.20 && spaceRatio > 0.05;
   }

   private static string[] SplitNonEmptyLines(string s)
       => s.Replace("\r\n", "\n").Replace('\r', '\n')
           .Split('\n')
           .Select(l => l.TrimEnd())
           .Where(l => !string.IsNullOrWhiteSpace(l))
           .ToArray();
}


