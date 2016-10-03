using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace TmaIotboxAdapter
{
    /// <summary>
    /// http://stackoverflow.com/questions/37265369/specify-proxy-in-asp-net-core-rc2
    /// </summary>
    public class FiddlerProxy : IWebProxy
    {
        public FiddlerProxy()
            : this("http://localhost:8888")
        { }
        public FiddlerProxy(string proxyUri)
            : this(new Uri(proxyUri))
        {}

        public FiddlerProxy(Uri proxyUri)
        {
            ProxyUri = proxyUri;
        }

        public Uri ProxyUri { get; set; }

        public ICredentials Credentials { get; set; }

        public Uri GetProxy(Uri destination)
        {
            return this.ProxyUri;
        }

        public bool IsBypassed(Uri host)
        {
            return false; /* Proxy all requests */
        }
    }
}
