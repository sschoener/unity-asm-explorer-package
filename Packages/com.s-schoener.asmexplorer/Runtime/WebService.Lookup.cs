using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
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
                var modules = Process.GetCurrentProcess().Modules;
                string FindModule(long address)
                {
                    for (int i = 0; i < modules.Count; i++)
                    {
                        var m = modules[i];
                        long baseAddress = m.BaseAddress.ToInt64();
                        if (baseAddress <= address && address < baseAddress + m.ModuleMemorySize)
                            return modules[i].ModuleName;
                    }
                    return "unknown module";
                }

                using (writer.ContainerFluid())
                {
                    writer.Inline("h4", "Results");
                    var lines = postValues[addresses].Split(new[]{'\n'}, StringSplitOptions.RemoveEmptyEntries);
                    using (writer.Tag("textarea", "class", "form-control", "rows", "10"))
                    {
                        foreach (var line in lines)
                        {
                            writer.Write(line);
                            writer.Write(", ");
                            int start = line.IndexOf("0x");
                            if (start >= 0)
                            {
                                int end = line.IndexOf(',', start);
                                if (end < 0) end = line.Length;
                                var numberString = line.Substring(start + 2, end - start - 2);
                                if (long.TryParse(numberString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long address))
                                {
                                    var jitInfo = Mono.GetJitInfo(new IntPtr(address));
                                    if (jitInfo.Method == null)
                                        writer.Write("unknown method");
                                    else if (jitInfo.Method is MethodInfo m)
                                    {
                                        WriteTypeName(writer, m.DeclaringType, true);
                                        writer.Write(".");
                                        WriteMethodDeclaration(writer, m, true);
                                    }
                                    else if (jitInfo.Method is ConstructorInfo c)
                                        WriteCtorDeclaration(writer, c, true);
                                    writer.Write(", ");
                                    writer.Write(FindModule(address));
                                }
                                else
                                    writer.Write("failed to parse " + numberString + ",");
                            } else
                                writer.Write("failed to parse,");


                            writer.Write("\n");
                        }
                    }
                }
            }
        }
    }
}
