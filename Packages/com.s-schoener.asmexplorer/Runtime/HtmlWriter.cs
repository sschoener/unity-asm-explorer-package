using System;
using System.IO;
using System.Security;
using System.Xml;

namespace AsmExplorer
{
    public class HtmlWriter {

        private TextWriter m_Writer;
        public HtmlWriter(TextWriter writer) {
            m_Writer = writer;
        }

        void Start(string t)
        {
            m_Writer.Write('<');
            m_Writer.Write(t);
            m_Writer.Write('>');
        }

        void Start(string t, params string[] attributes)
        {
            m_Writer.Write('<');
            m_Writer.Write(t);
            for (int i = 0; i < attributes.Length; i += 2) {
                m_Writer.Write(' ');
                m_Writer.Write(attributes[i]);
                m_Writer.Write("=\"");
                Escaped(attributes[i + 1]);
                m_Writer.Write('"');
            }
            m_Writer.Write('>');
        }

        void Escaped(string t) => m_Writer.Write(SecurityElement.Escape(t));

        void End(string t)
        {
            m_Writer.Write('<');
            m_Writer.Write('/');
            m_Writer.Write(t);
            m_Writer.Write('>');
        }

        public void Inline(string tag, string text)
        {
            Start(tag);
            Escaped(text);
            End(tag);
        }

        public void Break() {
            m_Writer.Write("<br>");
        }

        public void Inline(string tag, string text, params string[] attributes)
        {
            Start(tag, attributes);
            Escaped(text);
            End(tag);
        }

        public void Write(string s) => m_Writer.Write(SecurityElement.Escape(s));

        public void WriteLine(string s)
        {
            Write(s); Break();
        }


        public TagHandle Tag(string tag) {
            Start(tag);
            return new TagHandle(this, tag);
        }

        public TagHandle Tag(string tag, params string[] attributes) {
            Start(tag, attributes);
            return new TagHandle(this, tag);
        }

        public void AHref(string content, string href) {
            Start("a", "href", href);
            Escaped(content);
            End("a");
        }


        public struct TagHandle : IDisposable
        {
            readonly HtmlWriter m_Writer;
            readonly string m_Tag;

            public TagHandle(HtmlWriter writer, string tag) {
                m_Writer = writer;
                m_Tag = tag;
            }

            public void Dispose()
            {
                m_Writer?.End(m_Tag);
            }
        }
    }

    static class HtmlWriterExtensions
    {
        public static HtmlWriter.TagHandle ContainerFluid(this HtmlWriter writer, params string[] attributes)
        {
            return writer.Tag("div class=\"container-fluid\"", attributes);
        }
    }
}