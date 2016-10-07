namespace Smpp.Events
{
    public class GateEvents
    {
        public delegate void LogEventHandler(LogEvent.Level level, string description);
        public event LogEventHandler Event;

        public delegate void LogChannelEventHandler(string channelName, string description, string pdu);
        public event LogChannelEventHandler ChannelEvent;

        public delegate void LogMessageChangeStatusEventHandler(int messageId, Common.MessageStatus status, string responseMessageId = "");
        public event LogMessageChangeStatusEventHandler MessageStatusEvent;

        public delegate void LogMessageDeliverReportEventHandler(string responseMessageId, Common.MessageStatus status);
        public event LogMessageDeliverReportEventHandler MessageDeliveryReportEvent;

        public delegate void LogNewMessageEventHandler(string channelName, string messageId, string sender, string recipient, string body, string bodyFormat, int registeredDelivery);
        public event LogNewMessageEventHandler NewMessageEvent;

        /// <summary>
        /// Event which indicates Informational or Error event
        /// </summary>
        /// <param name="level">Event level</param>
        /// <param name="description">The description</param>
        public void LogEvent(LogEvent.Level level, string description)
        {
            LogEventHandler handler = Event;
            if (handler != null) handler(level, description);
        }

        /// <summary>
        /// Event which indicates channel communication message was received or sent
        /// </summary>
        /// <param name="channelName">The channel name</param>
        /// <param name="description">Event description</param>
        /// <param name="pdu">The PDU of the message, used for debugging, debug flag for the channel should be true to log PDU</param>
        public void LogChannelEvent(string channelName, string description, string pdu = "")
        {
            LogChannelEventHandler handler = ChannelEvent;
            if (handler != null) handler(channelName, description, pdu);
        }

        /// <summary>
        /// Event which indicated that message status was changed
        /// </summary>
        /// <param name="messageId">Internal message id</param>
        /// <param name="status">New status</param>
        /// <param name="responseMessageId">If not empty contains MessageId returned by remote server</param>
        public void LogMessageChangeStatusEvent(int messageId, Common.MessageStatus status, string responseMessageId = "")
        {
            LogMessageChangeStatusEventHandler handler = MessageStatusEvent;
            if (handler != null) handler(messageId, status, responseMessageId);
        }

        /// <summary>
        /// Event which indicates that a delivery report was received for a message which was sent before
        /// </summary>
        /// <param name="responseMessageId">Remote system message id</param>
        /// <param name="status">New status</param>
        public void LogMessageDeliverReportEvent(string responseMessageId, Common.MessageStatus status)
        {
            LogMessageDeliverReportEventHandler handler = MessageDeliveryReportEvent;
            if (handler != null) handler(responseMessageId, status);
        }

        /// <summary>
        /// Event which indicates that new message was received
        /// </summary>
        /// <param name="channelName">The channel which received message</param>
        /// <param name="messageId"></param>
        /// <param name="sender"></param>
        /// <param name="recipient"></param>
        /// <param name="body"></param>
        /// <param name="bodyFormat"></param>
        /// <param name="registeredDelivery"></param>
        public void LogNewMessageEvent(string channelName, string messageId, string sender, string recipient, string body, string bodyFormat, int registeredDelivery)
        {
            LogNewMessageEventHandler handler = NewMessageEvent;
            if (handler != null) handler(channelName, messageId, sender, recipient, body, bodyFormat, registeredDelivery);
        }
    }
}
