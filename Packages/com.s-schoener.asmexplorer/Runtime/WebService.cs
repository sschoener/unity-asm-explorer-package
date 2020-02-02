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
    public partial class WebService {
        private const BindingFlags AllInstanceBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags AllStaticBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags AllBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private WebServer _webServer;
        private string _completePrefix;
        private Explorer _explorer;
        public WebService(Explorer explorer, string prefix, int port=8080) {
            _explorer = explorer;
            _completePrefix = "http://localhost:" + port + "/" + prefix + "/";
            _webServer = new WebServer(HandleRequest, _completePrefix,
                                                      "http://127.0.0.1:" + port + "/" + prefix + "/");
        }

        public void Start() {
            _webServer.Run();
        }

        public void Stop() {
            _webServer.Stop();
        }

        private void HandleRequest(HttpListenerContext ctxt) {
            var settings = new XmlWriterSettings {
                // this is crucial to get rid of the BOM header, see https://stackoverflow.com/questions/1755958/how-can-i-remove-the-bom-from-xmltextwriter-using-c
                Encoding = new System.Text.UTF8Encoding(false),
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment,
                CloseOutput = false,
            };

            // decode command
            string url = ctxt.Request.RawUrl;
            int commandStart = url.IndexOf('/', 1) + 1;
            string command;
            if (commandStart != -1) {
                int commandEnd = url.IndexOf('?', commandStart);
                if (commandEnd == -1) {
                    commandEnd = url.Length;
                }
                command = url.Substring(commandStart, commandEnd - commandStart);
            } else {
                command = "inspect";
            }

            using (MemoryStream stream = new MemoryStream()) {
                using (var writer = XmlWriter.Create(stream, settings)) {
                    var htmlWriter = new HtmlWriter(writer);
                    try {
                        writer.WriteStartElement("html");
                        writer.WriteStartElement("head");
                        WriteStyle(htmlWriter);
                        writer.WriteEndElement();
                        writer.WriteStartElement("body");
                        switchStart:
                        switch(command) {
                            case "inspect":
                                ExecuteInspect(htmlWriter, ctxt.Request.QueryString);
                                break;
                            default:
                                command = "inspect";
                                goto switchStart;
                        }
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    } catch (Exception ex) {
                        htmlWriter.Write(ex.Message);
                        htmlWriter.Break();
                        using (htmlWriter.Tag("pre"))
                            htmlWriter.Write(ex.StackTrace);
                        writer.WriteFullEndElement();
                    }
                }
                stream.Flush();
                ctxt.Response.ContentLength64 = stream.Length;
                stream.Position = 0;
                stream.WriteTo(ctxt.Response.OutputStream);
            }
            ctxt.Response.OutputStream.Close();
        }

        private static void WriteStyle(HtmlWriter writer) {
            using (writer.Tag("style")) {
                writer.Write(
@"table {
    font-family: arial, sans-serif;
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
}"
                );
            }
        }

        private void ExecuteInspect(HtmlWriter writer, NameValueCollection args) {
            if (args["assembly"] != null) {
                var asm = args["assembly"];
                if (args["type"] != null) {
                    var type = args["type"];
                    if (args["method"] != null)
                        InspectMethod(writer, asm, type, args["method"]);
                    else
                        InspectType(writer, asm, type);
                } else if (args["namespace"] != null) {
                    InspectNamespace(writer, asm, args["namespace"]);
                } else {
                    InspectAssembly(writer, asm);
                } 
            } else {
                InspectDomain(writer);
            }
        }

        private void MakeTable<T>(HtmlWriter writer, IEnumerable<T> ts, params Action<T>[] inner) {
            using(writer.Tag("table")) {
                foreach (var t in ts) {
                    using (writer.Tag("tr")) {
                        foreach(var cell in inner) {
                            using (writer.Tag("td"))
                                cell(t);
                        }
                    }
                }
            }
        }

        private void WriteAttributes(HtmlWriter writer, object[] attributes, bool stacked=true) {
            if (attributes.Length == 0) return;
            if (!stacked) writer.Write("[");
            for (int i = 0; i < attributes.Length; i++) {
                if (stacked) writer.Write("[");
                else if (i > 0) writer.Write(" ,");
                var type = attributes[i].GetType();
                TypeLink(writer, type);
                var properties = type.GetProperties(AllInstanceBindings);
                if (properties.Length > 0) {
                    bool first = true;
                    for (int j = 0; j < properties.Length; j++) {
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
                        else if (value is string) {
                            writer.Write("\"");
                            writer.Write((value as string));
                            writer.Write("\"");
                        }
                    }
                    if (!first)
                        writer.Write(" ) ");
                }

                if (stacked) {
                    writer.Write("]");
                    writer.Break();
                }
            }
            if (!stacked) writer.Write("]");
        }


        private void FunctionLink(HtmlWriter writer, MethodBase method) {
            FunctionLink(writer, method, method.DeclaringType.Name + "." + method.Name);
        }

        private void FunctionLink(HtmlWriter writer, MethodBase method, string txt) {
            writer.AHref(txt,
                Html.Url(
                    _completePrefix + "inspect",
                    "assembly", method.DeclaringType.Assembly.FullName,
                    "type", method.DeclaringType.FullName,
                    "method", method.ToString()
                )
            );
        }

        private void TypeLink(HtmlWriter writer, Type type) {
            TypeLink(writer, type, type.PrettyName());
        }

        private void TypeLink(HtmlWriter writer, Type type, string txt) {
            writer.AHref(txt,
                Html.Url(
                    _completePrefix + "inspect",
                    "assembly", type.Assembly.FullName,
                    "type", type.FullName
                )
            );
        }

        private void NamespaceLink(HtmlWriter writer, Namespace ns) {
            NamespaceLink(writer, ns, ns.FullName);
        }

        private void NamespaceLink(HtmlWriter writer,  Namespace ns, string txt) {
            writer.AHref(txt,
                Html.Url(
                    _completePrefix + "inspect",
                    "assembly", ns.Assembly.FullName,
                    "namespace", ns.FullName
                )
            );
        }

        private void AssemblyLink(HtmlWriter writer, Assembly assembly) {
            AssemblyLink(writer, assembly, assembly.Name);
        }

        private void AssemblyLink(HtmlWriter writer, Assembly assembly, string txt) {
            writer.AHref(txt,
                Html.Url(
                    _completePrefix + "inspect",
                    "assembly", assembly.FullName
                )
            );
        }
    }
}