using NUnit.Framework;

namespace Smpp.Tests
{
    [TestFixture]
    public class CommonTests
    {
        private string _8bitString = "Í<è\u0006\u0012§é\u00A0vy>\u000F\u009FË\u00A09]\u009Ev\u009F\u0001";
        private string _string = "My 7 bit message string";
        private string _stringHex = "4D79203720626974206D65737361676520737472696E67";

        [Test]
        public void ConvertStringToHexTest()
        {
            var result = Common.StringToHex(_string);
            Assert.That(result, Is.EqualTo(_stringHex), "string should be converted to Hex");
        }

        [Test]
        public void ConvertHexToStringTest()
        {
            var result = Common.HexToString(_stringHex);
            Assert.That(result, Is.EqualTo(_string), "string should be converted from Hex to string");
        }

        [Test]
        public void Convert8bitTo7bitTest()
        {
            var result = Common.Convert8bitTo7bit(Common.StringToHex8Bit(_8bitString));

            // to expected result trailing 0 should be added, it is for missing bit during conversion, by design
            Assert.That(result, Is.EqualTo(_string + "\0"), "Error converting 8 bit to 7 bit");
        }

        [Test]
        public void Convert7bitTo8bitTest()
        {
            var result = Common.Convert7bitTo8bit(_string);

            Assert.That(result, Is.EqualTo(_8bitString), "Error converting 7 bit to 8 bit");
        }
    }
}
