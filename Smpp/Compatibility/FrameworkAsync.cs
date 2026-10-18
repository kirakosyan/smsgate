#if NETFRAMEWORK
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Smpp
{
    internal static class FrameworkAsync
    {
        internal static Task CancelAsync(this CancellationTokenSource cancellation) => Task.Run(() => cancellation.Cancel());
        internal static bool Remove<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        {
            if (!dictionary.TryGetValue(key, out value)) return false;
            return dictionary.Remove(key);
        }
        internal static async Task WithCancellation(Func<Task> operation, Action abort, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            using (cancellation.Register(() => Abort(abort)))
            {
                try { await operation().ConfigureAwait(false); }
                catch (Exception) when (cancellation.IsCancellationRequested) { throw new OperationCanceledException(cancellation); }
            }
            // Dispose waits for an in-progress abort before success can transfer ownership.
            cancellation.ThrowIfCancellationRequested();
        }
        // A successful accept transfers ownership only after cancellation has been checked.
        internal static async Task<T> WithCancellation<T>(Func<Task<T>> operation, Action abort, CancellationToken cancellation) where T : class, IDisposable
        {
            T result = default;
            try
            {
                await WithCancellation(async () => { result = await operation().ConfigureAwait(false); }, abort, cancellation).ConfigureAwait(false);
                return result;
            }
            catch
            {
                result?.Dispose();
                throw;
            }
        }
        private static void Abort(Action abort)
        {
            try { abort(); }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
            catch (SocketException) { }
        }
        internal static ValueTask ConnectAsync(this TcpClient client, string host, int port, CancellationToken cancellation) =>
            new ValueTask(WithCancellation(() => client.ConnectAsync(host, port), client.Close, cancellation));
        internal static ValueTask<TcpClient> AcceptTcpClientAsync(this TcpListener listener, CancellationToken cancellation) =>
            new ValueTask<TcpClient>(WithCancellation(listener.AcceptTcpClientAsync, listener.Stop, cancellation));
        internal static ValueTask<int> ReadAsync(this Stream stream, byte[] buffer) => new ValueTask<int>(stream.ReadAsync(buffer, 0, buffer.Length));
        internal static void Write(this Stream stream, byte[] buffer) => stream.Write(buffer, 0, buffer.Length);
        internal static ValueTask WriteAsync(this Stream stream, byte[] buffer, CancellationToken cancellation = default) =>
            new ValueTask(WithCancellation(() => stream.WriteAsync(buffer, 0, buffer.Length, cancellation), stream.Dispose, cancellation));
        internal static ValueTask ReadExactlyAsync(this Stream stream, byte[] buffer, CancellationToken cancellation = default) =>
            new ValueTask(ReadExactlyAsync(stream, buffer, 0, buffer.Length, cancellation));
        internal static async Task ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellation)
        {
            await WithCancellation(async () =>
            {
                int end = checked(offset + count);
                while (offset < end)
                {
                    int read = await stream.ReadAsync(buffer, offset, end - offset, cancellation).ConfigureAwait(false);
                    if (read == 0) throw new EndOfStreamException();
                    offset += read;
                }
            }, stream.Dispose, cancellation).ConfigureAwait(false);
        }
        internal static Task WaitAsync(this Task task, CancellationToken cancellation) => WaitAsync(task, Timeout.InfiniteTimeSpan, cancellation);
        internal static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellation = default)
        {
            if (task.IsCompleted) { await task.ConfigureAwait(false); return; }
            cancellation.ThrowIfCancellationRequested();
            using (var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                var delay = Task.Delay(timeout, delayCancellation.Token);
                try
                {
                    if (await Task.WhenAny(task, delay).ConfigureAwait(false) == task)
                    {
                        await task.ConfigureAwait(false);
                        return;
                    }
                    _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    cancellation.ThrowIfCancellationRequested();
                    throw new TimeoutException();
                }
                finally { delayCancellation.Cancel(); }
            }
        }
        internal static async Task<T> WaitAsync<T>(this Task<T> task, CancellationToken cancellation)
        {
            await WaitAsync((Task)task, cancellation).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }
        internal static async Task<T> WaitAsync<T>(this Task<T> task, TimeSpan timeout, CancellationToken cancellation = default)
        {
            await WaitAsync((Task)task, timeout, cancellation).ConfigureAwait(false);
            return await task.ConfigureAwait(false);
        }
    }
}
#endif
