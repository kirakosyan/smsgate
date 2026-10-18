using System;
using System.Net;
using System.Net.Sockets;

namespace Smpp.Tests
{
    internal sealed class TestTcpListener : TcpListener, IDisposable
    {
        internal TestTcpListener(IPAddress address, int port) : base(address, port) { }
        void IDisposable.Dispose() => Stop();
    }
}
