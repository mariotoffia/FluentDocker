using System.IO;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Extensions.Utils;
using Ductus.FluentDocker.Model.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ductus.FluentDocker.Tests.ExtensionTests
{
    [TestClass]
    public class CommandExtensionTest
    {
        [TestMethod, ExpectedException(typeof(FluentDockerException))]
        public void MissingDockerComposeShallThrowExceptionInResolveBinary()
        {
          var dockerFile = Path.Combine(Directory.GetCurrentDirectory(), FdOs.IsWindows() ? "docker.exe" : "docker");
          try
          {
            using (StreamWriter outputFile = new StreamWriter(dockerFile))
            {
                outputFile.WriteLine("fake docker client to satisfy DockerBinariesResolver");
            }

            var resolver = new DockerBinariesResolver(SudoMechanism.None, "", Directory.GetCurrentDirectory());
            "docker-compose".ResolveBinary(resolver, false);
            Assert.Fail("Shall never reach here since it shall throw a FluentDockerException");
          } finally {
            System.IO.File.Delete(dockerFile);
          }
        }
    }
}