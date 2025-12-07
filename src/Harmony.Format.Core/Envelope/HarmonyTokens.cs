using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

public static class HarmonyTokens
{
   public const string Start = "<|start|>";
   public const string Message = "<|message|>";

   /// <summary>
   /// Represents the placeholder string used to identify a channel in templated content or 
   /// messages.
   /// </summary>
   /// <remarks>
   /// You need to specify a channel in the Harmony Response Format to indicate where the content 
   /// will be routed (e.g., analysis, commentary, final).
   /// 
   /// In the Harmony Response Format, the <|channel|> segment is optional in the message header, 
   /// but in practice it’s often effectively required because the model is expected to route 
   /// outputs into channels like analysis, commentary, and final. The spec describes the general
   /// message structure as <|start|>{header}<|message|>{content}<|end|> and defines <|channel|> 
   /// as a token that “indicates the transition to the channel information of the header”
   /// (i.e., it’s a header field you may include).
   /// 
   /// However, for normal chat completions the guide also notes that the output will begin by 
   /// specifying the channel, and their examples show the assistant starting with 
   /// <|channel|>analysis... and later <|start|>assistant<|channel|>final.... 
   /// So: syntactically optional, but if you omit it you’re relying on defaults that may reduce
   /// reliability—especially if you want clean separation of tool-calling(commentary) vs 
   /// user-visible output(final).
   /// </remarks>
   public const string Channel = "<|channel|>";

   public const string Constrain = "<|constrain|>";
   public const string End = "<|end|>";
   public const string Call = "<|call|>";
   public const string Return = "<|return|>";
}

