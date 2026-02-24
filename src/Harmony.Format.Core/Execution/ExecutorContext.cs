using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Harmony.Tooling.Llm;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format;

/// <summary>
/// Provides contextual data and utility methods for executing chat-based operations, including
/// access to kernel services, chat history, variable storage, and input parameters.
/// </summary>
/// <remarks>ExecContext encapsulates all resources and state required for evaluating 
/// expressions, rendering templates, and managing chat interactions within an execution flow. 
/// It is intended for internal use to coordinate execution logic and should not be instantiated
/// directly by consumers. Thread safety is not guaranteed; concurrent access should be managed 
/// externally if required.</remarks>
internal sealed class ExecutorContext
{
   public const string ExprPrefixVars = "$vars.";
   public const string ExprPrefixInput = "$input.";

   private const string ExprFuncLen = "$len(";
   private const string ExprFuncMap = "$map(";

   private static readonly Regex BooleanExpressionRegex =
   new(@"^(?<left>.+?)\s*(?<op>==|!=|<=|>=|<|>)\s*(?<right>.+?)$",
       RegexOptions.Compiled);

   private static readonly Regex TemplateRegex =
      new(@"\{\{\s*(?<path>[^}]+)\s*\}\}", RegexOptions.Compiled);

   public ILanguageModelChatService ChatService { get; }
   public IToolExecutionService ToolRouter { get; }
   public ChatConversation ChatHistory { get; }

   public IDictionary<string, object?> Vars { get; }
   public IDictionary<string, object?> Input { get; }

   public JsonSerializerOptions JsonOptions;
   public string FinalText { get; set; } = string.Empty;

   public ExecutorContext(
      ILanguageModelChatService chat, IToolExecutionService toolRouter,
      ChatConversation history, Dictionary<string, object?> vars,
      IDictionary<string, object?> input, JsonSerializerOptions opts)
   {
      ChatService = chat;
      ToolRouter = toolRouter;
      ChatHistory = history;

      Vars = vars;
      Input = input;
      JsonOptions = opts;
   }

   public static object? FromJsonElement(JsonElement e)
   {
      return e.ValueKind switch
      {
         JsonValueKind.String => e.GetString(),
         JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
         JsonValueKind.True => true,
         JsonValueKind.False => false,
         JsonValueKind.Null => null,
         JsonValueKind.Object => JsonDocument.Parse(e.GetRawText()).RootElement.Clone(),
         JsonValueKind.Array => JsonDocument.Parse(e.GetRawText()).RootElement.Clone(),
         _ => e.GetRawText()
      };
   }

   /// <summary>
   /// Evaluates a simple expression and returns its computed value, supporting patterns such as 
   /// variable access, input property access, length calculation, and property mapping.
   /// </summary>
   /// <remarks>Supported patterns allow dynamic access to variables and input properties, as
   /// well as basic collection operations. If the expression does not match a supported 
   /// pattern, it is returned as-is. The method does not perform complex parsing and is 
   /// intended for simple evaluation scenarios.</remarks>
   /// <param name="expr">The expression to evaluate. Supported patterns include variable 
   /// references (e.g., "$vars.x"), input property access (e.g., "$input.x"), length calculation
   /// (e.g., "$len(collection)"), and property mapping (e.g., "$map(collection, 'property')").
   /// The expression must not be null.</param>
   /// <returns>The result of evaluating the expression. Returns the computed value for
   /// supported patterns, or the original expression string if no pattern is matched.</returns>
   /// <exception cref="InvalidOperationException">Thrown if the "$map" pattern is used with an
   /// argument list that does not contain exactly two elements:  
   ///    collection and a property name.</exception>
   public object? EvalExpression(string expr)
   {
      if (string.IsNullOrWhiteSpace(expr)) return expr;

      // Minimal evaluator for patterns: $vars.x, $input.x, $len(...), $map(...)
      expr = expr.Trim();
      if (!expr.StartsWith("$", StringComparison.Ordinal)) return expr;

      if (expr.StartsWith(ExprFuncLen, StringComparison.Ordinal))
      {
         var inner = Between(expr, ExprFuncLen, ")");
         var value = EvalExpression(inner);
         if (value is JsonElement je)
         {
            return je.ValueKind == JsonValueKind.Array ? je.GetArrayLength() : 0;
         }
         if (value is IEnumerable<object?> list) return list.Count();
         if (value is string s) return s.Length;
         if (value is ICollection<object?> col) return col.Count;
         return 0;
      }

      if (expr.StartsWith(ExprFuncMap, StringComparison.Ordinal))
      {
         // $map(vars.recipes, 'name')
         var inner = Between(expr, ExprFuncMap, ")");
         var parts = SplitArgs(inner);
         if (parts.Count != 2) throw new InvalidOperationException(
            "map expects two args: collection, 'prop'");
         var collection = EvalExpression(parts[0]);
         var prop = parts[1].Trim().Trim('\'', '"');

         var results = new List<object?>();
         if (collection is JsonElement je && je.ValueKind == JsonValueKind.Array)
         {
            foreach (var item in je.EnumerateArray())
            {
               if (item.ValueKind == JsonValueKind.Object &&
                   item.TryGetProperty(prop, out var v))
               {
                  results.Add(FromJsonElement(v));
               }
            }
            return results;
         }
         if (collection is IEnumerable<object?> enumerable)
         {
            foreach (var item in enumerable)
            {
               if (item is JsonElement o && o.ValueKind ==
                  JsonValueKind.Object && o.TryGetProperty(prop, out var v))
                  results.Add(FromJsonElement(v));
               else if (item is IDictionary<string, object?> dict &&
                  dict.TryGetValue(prop, out var v2))
                  results.Add(v2);
            }
            return results;
         }
         return results;
      }

