using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format;

public class HarmonyError
{
   public const string VALIDATION_FAILED = "HRF_VALIDATION_FAILED";
   public const string MISSING_TOOL = "MISSING_TOOL";

   public string Code { get; set; } = VALIDATION_FAILED;
   public string Message { get; set; } = string.Empty;
   public object? Details { get; set; }
}

