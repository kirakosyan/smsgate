namespace Smpp.Requests
{
    public class Unbind : Pdu
    {
        public Unbind(string pdu)
            : base(pdu)
        {
        }

        public Unbind()
            : base()
        {
            sequence_number = 0;
            command_id = (uint)Common.CommandId.unbind;
        }
    }
}
