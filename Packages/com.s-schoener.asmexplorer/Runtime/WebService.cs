using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml;
using UnityEngine;

namespace AsmExplorer
{
    public partial class WebService
    {
        private const BindingFlags AllInstanceBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags AllStaticBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags AllBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private readonly WebServer _webServer;
        private readonly string _completePrefix;
        private readonly Explorer _explorer;

        public WebService(Explorer explorer, string prefix, int port = 8080)
        {
            _explorer = explorer;
            _completePrefix = "http://localhost:" + port + "/" + prefix + "/";
            _webServer = new WebServer(HandleRequest, _completePrefix,
                "http://127.0.0.1:" + port + "/" + prefix + "/");
        }

        public void Start()
        {
            _webServer.Run();
        }

        public void Stop()
        {
            _webServer.Stop();
        }

        private void HandleRequest(HttpListenerContext ctxt)
        {
            // decode command
            string url = ctxt.Request.RawUrl;
            int commandStart = url.IndexOf('/', 1) + 1;
            string command = null;
            if (commandStart != -1)
            {
                int commandEnd = url.IndexOf('?', commandStart);
                if (commandEnd == -1)
                {
                    commandEnd = url.Length;
                }

                command = url.Substring(commandStart, commandEnd - commandStart);
            }

            if (string.IsNullOrEmpty(command))
                command = "inspect";

            using (MemoryStream stream = new MemoryStream())
            {
                var sw = new StreamWriter(stream, new UTF8Encoding(false));
                sw.WriteLine("<!doctype html>");
                var writer = new HtmlWriter(sw);
                using (writer.Tag("html", "lang", "en"))
                {
                    using (writer.Tag("head"))
                    {
                        WriteHeaderContent(sw, "ASM Explorer");
                    }

                    using (writer.Tag("body"))
                    using (writer.Tag("div", "class", "container-fluid"))
                    {
                        if (command == "inspect")
                            ExecuteInspect(writer, ctxt.Request.QueryString);
                        else
                            writer.Write($"Invalid command \"{command}\" in {url}");
                    }

                    sw.WriteLine();
                    WriteJavaScript(sw);
                    sw.WriteLine();
                }

                sw.Flush();
                ctxt.Response.ContentLength64 = stream.Length;
                stream.Position = 0;
                stream.WriteTo(ctxt.Response.OutputStream);
            }

            ctxt.Response.OutputStream.Close();
        }

        static void WriteJavaScript(StreamWriter writer)
        {
            const string js = @"
<!-- jQuery first, then Popper.js, then Bootstrap JS -->
<script src=""https://code.jquery.com/jquery-3.4.1.slim.min.js"" integrity=""sha384-J6qa4849blE2+poT4WnyKhv5vZF5SrPo0iEjwBvKU7imGFAV0wwj1yYfoRSJoZ+n"" crossorigin=""anonymous""></script>
<script src=""https://cdn.jsdelivr.net/npm/popper.js@1.16.0/dist/umd/popper.min.js"" integrity=""sha384-Q6E9RHvbIyZFJoft+2mJbHaEWldlvI9IOYy5n3zV9zzTtmI3UksdQRVvoxMfooAo"" crossorigin=""anonymous""></script>
<script src=""https://stackpath.bootstrapcdn.com/bootstrap/4.4.1/js/bootstrap.min.js"" integrity=""sha384-wfSDF2E50Y2D1uUdj0O3uMBJnjuUD4Ih7YwaYd1iqfktj0Uod8GCExl3Og8ifwB6"" crossorigin=""anonymous""></script>";
            writer.WriteLine(js);
        }

        static void WriteHeaderContent(StreamWriter writer, string title)
        {
            const string header = @"
    <!-- Required meta tags -->
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1, shrink-to-fit=no"">

    <!-- Bootstrap CSS -->
    <link rel=""stylesheet"" href=""https://stackpath.bootstrapcdn.com/bootstrap/4.4.1/css/bootstrap.min.css"" integrity=""sha384-Vkoo8x4CGsO3+Hhxv8T/Q5PaXtkKtu6ug5TOeNV6gBiFeWPGFN9MuhOf23Q9Ifjh"" crossorigin=""anonymous"">
";
            const string style = @"table {
    border-collapse: collapse;
    width: 100%;
}

td, th {
    border: 1px solid #dddddd;
    text-align: left;
    padding: 8px;
}

tr:nth-child(even) {
    background-color: #dddddd;
}";
            writer.WriteLine(header);
            writer.WriteLine("<style>");
            writer.WriteLine(style);
            writer.WriteLine("</style>");
            writer.WriteLine($"<title>{title}</title>");
        }

        private void ExecuteInspect(HtmlWriter writer, NameValueCollection args)
        {
            if (args["assembly"] != null)
            {
                var asm = args["assembly"];
                if (args["type"] != null)
                {
                    var type = args["type"];
                    if (args["method"] != null)
                        InspectMethod(writer, asm, type, args["method"]);
                    else
                        InspectType(writer, asm, type);
                }
                else if (args["namespace"] != null)
                {
                    InspectNamespace(writer, asm, args["namespace"]);
                }
                else
                {
                    InspectAssembly(writer, asm);
                }
            }
            else
            {
                InspectDomain(writer);
            }
        }

        private void MakeTable<T>(HtmlWriter writer, IEnumerable<T> ts, params Action<T>[] inner)
        {
            using (writer.Tag("div", "class", "container-fluid"))
            {
                using (writer.Tag("table"))
                {
                    foreach (var t in ts)
                    {
                        using (writer.Tag("tr"))
                        {
                            foreach (var cell in inner)
                            {
                                using (writer.Tag("td"))
                                    cell(t);
                            }
                        }
                    }
                }
            }
        }

