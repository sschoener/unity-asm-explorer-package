using System;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AsmExplorer {
    /// <summary>
    /// Based on https://codehosting.net/blog/BlogEngine/post/Simple-C-Web-Server,
    /// used under the MIT license: https://codehosting.net/blog/files/MITlicense.txt
    /// </summary>
    public class WebServer {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Action<HttpListenerContext> _handler;
 
        public WebServer(string[] prefixes, Action<HttpListenerContext> handler)
        {
            if (!HttpListener.IsSupported)
                throw new NotSupportedException("Needs Windows XP SP2, Server 2003 or later.");
 
            // URI prefixes are required, for example 
            // "http://localhost:8080/index/".
            if (prefixes == null || prefixes.Length == 0)
                throw new ArgumentException("prefixes");
 
            // A responder method is required
            if (handler == null)
                throw new ArgumentException("method");
 
            foreach (string s in prefixes)
                _listener.Prefixes.Add(s);
 
            _handler = handler;
            _listener.Start();
        }
 
        public WebServer(Func<HttpListenerRequest, string> handler, params string[] prefixes)
            : this(prefixes, c => {
                var ctx = c as HttpListenerContext;
                byte[] buf = Encoding.UTF8.GetBytes(handler(c.Request));
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            })
            { }

        public WebServer(Action<HttpListenerContext> handler, params string[] prefixes)
            : this(prefixes, handler) {
        }
 
        public void Run()
        {
            ThreadPool.QueueUserWorkItem(o =>
            {
                while (_listener.IsListening)
                {
                    ThreadPool.QueueUserWorkItem(c =>
                        {
                            var ctx = c as HttpListenerContext;
                            try
                            {
                                _handler(ctx);
                            } catch (Exception ex) {
                                Debug.LogException(ex);
                            } finally {
                                ctx.Response.OutputStream.Close();
                            }
                        },
                        _listener.GetContext());
                }
            });
        }
 
        public void Stop()
        {
            _listener.Stop();
            _listener.Close();
        }
    }
}