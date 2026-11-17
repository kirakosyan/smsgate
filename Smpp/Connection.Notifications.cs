using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Smpp.Events;

namespace Smpp
{
    public abstract partial class Connection
    {
        private enum NotificationKind { Observation, Diagnostic, Incoming, Measurement }
        private readonly Queue<(Action Callback, NotificationKind Kind)> notifications = new();
        private int maxPendingNotifications = 2048;
        private int incomingNotifications;
        private int observationalNotifications;
        private bool dispatchingNotifications;
        private TaskCompletionSource<bool> notificationDrain;
        private long droppedNotifications;

        /// <summary>Maximum queued inbound message callbacks and, separately, observational callbacks per connection.</summary>
        public int MaxPendingNotifications
        {
            get { lock (notifications) return maxPendingNotifications; }
            set { Guard.ThrowIfNegativeOrZero(value); lock (notifications) maxPendingNotifications = value; }
        }

        /// <summary>Observational callbacks omitted due to a full backlog. Inbound messages are throttled before acknowledgement.</summary>
        public long DroppedNotifications { get { lock (notifications) return droppedNotifications; } }

        // Call under SessionLock. Each pending acceptance can still need one inbound callback slot.
        private bool HasInboundNotificationCapacity()
        {
            lock (notifications)
                return (long)incomingNotifications + pendingAcknowledgements < maxPendingNotifications;
        }

        protected void Notify(Action notification) => EnqueueNotification(notification, NotificationKind.Observation);
        private protected void NotifyDiagnostic(Action notification) => EnqueueNotification(notification, NotificationKind.Diagnostic);
        private void NotifyIncoming(Action notification) => EnqueueNotification(notification, NotificationKind.Incoming);

        private void EnqueueNotification(Action notification, NotificationKind kind)
        {
            lock (notifications)
            {
                if (kind == NotificationKind.Incoming)
                    incomingNotifications++;
                else if (kind == NotificationKind.Observation || kind == NotificationKind.Diagnostic)
                {
                    int limit = kind == NotificationKind.Diagnostic ? Math.Max(1, maxPendingNotifications / 2) : maxPendingNotifications;
                    if (observationalNotifications >= limit)
                    {
                        droppedNotifications++;
                        return;
                    }
                    observationalNotifications++;
                }
                notifications.Enqueue((notification, kind));
                if (dispatchingNotifications) return;
                dispatchingNotifications = true;
                _ = RunDetached(DispatchNotifications);
            }
        }

        private void DispatchNotifications()
        {
            while (true)
            {
                Action callback;
                lock (notifications)
                {
                    if (notifications.Count == 0)
                    {
                        dispatchingNotifications = false;
                        notificationDrain?.TrySetResult(true);
                        notificationDrain = null;
                        return;
                    }
                    var next = notifications.Dequeue();
                    if (next.Kind == NotificationKind.Incoming) incomingNotifications--;
                    else if (next.Kind != NotificationKind.Measurement) observationalNotifications--;
                    callback = next.Callback;
                }
                try { callback(); }
                catch (Exception ex)
                {
                    try { Events.LogEvent(LogEvent.Level.Error, channel_name + " - Application callback failed: " + ex.Message); }
                    catch (Exception) { }
                }
            }
        }

        internal Task DrainNotificationsAsync()
        {
            lock (notifications)
            {
                if (!dispatchingNotifications) return Task.CompletedTask;
                notificationDrain ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return notificationDrain.Task;
            }
        }
    }
}
