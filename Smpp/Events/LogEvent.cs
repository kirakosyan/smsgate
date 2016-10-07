
namespace Smpp.Events
{
    public class LogEvent
    {
        public enum Level
        {
            Info,
            Error
        }

        public Level EventLevel;
        public string Description;
    }
}
