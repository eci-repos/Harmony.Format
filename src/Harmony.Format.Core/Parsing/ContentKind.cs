using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// -------------------------------------------------------------------------------------------------
namespace Harmony.Format.Core;

public enum ContentKind
{
   Empty,
   Json,
   Xml,
   Markdown,
   Html,
   Yaml,
   CsvTsv,
   UrlEncodedForm,
   Base64,
   PlainText,
   Unknown
}

