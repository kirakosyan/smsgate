using System;

namespace Smpp.Exceptions
{
    /// <summary>
    /// Command length exception fired when pdu is broken and content has wrong length
    /// </summary>
    public class CommandLengthException : ApplicationException
    {
        private string _message = "Command length error.";
        private string _pdu;

        public CommandLengthException(string pdu)
        {
            _pdu = pdu;
        }

        public override string Message
        {
            get
            {
                return _message;
            }
        }

        public string Pdu
        {
            get
            {
                return _pdu;
            }
        }
    }
}
