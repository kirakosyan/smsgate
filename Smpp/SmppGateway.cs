using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Smpp.Events;

namespace Smpp
{
    /// <summary>Instance-owned outbound clients suitable for dependency injection. Does not use Gate's static registrations.</summary>
    public sealed class SmppGateway : IDisposable, IAsyncDisposable
    {
        private readonly Dictionary<string, Client> clients = new(StringComparer.Ordinal);
        private bool disposed;
        public GateEvents Events { get; }
        public SmppGateway(GateEvents events) => Events = events ?? throw new ArgumentNullException(nameof(events));
        public Client AddClient(SmppClientOptions options)
        {
            Guard.ThrowIfNull(options);
            lock (clients)
            {
                Guard.ThrowIfDisposed(disposed, this);
                options.Validate();
                if (clients.ContainsKey(options.ChannelName)) throw new ArgumentException("Duplicate channel name.", nameof(options));
                var client = options.CreateClient(Events);
                clients.Add(options.ChannelName, client);
                return client;
            }
        }
        public IReadOnlyList<Client> Clients { get { lock (clients) return Array.AsReadOnly(clients.Values.ToArray()); } }
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            Client[] snapshot;
            lock (clients) { disposed = true; snapshot = clients.Values.ToArray(); }
            try { await Task.WhenAll(snapshot.Select(client => client.StopAsync(cancellationToken))).ConfigureAwait(false); }
            finally { foreach (var client in snapshot) client.Dispose(); }
        }
        public void Dispose()
        {
            lock (clients)
            {
                disposed = true;
                foreach (var client in clients.Values) client.Dispose();
                clients.Clear();
            }
        }
        public async ValueTask DisposeAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await StopAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            finally { Dispose(); }
        }
    }
}
