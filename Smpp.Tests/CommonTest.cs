using NUnit.Framework;

namespace Smpp.Tests
{
    [TestFixture]
    public class CommonTests
    {
        private string _8bitString = "Í<è§é vy>Ë 9]v";
        private string _string = "My 7 bit message string";
        private string _stringHex = "4D79203720626974206D65737361676520737472696E67";

        [Test]
        public void ConvertStringToHexTest()
        {
            var result = Common.StringToHex(_string);
            Assert.AreEqual(_stringHex, result, "string should be converted to Hex");
        }

        [Test]
        public void ConvertHexToStringTest()
        {
            var result = Common.HexToString(_stringHex);
            Assert.AreEqual(_string, result, "string should be converted from Hex to string");
        }

        [Test]
        public void Convert8bitTo7bitTest()
        {
            var result = Common.Convert8bitTo7bit(Common.StringToHex8Bit(_8bitString));

            // to expected result trailing 0 should be added, it is for missing bit during conversion, by design
            Assert.AreEqual(_string + "\0", result, "Error converting 8 bit to 7 bit");
        }

        [Test]
        public void Convert7bitTo8bitTest()
        {
            var result = Common.Convert7bitTo8bit(_string);

            Assert.AreEqual(_8bitString, result, "Error converting 7 bit to 8 bit");
        }
    }
}
