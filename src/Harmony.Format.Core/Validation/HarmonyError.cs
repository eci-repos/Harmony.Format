using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

public class HarmonyError
{
   public string Code { get; set; } = "HRF_VALIDATION_FAILED";
   public string Message { get; set; } = string.Empty;
   public object? Details { get; set; }
}

