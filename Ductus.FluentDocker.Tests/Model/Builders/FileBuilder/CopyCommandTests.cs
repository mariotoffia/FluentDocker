using Ductus.FluentDocker.Model.Builders.FileBuilder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.Model.Builders.FileBuilder
{
  [TestClass]
  public class CopyCommandTests
  {
    [TestMethod]
    public void CopyCommandShallDoubleQuoteWrapAllArguments()
    {
      var cp  = new CopyCommand("entrypoint.sh", "/worker/entrypoint.sh");
      Assert.AreEqual("COPY [\"entrypoint.sh\",\"/worker/entrypoint.sh\"]", cp.ToString());
    }

    [TestMethod]
    public void CopyCommandShallNotAddDoubleQuoteWrapForArgumentsWithDoubleQote()
    {
      var cp  = new CopyCommand("entrypoint.sh", "\"/worker/entrypoint.sh\"");
      Assert.AreEqual("COPY [\"entrypoint.sh\",\"/worker/entrypoint.sh\"]", cp.ToString());
    }

    [TestMethod]
    public void CopyCommandShallEnsureBothSidesAreDoubleQotedEvenIfArgumentHasOnlyOneSide()
    {
      var cp  = new CopyCommand("entrypoint.sh", "\"/worker/entrypoint.sh");
      Assert.AreEqual("COPY [\"entrypoint.sh\",\"/worker/entrypoint.sh\"]", cp.ToString());
    }
  }
}