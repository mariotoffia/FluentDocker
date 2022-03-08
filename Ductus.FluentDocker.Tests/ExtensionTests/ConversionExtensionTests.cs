using System.Numerics;
using Ductus.FluentDocker.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ExtensionTests
{
  [TestClass]
  public class ConversionExtensionTests
  {
    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    public void NullStringShallGiveMinimumValue(string input)
    {
      var num = input.Convert();
      Assert.IsTrue(string.IsNullOrEmpty(input));
      Assert.AreEqual(long.MinValue, num);
    }

    [TestMethod]
    [DataRow("2googles")]
    [DataRow("42p")]
    [DataRow("wrongFormat42")]
    [DataRow("-3498lfk")]
    public void InvalidUnitInputShallGiveMinimumValue(string input)
    {
      var num = input.Convert();
      Assert.AreEqual(long.MinValue, num);
    }

    [TestMethod]
    public void LessThanLongMinimumValueShallGiveMinimumValue()
    {
      var lessThanMinimum = (new BigInteger(long.MinValue)) - 1;
      var input = lessThanMinimum.ToString() + "g";

      var num = input.Convert();
      Assert.AreEqual(long.MinValue, num);
    }

    [TestMethod]
    public void GreaterThanLongMaximumValueShallGiveMinimumValue()
    {
      var greaterThanMaximum = (new BigInteger(long.MaxValue)) + 1;
      var input = greaterThanMaximum.ToString() + "g";

      var num = input.Convert();
      Assert.AreEqual(long.MinValue, num);
    }

    [TestMethod]
    public void ValidByteInputShallGiveExactNumber()
    {
      var input = "42b";

      var num = input.Convert();
      Assert.AreEqual(42, num);
    }

    [TestMethod]
    public void ValidKilobyteInputShallGiveCorrectKilobyteNumber()
    {
      var input = "42k";

      var num = input.Convert();
      Assert.AreEqual(43008, num);
    }

    [TestMethod]
    public void ValidMegabyteInputShallGiveCorrectMegabyteNumber()
    {
      var input = "42m";

      var num = input.Convert();
      Assert.AreEqual(44040192, num);
    }

    [TestMethod]
    public void ValidGigabyteInputShallGiveCorrectGigabyteNumber()
    {
      var input = "42g";

      var num = input.Convert();
      Assert.AreEqual(45097156608, num);
    }
  }
}