        private void WriteAttributes(HtmlWriter writer, object[] attributes, bool stacked = true)
        {
            if (attributes.Length == 0) return;
            if (!stacked) writer.Write("[");
            for (int i = 0; i < attributes.Length; i++)
            {
                if (stacked) writer.Write("[");
                else if (i > 0) writer.Write(" ,");
                var type = attributes[i].GetType();
                TypeLink(writer, type);
                var properties = type.GetProperties(AllInstanceBindings);
                if (properties.Length > 0)
                {
                    bool first = true;
                    for (int j = 0; j < properties.Length; j++)
                    {
                        var prop = properties[j];
                        if (!prop.CanRead || prop.GetIndexParameters().Length > 0 || prop.Name == "TypeId") continue;
                        if (!first) writer.Write(", ");
                        else writer.Write(" ( ");
                        first = false;
                        writer.Write(prop.Name);
                        writer.Write(" = ");
                        var value = prop.GetValue(attributes[i], null);
                        if (value == null)
                            writer.Write("null");
                        else if (value is string)
                        {
                            writer.Write("\"");
                            writer.Write((value as string));
                            writer.Write("\"");
                        }
                        else
                            writer.Write(value.ToString());
                    }

                    if (!first)
                        writer.Write(" ) ");
                }

                if (stacked)
                {
                    writer.Write("]");
                    writer.Break();
                }
            }

            if (!stacked) writer.Write("]");
        }

        #region encode/decode

        const string k_MethodEncodingGenericArgSeparator = ";;;with;;;";

        private string EncodeMethod(MethodInfo method)
        {
            if (method.IsGenericMethod && !method.IsGenericMethodDefinition)
            {
                var genericDefinition = method.GetGenericMethodDefinition();
                string methodId = genericDefinition.ToString();
                var arguments = method.GetGenericArguments();
                methodId += k_MethodEncodingGenericArgSeparator + string.Join(";", arguments.Select(EncodeType));
                return methodId;
            }
            else
            {
                return method.ToString();
            }
        }

        private MethodInfo DecodeMethod(Type type, string encodedMethod)
        {
            int separator = encodedMethod.IndexOf(k_MethodEncodingGenericArgSeparator);
            Type[] genericArguments;
            string lookUpKey;
            if (separator < 0)
            {
                lookUpKey = encodedMethod;
                genericArguments = null;
            }
            else
            {
                lookUpKey = encodedMethod.Substring(0, separator);
                var arguments = encodedMethod.Substring(separator + k_MethodEncodingGenericArgSeparator.Length).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                genericArguments = arguments.Select(DecodeType).ToArray();
            }

            var methods = type.GetMethods(AllBindings);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].ToString() == lookUpKey)
                {
                    if (genericArguments != null)
                        return methods[i].MakeGenericMethod(genericArguments);
                    return methods[i];
                }
            }

            return null;
        }

        private string EncodeCtor(ConstructorInfo ctor) => ctor.ToString();

        private ConstructorInfo DecodeCtor(Type type, string ctorName)
        {
            var ctors = type.GetConstructors(AllBindings);
            for (int i = 0; i < ctors.Length; i++)
            {
                if (ctors[i].ToString() == ctorName)
                    return ctors[i];
            }

            return null;
        }

        private string EncodeType(Type type) => type.AssemblyQualifiedName;
        private Type DecodeType(string encodedType) => Type.GetType(encodedType, false, false);

        #endregion

        private void FunctionLink(HtmlWriter writer, MethodInfo method)
        {
            FunctionLink(writer, method, method.DeclaringType.Name + "." + method.Name);
        }

        private void FunctionLink(HtmlWriter writer, MethodInfo method, string txt)
        {
            writer.AHref(txt,
                Html.Url(
                    _completePrefix + "inspect",
                    "assembly", method.DeclaringType.Assembly.FullName,
                    "type", method.DeclaringType.FullName,
                    "method", EncodeMethod(method)
                )
            );
        }

        private void FunctionLink(HtmlWriter writer, ConstructorInfo method)
        {
            FunctionLink(writer, method, method.DeclaringType.Name + "." + method.Name);
        }

        private void FunctionLink(HtmlWriter writer, ConstructorInfo method, string txt)
        {
            writer.AHref(txt,
                Html.Url(
                    _completePrefix + "inspect",
                    "assembly", method.DeclaringType.Assembly.FullName,
                    "type", method.DeclaringType.FullName,
                    "method", EncodeCtor(method)
                )
            );
        }

        private void TypeLink(HtmlWriter writer, Type type)
        {
            TypeLink(writer, type, type.PrettyName());
        }

        private void TypeLink(HtmlWriter writer, Type type, string txt)
        {
            writer.AHref(txt,
                Html.Url(
                    _completePrefix + "inspect",
                    "assembly", type.Assembly.FullName,
                    "type", type.FullName
                )
            );
        }

        private void NamespaceLink(HtmlWriter writer, Namespace ns)
        {
            NamespaceLink(writer, ns, ns.FullName);
        }

        private void NamespaceLink(HtmlWriter writer, Namespace ns, string txt)
        {
            writer.AHref(txt,
                Html.Url(
                    _completePrefix + "inspect",
                    "assembly", ns.Assembly.FullName,
                    "namespace", ns.FullName
                )
            );
        }

        private void AssemblyLink(HtmlWriter writer, Assembly assembly)
        {
            AssemblyLink(writer, assembly, assembly.Name);
        }

        private void AssemblyLink(HtmlWriter writer, Assembly assembly, string txt)
        {
            writer.AHref(txt,
                Html.Url(
                    _completePrefix + "inspect",
                    "assembly", assembly.FullName
                )
            );
        }
    }
}
