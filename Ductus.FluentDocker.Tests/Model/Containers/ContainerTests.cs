using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Containers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Ductus.FluentDocker.Tests.Model.Containers
{
  [TestClass]
  public class ContainerTests
  {
    [TestMethod]
    public void TestWithNoCreated()
    {
      var data = ((TemplateString)"Model/Containers/inspect_no_create.json").FromFile();
      var obj = JsonConvert.DeserializeObject<Container>(data);

      Assert.AreEqual(obj.Created, default(System.DateTime));
    }

  }
}