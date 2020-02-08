using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Xml;
using UnityEngine;

namespace AsmExplorer
{
    public partial class WebService
    {
        const BindingFlags k_AllInstanceBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags k_AllStaticBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags k_AllBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        readonly WebServer m_WebServer;
        readonly string m_CompletePrefix;
        readonly Explorer m_Explorer;

        public WebService(Explorer explorer, string prefix, int port = 8080)
        {
            m_Explorer = explorer;
            m_CompletePrefix = "http://localhost:" + port + "/" + prefix + "/";
            m_WebServer = new WebServer(HandleRequest, m_CompletePrefix,
                "http://127.0.0.1:" + port + "/" + prefix + "/");
        }

        public static string MakeCommandURL(string baseUrl, WebServiceCommand command)
        {
            if (!baseUrl.EndsWith("/"))
                baseUrl += '/';
            return baseUrl + CommandName(command);
        }

        static string CommandName(WebServiceCommand command)
        {
            switch (command)
            {
                case WebServiceCommand.Inspect:
                    return "inspect";
                case WebServiceCommand.Lookup:
                    return "lookup";
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }
        }

        string CommandUrl(string command) => m_CompletePrefix + command;

        public void Start() => m_WebServer.Run();

        public void Stop() => m_WebServer.Stop();

        void HandleRequest(HttpListenerContext ctxt)
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
                    using (writer.ContainerFluid())
                    {
                        switch (command)
                        {
                            case "inspect":
                            {
                                ExecuteInspect(writer, ctxt.Request.QueryString);
                                break;
                            }
                            case "lookup":
                            {
                                ExecuteLookup(writer, ctxt.Request);
                                break;
                            }
                            default:
                            {
                                writer.Write($"Invalid command \"{command}\" in {url}");
                                break;
                            }
                        }
                    }

                    sw.WriteLine();
                }

                sw.Flush();
                ctxt.Response.ContentLength64 = stream.Length;
                stream.Position = 0;
                stream.WriteTo(ctxt.Response.OutputStream);
            }

            ctxt.Response.OutputStream.Close();
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
}

.container-fluid {
    margin-top: 15px;
    margin-bottom: 15px;
}
";
            writer.WriteLine(header);
            writer.WriteLine("<style>");
            writer.WriteLine(style);
            writer.WriteLine("</style>");
            writer.WriteLine($"<title>{title}</title>");
        }

        void ExecuteInspect(HtmlWriter writer, NameValueCollection args)
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

        static void MakeCodeList<T>(HtmlWriter writer, IEnumerable<T> ts, params Action<T>[] inner)
        {
            foreach (var t in ts)
            {
                bool first = true;
                foreach (var cell in inner)
                {
                    if (!first)
                        writer.Write(" ");
                    cell(t);
                    first = false;
                }
                writer.WriteLine(";");
            }
        }

        static void MakeCodeListWithoutSemicolon<T>(HtmlWriter writer, IEnumerable<T> ts, params Action<T>[] inner)
        {
            foreach (var t in ts)
            {
                bool first = true;
                foreach (var cell in inner)
                {
                    if (!first)
                        writer.Write(" ");
                    cell(t);
                    first = false;
                }
                writer.Break();
            }
        }

        void WriteInlineAttributes(HtmlWriter writer, object[] attributes)
        {
            if (attributes == null || attributes.Length == 0)
                return;
            WriteAttributes(writer, attributes, false);
            writer.Break();
        }

        void WriteAttributes(HtmlWriter writer, object[] attributes, bool stacked = true)
        {
            using (writer.Tag("code"))
            {
                if (attributes.Length == 0) return;
                if (!stacked) writer.Write("[");
                for (int i = 0; i < attributes.Length; i++)
                {
                    if (stacked) writer.Write("[");
                    else if (i > 0) writer.Write(", ");
                    var type = attributes[i].GetType();
                    TypeLink(writer, type);
                    var properties = type.GetProperties(k_AllInstanceBindings);
                    if (properties.Length > 0)
                    {
                        bool first = true;
                        for (int j = 0; j < properties.Length; j++)
                        {
                            var prop = properties[j];
                            if (!prop.CanRead || prop.GetIndexParameters().Length > 0 || prop.Name == "TypeId") continue;
                            if (!first) writer.Write(", ");
                            else writer.Write("(");
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
                            writer.Write(")");
                    }

                    if (stacked)
                    {
                        writer.Write("]");
                        writer.Break();
                    }
                }

                if (!stacked) writer.Write("]");
            }
        }

        void MethodLink(HtmlWriter writer, MethodInfo method) => MethodLink(writer, method, method.DeclaringType.Name + "." + method.Name);

        void MethodLink(HtmlWriter writer, MethodInfo method, string txt)
        {
            writer.AHref(txt,
                Html.Url(
                    m_CompletePrefix + "inspect",
                    "assembly", method.DeclaringType.Assembly.FullName,
                    "type", method.DeclaringType.FullName,
                    "method", Serialization.EncodeMethod(method)
                )
            );
        }

        void MethodLink(HtmlWriter writer, ConstructorInfo method) => MethodLink(writer, method, method.DeclaringType.Name + "." + method.Name);

        void MethodLink(HtmlWriter writer, ConstructorInfo method, string txt)
        {
            writer.AHref(txt,
                Html.Url(
                    m_CompletePrefix + "inspect",
                    "assembly", method.DeclaringType.Assembly.FullName,
                    "type", method.DeclaringType.FullName,
                    "method", Serialization.EncodeCtor(method)
                )
            );
        }

        void TypeLink(HtmlWriter writer, Type type) => TypeLink(writer, type, type.PrettyName());

        void TypeLink(HtmlWriter writer, Type type, string txt)
        {
            writer.AHref(txt,
                Html.Url(
                    m_CompletePrefix + "inspect",
                    "assembly", type.Assembly.FullName,
                    "type", type.FullName
                )
            );
        }

        void NamespaceLink(HtmlWriter writer, Namespace ns) => NamespaceLink(writer, ns, ns.FullName);

        void NamespaceLink(HtmlWriter writer, Namespace ns, string txt)
        {
            writer.AHref(txt,
                Html.Url(
                    m_CompletePrefix + "inspect",
                    "assembly", ns.Assembly.FullName,
                    "namespace", ns.FullName
                )
            );
        }

        void AssemblyLink(HtmlWriter writer, Assembly assembly) => AssemblyLink(writer, assembly, assembly.Name);

        void AssemblyLink(HtmlWriter writer, Assembly assembly, string txt)
        {
            writer.AHref(txt,
                Html.Url(
                    m_CompletePrefix + "inspect",
                    "assembly", assembly.FullName
                )
            );
        }

        void DomainLink(HtmlWriter writer) =>
            writer.AHref(AppDomain.CurrentDomain.FriendlyName, Html.Url(m_CompletePrefix + "inspect"));
    }
}
