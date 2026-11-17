using Smpp.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Smpp
{
    /// <summary>
    /// Abstract PDU class which should be a base for all messages
    /// </summary>
    public abstract class Pdu
    {
        #region private

        private readonly string _pdu;

        private uint _commandId;
        private uint _sequenceNumber;
        private uint _commandStatus;
        private uint _commandLength;

        #endregion

        #region members

        /// <summary>
        /// TLV list
        /// </summary>
        public SortedList<string, object> tlv;

        /// <summary>
        /// Load TLVs (Tag Length Value)
        /// </summary>
        /// <param name="tail">the tail of the PDU</param>
        public void LoadTLV(string tail)
        {
            if (tail.Length == 0) return;
            int offset = 0;
            Common.ReadTlv(tail, ref offset, out string key, out object value);
            if (offset == tail.Length)
            {
                tlv.Add(key, value);
                return;
            }

            // Preserve the public collection and its comparer. Sorting once avoids shifting the
            // SortedList backing arrays for each out-of-order parameter in a received PDU.
            var parameters = new List<KeyValuePair<string, object>>(tlv);
            parameters.Add(new(key, value));
            while (offset < tail.Length)
            {
                Common.ReadTlv(tail, ref offset, out key, out value);
                parameters.Add(new(key, value));
            }
            parameters.Sort((left, right) => tlv.Comparer.Compare(left.Key, right.Key));
            for (int i = 1; i < parameters.Count; i++)
                if (tlv.Comparer.Compare(parameters[i - 1].Key, parameters[i].Key) == 0)
                    throw new ArgumentException("Duplicate SMPP optional parameter " + parameters[i].Key + ".");
            tlv.Clear();
            if (tlv.Capacity < parameters.Count) tlv.Capacity = parameters.Count;
            foreach (var parameter in parameters)
                tlv.Add(parameter.Key, parameter.Value);
        }

        /// <summary>
        /// PDU constructor for responses
        /// </summary>
        protected Pdu()
        {
            tlv = new SortedList<string, object>(StringComparer.OrdinalIgnoreCase);
            _sequenceNumber = 0;
        }

        /// <summary>
        /// Pdu constructor for requests
        /// </summary>
        /// <param name="pdu">The request pdu</param>
        protected Pdu(string pdu)
        {
            tlv = new SortedList<string, object>(StringComparer.OrdinalIgnoreCase);

            _pdu = pdu;

            _commandLength = uint.Parse(_pdu.Substring(0, 8), System.Globalization.NumberStyles.HexNumber);

            if (_commandLength * 2 != pdu.Length)
            {
                _commandId = 0;
                _commandStatus = 0;
                _sequenceNumber = 0;

                throw new CommandLengthException(pdu);
            }

            _commandId = uint.Parse(_pdu.Substring(8, 8), System.Globalization.NumberStyles.HexNumber);
            _commandStatus = uint.Parse(_pdu.Substring(16, 8), System.Globalization.NumberStyles.HexNumber);
            _sequenceNumber = uint.Parse(_pdu.Substring(24, 8), System.Globalization.NumberStyles.HexNumber);
        }

        public uint command_id
        {
            get
            {
                return _commandId;
            }
            set
            {
                _commandId = value;
            }
        }

        public uint sequence_number
        {
            get
            {
                return _sequenceNumber;
            }
            set
            {
                _sequenceNumber = value;
            }
        }

        public uint command_status
        {
            get
            {
                return _commandStatus;
            }
            set
            {
                _commandStatus = value;
            }
        }

        protected string body
        {
            get
            {
                return _pdu.Substring(Common.HEADER_LENGTH);
            }
        }

        #endregion

        #region utility methods

        internal readonly record struct BodyEncoding(string Text, int Coding, int Mode, int Split, bool Packed, byte Reference, bool Gsm7);
        private sealed record ReceivedBody(BodyEncoding Encoding, MessageBodyCodec.Part Part, object Payload);
        private ReceivedBody receivedBody;

        internal void RememberWireBody(BodyEncoding encoding, string shortMessageHex, int esmClass)
        {
            tlv.TryGetValue("0424", out var payload);
            var field = (shortMessageHex.Length / 2).ToString("X2") + shortMessageHex;
            receivedBody = new(encoding, new MessageBodyCodec.Part(field, (esmClass & 0x40) != 0), payload);
        }

        internal MessageBodyCodec.Part[] EncodeBody(BodyEncoding encoding, out bool originalBody)
        {
            // A received part can contain a UDH, provider-specific alphabet or packed padding.
            // Forward its original octets unless the application changed the body or its encoding.
            originalBody = receivedBody?.Encoding == encoding;
            if (originalBody) return new[] { receivedBody.Part };
            return MessageBodyCodec.Encode(encoding.Text, encoding.Coding, encoding.Mode,
                encoding.Split, encoding.Packed, encoding.Reference, encoding.Gsm7);
        }

        internal int EncodeEsmClass(int esmClass, MessageBodyCodec.Part part, bool originalBody)
        {
            if (originalBody) return esmClass;
            if (receivedBody?.Part.HasUserDataHeader == true)
                esmClass &= ~0x40;
            return esmClass | (part.HasUserDataHeader ? 0x40 : 0);
        }

        protected string EncodeOptionalParameters(bool hasPayload, bool hasShortMessage)
            => EncodeMessageParameters(hasPayload, hasShortMessage, originalBody: false);

        internal string EncodeMessageParameters(bool hasPayload, bool hasShortMessage, bool originalBody)
        {
            var result = new StringBuilder();
            foreach (var parameter in tlv)
            {
                if (parameter.Key.Equals("0424", StringComparison.OrdinalIgnoreCase))
                {
                    // On a body edit, the encoder supplies the replacement payload. Keep the raw
                    // parsed TLV available to callers, and still reject a second, manually set one.
                    if (!originalBody && receivedBody != null && Equals(receivedBody.Payload, parameter.Value)) continue;
                    if (hasPayload || hasShortMessage)
                        throw new ArgumentException("message_payload cannot duplicate Body or be combined with short_message.");
                }
                if (parameter.Key.Length != 4)
                    throw new ArgumentException("TLV tags must contain four hexadecimal digits.");
                ushort tag = ushort.Parse(parameter.Key, System.Globalization.NumberStyles.HexNumber);
                byte[] bytes = HexCodec.FromHexString(parameter.Value.ToString());
                if (bytes.Length > ushort.MaxValue)
                    throw new ArgumentException("TLV values cannot exceed 65,535 octets.");
                result.Append($"{tag:X4}{bytes.Length:X4}{HexCodec.ToHexString(bytes)}");
            }
            return result.ToString();
        }

        /// <summary>
        /// Builds PDU header
        /// </summary>
        /// <param name="command_id">Command id</param>
        /// <param name="command_status">Command status</param>
        /// <param name="sequence_number">Sequence number</param>
        /// <returns>Pdu header as a string</returns>
        protected string BuildHeader(Common.CommandId command_id, uint command_status, uint sequence_number)
        {
            var header = new StringBuilder();

            header.AppendFormat("{0:X8}{1:X8}{2:X8}", (int)command_id, command_status, sequence_number);

            return header.ToString();
        }

        #endregion

        #region overridden methods

        /// <summary>
        /// Decodes the bind request for the SMSC.  This version throws a NotImplementedException.
        /// </summary>
        public virtual void Decode()
        {
            throw new NotImplementedException("DecodeSmscResponse is not implemented in Pdu.");
        }

        /// <summary>
        /// Encodes the response for PDU to ESME
        /// </summary>
        public virtual string Encode()
        {
            var response = new StringBuilder();
            int len = 0;

            response.Append(BuildHeader((Common.CommandId)command_id, command_status, sequence_number));

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }

        #endregion
    }
}
