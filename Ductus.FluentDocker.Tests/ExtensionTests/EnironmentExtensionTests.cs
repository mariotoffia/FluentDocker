using Ductus.FluentDocker.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ExtensionTests
{
  [TestClass]
  public class EnironmentExtensionTests
  {
    [TestMethod]
    public void NullStringShallGiveNullReturnInExtract()
    {
      var t = EnvironmentExtensions.Extract(null);
      Assert.IsNull(t);
    }
    [TestMethod]
    public void EmptyStringShallGiveNullReturnInExtract()
    {
      var t = "".Extract();
      Assert.IsNull(t);
    }
    [TestMethod]
    public void OnlyWhitespaceStringShallGiveNullReturnInExtract()
    {
      var t = "   ".Extract();
      Assert.IsNull(t);
    }
    [TestMethod]
    public void SingleNameNotEqualSignGivesStringShallGiveNameAnEmptyStringReturnInExtract()
    {
      var t = "CUSTOM_ENV".Extract();
      Assert.AreEqual("CUSTOM_ENV", t.Item1);
      Assert.IsTrue(t.Item2 == string.Empty);
    }
    [TestMethod]
    public void SingleNameWithEqualSignGivesStringShallGiveNameAnEmptyStringReturnInExtract()
    {
      var t = "CUSTOM_ENV=".Extract();
      Assert.AreEqual("CUSTOM_ENV", t.Item1);
      Assert.IsTrue(t.Item2 == string.Empty);
    }
    [TestMethod]
    public void NameValueShallReturnNameAndValue()
    {
      var t = "CUSTOM_ENV=custom value".Extract();
      Assert.AreEqual("CUSTOM_ENV", t.Item1);
      Assert.AreEqual("custom value", t.Item2);
    }
    [TestMethod]
    public void ItShallBePossibleToHaveEqualSignsInTheValue()
    {
      var t = "CUSTOM_ENV=custom value with = sign shall be possible".Extract();
      Assert.AreEqual("CUSTOM_ENV", t.Item1);
      Assert.AreEqual("custom value with = sign shall be possible", t.Item2);
    }
  }
}
