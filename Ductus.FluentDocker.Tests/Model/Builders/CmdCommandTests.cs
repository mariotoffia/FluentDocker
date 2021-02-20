using Ductus.FluentDocker.Model.Builders.FileBuilder;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.Model.Builders
{
  [TestClass]
  public class CmdCommandTests
  {
    [TestMethod]
    public void SimpleConstructor()
    {
      var cmd = new CmdCommand("/bin/bash", "arg1", "arg2");

      Assert.AreEqual("/bin/bash", cmd.Cmd);

      Assert.AreEqual(2, cmd.Arguments.Length);
      Assert.AreEqual("\"arg1\"", cmd.Arguments[0]);
      Assert.AreEqual("\"arg2\"", cmd.Arguments[1]);
    }

    [TestMethod]
    public void ToStringWithParams()
    {
      var cmd = new CmdCommand("/bin/bash", "arg1", "arg2");

      Assert.AreEqual("CMD [\"/bin/bash\", \"arg1\", \"arg2\"]", cmd.ToString());
    }

    [TestMethod]
    public void ConstructorNoParams()
    {
      var cmd = new CmdCommand("/bin/bash");

      Assert.AreEqual("/bin/bash", cmd.Cmd);

      Assert.AreEqual(0, cmd.Arguments.Length);
    }

    [TestMethod]
    public void ToStringNoParams()
    {
      var cmd = new CmdCommand("/bin/bash");

      Assert.AreEqual("CMD [\"/bin/bash\"]", cmd.ToString());
    }
  }
}
