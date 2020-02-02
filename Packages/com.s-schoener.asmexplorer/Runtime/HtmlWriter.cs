using System;
using System.Xml;

namespace AsmExplorer
{
    public class HtmlWriter {

        private XmlWriter m_Writer;
        public HtmlWriter(XmlWriter writer) {
            m_Writer = writer;
        }

        public void Inline(string tag, string text) {
            m_Writer.WriteStartElement(tag);
            m_Writer.WriteString(text);
            m_Writer.WriteEndElement();
        }

        public void Break() {
            m_Writer.WriteStartElement("br");
            m_Writer.WriteEndElement();
        }

        public void Inline(string tag, string text, params string[] attributes) {
            m_Writer.WriteStartElement(tag);
            for (int i = 0; i < attributes.Length; i += 2) {
                m_Writer.WriteAttributeString(attributes[i], attributes[i+1]);
            }
            m_Writer.WriteString(text);
            m_Writer.WriteEndElement();
        }

        public void Write(string s) {
            m_Writer.WriteString(s);
        }

        public TagHandle Tag(string tag) {
            m_Writer.WriteStartElement(tag);
            return new TagHandle(m_Writer);
        }

        public TagHandle Tag(string tag, params string[] attributes) {
            m_Writer.WriteStartElement(tag);
            WriteAttributes(attributes);
            return new TagHandle(m_Writer);
        }

        public void AHref(string content, string href, params string[] attributes) {
            m_Writer.WriteStartElement("a");
            m_Writer.WriteAttributeString("href", href);
            WriteAttributes(attributes);
            m_Writer.WriteString(content);
            m_Writer.WriteEndElement();
        }

        public void AHref(string content, string href) {
            m_Writer.WriteStartElement("a");
            m_Writer.WriteAttributeString("href", href);
            m_Writer.WriteString(content);
            m_Writer.WriteEndElement();
        }

        public void WriteAttributes(string[] attributes) {
            for (int i = 0; i < attributes.Length; i += 2) {
                m_Writer.WriteAttributeString(attributes[i], attributes[i+1]);
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