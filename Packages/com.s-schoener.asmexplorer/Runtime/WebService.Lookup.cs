using System;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web;

namespace AsmExplorer
{
    public partial class WebService
    {
        void ExecuteLookup(HtmlWriter writer, HttpListenerRequest request)
        {
            const string addresses = nameof(addresses);
            using (writer.Tag("form", "action", CommandUrl("lookup"), "method", "post"))
            using (writer.ContainerFluid())
            using (writer.Tag("div", "class", "form-group"))
            {
                writer.Inline("h2", "Paste addresses to look up (hex)");
                writer.Tag("textarea", "class", "form-control", "name", addresses, "rows", "10").Dispose();
                writer.Break();
                writer.InlineTag("input", "type", "submit", "value", "Submit");
            }
            NameValueCollection postValues;
            using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
                postValues = HttpUtility.ParseQueryString(reader.ReadToEnd());
            if (postValues[addresses] != null)
            {
                using (writer.ContainerFluid())
                {
                    writer.Inline("h4", "Results");
                    var lines = postValues[addresses].Split(new[]{'\n'}, StringSplitOptions.RemoveEmptyEntries);
                    using (writer.Tag("code"))
                    {
                        foreach (var line in lines)
                        {
                            writer.Write(line);
                            writer.Write(", ");
                            var numberString = line;
                            if (numberString.StartsWith("0x"))
                                numberString = numberString.Substring("0x".Length);
                            if (long.TryParse(numberString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long result))
                            {
                                var jitInfo = Mono.GetJitInfo(new IntPtr(result));
                                if (jitInfo.Method == null)
                                    writer.Write("unknown");
                                else if (jitInfo.Method is MethodInfo m)
                                    MethodLink(writer, m);
                                else if (jitInfo.Method is ConstructorInfo c)
                                    MethodLink(writer, c);
                            } else
                                writer.Write("failed to parse");
                            writer.Break();
                        }
                    }
                }
            }
        }
    }
}
