using System.Collections.Generic;
using Smpp.Events;

namespace Smpp
{
    /// <summary>
    /// Gate class is entry point for managing client and server connections
    /// </summary>
    public class Gate
    {
        /// <summary>
        /// The list of clients connections
        /// </summary>
        public static SortedList<string, Client> Clients = new SortedList<string, Client>();

        /// <summary>
        /// The list of server connections, connections which listen
        /// </summary>
        public static SortedList<string, Server> Servers = new SortedList<string, Server>();

        /// <summary>
        /// Gate events
        /// </summary>
        public GateEvents Events { get; set; }

        /// <summary>
        /// Initializes Gate
        /// </summary>
        public Gate(GateEvents events)
        {
            //Common.InitCommandID();
            //Common.InitCommandStatus();
            //Common.InitTLV();
            //Common.InitBodyFormat();

            Events = events;
        }

        /// <summary>
        /// Adds new client connection
        /// </summary>
        /// <param name="channelName">Unique channel name</param>
        /// <param name="host">Server IP</param>
        /// <param name="port">Server port</param>
        /// <param name="systemId">SystemId - login</param>
        /// <param name="password">System password</param>
        /// <returns>Client connection instance</returns>
        public Client AddClientConnection(string channelName, string host, int port, string systemId, string password)
        {
            var client = new Client(channelName, Events);
            client.is_server = false;
            client.host = host;
            client.port = port;
            client.system_id = systemId;
            client.password = password;

            return client;
        }

        /// <summary>
        /// Adds new server connection
        /// </summary>
        /// <param name="channelName">Unique channel name</param>
        /// <param name="systemId">SystemId - login</param>
        /// <param name="password">System password</param>
        /// <returns>Server connection instance</returns>
        public Server AddServerConnection(string channelName, string systemId, string password)
        {
            var server = new Server(channelName, Events);
            server.is_server = true;
            server.system_id = systemId;
            server.password = password;

            return server;
        }
    }
}
