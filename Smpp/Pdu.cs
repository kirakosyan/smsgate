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
            string key;
            object value;

            while (tail != String.Empty)
            {
                Common.GetTLV(out key, out value, ref tail);
                tlv.Add(key, value);
            }
        }

        /// <summary>
        /// PDU constructor for responses
        /// </summary>
        protected Pdu()
        {
            tlv = new SortedList<string, object>();
            _sequenceNumber = 0;
        }

        /// <summary>
        /// Pdu constructor for requests
        /// </summary>
        /// <param name="pdu">The request pdu</param>
        protected Pdu(string pdu)
        {
            tlv = new SortedList<string, object>();

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

            response.Append(BuildHeader((Common.CommandId)command_id, 0, sequence_number));

            len = response.Length / 2 + 4;

            return len.ToString("X8") + response;
        }

        #endregion
    }
}
