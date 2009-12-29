#region License
// Copyright (c) 2007 James Newton-King
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Newtonsoft.Json.Utilities
{
  internal static class JavaScriptUtils
  {
    public static void WriteEscapedJavaScriptString(StringBuilder writer, string value, char delimiter, bool appendDelimiters)
    {
      // leading delimiter
      if (appendDelimiters)
        writer.Append(delimiter);

      if (value != null)
      {
        for (int i = 0; i < value.Length; i++)
        {
          char c = value[i];

          switch (c)
          {
            case '\t':
              writer.Append(@"\t");
              break;
            case '\n':
              writer.Append(@"\n");
              break;
            case '\r':
              writer.Append(@"\r");
              break;
            case '\f':
              writer.Append(@"\f");
              break;
            case '\b':
              writer.Append(@"\b");
              break;
            case '\\':
              writer.Append(@"\\");
              break;
            case '\u0085': // Next Line
              writer.Append(@"\u0085");
              break;
            case '\u2028': // Line Separator
              writer.Append(@"\u2028");
              break;
            case '\u2029': // Paragraph Separator
              writer.Append(@"\u2029");
              break;
            case '\'':
              // only escape if this charater is being used as the delimiter
              writer.Append((delimiter == '\'') ? @"\'" : @"'");
              break;
            case '"':
              // only escape if this charater is being used as the delimiter
              writer.Append((delimiter == '"') ? "\\\"" : @"""");
              break;
            default:
              if (c > '\u001f')
                writer.Append(c);
              else
                StringUtils.WriteCharAsUnicode(writer, c);
              break;
          }
        }
      }

      // trailing delimiter
      if (appendDelimiters)
        writer.Append(delimiter);
    }

    public static string ToEscapedJavaScriptString(string value)
    {
      return ToEscapedJavaScriptString(value, '"', true);
    }

    public static string ToEscapedJavaScriptString(string value, char delimiter, bool appendDelimiters)
    {
      int length = value.Length;
      if (appendDelimiters)
        length += 2;

      StringBuilder builder = new StringBuilder(length);

      WriteEscapedJavaScriptString(builder, value, delimiter, appendDelimiters);
      return builder.ToString();
    }
  }
}