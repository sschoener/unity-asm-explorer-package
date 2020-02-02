using System;
using System.Xml;

namespace AsmExplorer
{
    public class HtmlWriter {

        private XmlWriter writer;
        public HtmlWriter(XmlWriter writer) {
            this.writer = writer;
        }

        public void Inline(string tag, string text) {
            writer.WriteStartElement(tag);
            writer.WriteString(text);
            writer.WriteEndElement();
        }

        public void Break() {
            writer.WriteStartElement("br");
            writer.WriteEndElement();
        }

        public void Inline(string tag, string text, params string[] attributes) {
            writer.WriteStartElement(tag);
            for (int i = 0; i < attributes.Length; i += 2) {
                writer.WriteAttributeString(attributes[i], attributes[i+1]);
            }
            writer.WriteString(text);
            writer.WriteEndElement();
        }

        public void Write(string s) {
            writer.WriteString(s);
        }

        public TagHandle Tag(string tag) {
            writer.WriteStartElement(tag);
            return new TagHandle(writer);
        }

        public TagHandle Tag(string tag, params string[] attributes) {
            writer.WriteStartElement(tag);
            WriteAttributes(attributes);
            return new TagHandle(writer);
        }

        public void AHref(string content, string href, params string[] attributes) {
            writer.WriteStartElement("a");
            writer.WriteAttributeString("href", href);
            WriteAttributes(attributes);
            writer.WriteString(content);
            writer.WriteEndElement();
        }

        public void AHref(string content, string href) {
            writer.WriteStartElement("a");
            writer.WriteAttributeString("href", href);
            writer.WriteString(content);
            writer.WriteEndElement();
        }

        public void WriteAttributes(string[] attributes) {
            for (int i = 0; i < attributes.Length; i += 2) {
                writer.WriteAttributeString(attributes[i], attributes[i+1]);
            }
        }

        public struct TagHandle : IDisposable
        {
            public readonly XmlWriter Writer;

            public TagHandle(XmlWriter writer) {
                Writer = writer;
            }

            public void Dispose()
            {
                Writer.WriteEndElement();    
            }

            public TagHandle With(string key, string value) {
                Writer.WriteAttributeString(key, value);
                return this;
            }
        }
    }
}