      if (expr.StartsWith(ExprPrefixInput, StringComparison.Ordinal))
      {
         var path = expr.Substring(ExprPrefixInput.Length);
         return ResolvePath(Input, path);
      }

      if (expr.StartsWith(ExprPrefixVars, StringComparison.Ordinal))
      {
         var path = expr.Substring(ExprPrefixVars.Length);
         return ResolvePath(Vars, path);
      }

      // Literal fall-through
      return expr;
   }

   public bool EvalBoolean(string condition)
   {
      if (string.IsNullOrWhiteSpace(condition))
         return false;  // Or throw with better message

      // Support very simple comparisons: <, <=, ==, !=, >=, >
      // Left/right may be expressions like $len(vars.recipes)
      var m = BooleanExpressionRegex.Match(condition);
      if (!m.Success)
      {
         // Single term truthiness?
         var single = EvalExpression(condition);
         return IsTruthy(single);
      }

      var left = EvalExpression(m.Groups["left"].Value);
      var right = EvalExpression(m.Groups["right"].Value);
      var op = m.Groups["op"].Value;

      int cmp = Compare(left, right);
      return op switch
      {
         "==" => AreEqual(left, right),
         "!=" => !AreEqual(left, right),
         "<" => cmp < 0,
         "<=" => cmp <= 0,
         ">" => cmp > 0,
         ">=" => cmp >= 0,
         _ => false
      };
   }

   public string RenderTemplate(string template)
   {
      // Very minimal mustache-like replacement: {{vars.key}}, {{input.key}}
      // Nested lookups by dot-path are supported.
      return TemplateRegex.Replace(template, m =>
      {
         var path = m.Groups["path"].Value.Trim();
         if (path.StartsWith("vars.", StringComparison.Ordinal))
         {
            var v = ResolvePath(Vars, path.Substring(5));
            return ToString(v);
         }
         if (path.StartsWith("input.", StringComparison.Ordinal))
         {
            var v = ResolvePath(Input, path.Substring(6));
            return ToString(v);
         }
         return m.Value;
      });
   }

   private static object? ResolvePath(IDictionary<string, object?> dict, string path)
   {
      var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
      object? current = dict;

      foreach (var part in parts)
      {
         if (current is IDictionary<string, object?> d)
         {
            if (!d.TryGetValue(part, out current)) return null;
            continue;
         }
         if (current is JsonElement je && je.ValueKind == JsonValueKind.Object)
         {
            if (!je.TryGetProperty(part, out var v)) return null;
            current = FromJsonElement(v);
            continue;
         }
         return null;
      }

      return current;
   }

   private static string Between(string s, string start, string end)
   {
      var i = s.IndexOf(start, StringComparison.Ordinal);
      if (i < 0) return string.Empty;
      i += start.Length;
      var j = s.IndexOf(end, i, StringComparison.Ordinal);
      if (j < 0) return string.Empty;
      return s.Substring(i, j - i);
   }

   private static List<string> SplitArgs(string s)
   {
      // split by comma outside quotes
      var list = new List<string>();
      var sb = new StringBuilder();
      bool inQuotes = false;

      foreach (var ch in s)
      {
         if (ch == '\'' || ch == '"') inQuotes = !inQuotes;
         if (ch == ',' && !inQuotes)
         {
            list.Add(sb.ToString());
            sb.Clear();
         }
         else sb.Append(ch);
      }
      if (sb.Length > 0) list.Add(sb.ToString());
      return list;
   }

   private static string ToString(object? v)
       => v switch
       {
          null => string.Empty,
          JsonElement je => je.ToString(),
          _ => v.ToString() ?? string.Empty
       };

   private static bool IsTruthy(object? v)
       => v switch
       {
          null => false,
          bool b => b,
          string s => !string.IsNullOrWhiteSpace(s),
          JsonElement je => je.ValueKind != JsonValueKind.Null &&
             je.ValueKind != JsonValueKind.Undefined,
          _ => true
       };

   private static bool AreEqual(object? a, object? b)
   {
      if (a is null && b is null) return true;
      if (a is null || b is null) return false;

      if (TryNumber(a, out var da) && TryNumber(b, out var db)) return da == db;
      return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
   }

   private static int Compare(object? a, object? b)
   {
      if (TryNumber(a, out var da) && TryNumber(b, out var db))
         return da.CompareTo(db);

      return string.Compare(a?.ToString(), b?.ToString(), StringComparison.Ordinal);
   }

   private static bool TryNumber(object? v, out double d)
   {
      if (v is double dd) { d = dd; return true; }
      if (v is float ff) { d = ff; return true; }
      if (v is long ll) { d = ll; return true; }
      if (v is int ii) { d = ii; return true; }
      if (v is string s && double.TryParse(s, out d)) return true;
      if (v is JsonElement je && je.ValueKind == JsonValueKind.Number)
      {
         if (je.TryGetInt64(out var l)) { d = l; return true; }
         d = je.GetDouble(); return true;
      }
      d = 0; return false;
   }
}

