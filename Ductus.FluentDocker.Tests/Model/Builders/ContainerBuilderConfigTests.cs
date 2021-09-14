using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Ductus.FluentDocker.Tests.Model.Builders
{
  [TestClass]
  public class ContainerBuilderConfigTests
  {


    [DataRow(null, null, null, null, "", "")]
    [DataRow(
      new string[] { "NETWORK-SERVICE" }, // INetworkServices
      null, // INetworkServices With Alias
      null, // NetworkNames
      null, // NetworkNames With Alias
      null, //alias
      "NETWORK-SERVICE")]
    [DataRow(
      null, // INetworkServices
      new string[] { "NETWORK-SERVICE", "ALIAS" }, // INetworkServices With Alias
      null, // NetworkNames
      null, // NetworkNames With Alias
      "ALIAS", //alias
      "NETWORK-SERVICE")]
    [DataRow(
      null, // INetworkServices
      null, // INetworkServices With Alias
      new string[] { "NETWORK-NAME" }, // NetworkNames
      null, // NetworkNames With Alias
      null, //alias
      "NETWORK-NAME")]
    [DataRow(
      null, // INetworkServices
      null, // INetworkServices With Alias
      null, // NetworkNames
      new string[] { "NETWORK-NAME", "ALIAS" }, // NetworkNames With Alias
      "ALIAS", //alias
      "NETWORK-NAME")]

    #region With INetworkService
    [DataRow(
      new string[] { "NETWORK-SERVICE" }, // INetworkServices
      new string[] { "NETWORK-SERVICE", "ALIAS" }, // INetworkServices With Alias
      new string[] { "NETWORK-NAME" }, // NetworkNames
      new string[] { "NETWORK-NAME", "ALIAS" }, // NetworkNames With Alias
      null, //alias
      "NETWORK-SERVICE")]
    [DataRow(
      new string[] { "NETWORK-SERVICE" }, // INetworkServices
      new string[] { "NETWORK-SERVICE", "ALIAS" }, // INetworkServices With Alias
      new string[] { "NETWORK-NAME" }, // NetworkNames
      null, // NetworkNames With Alias
      null, //alias
      "NETWORK-SERVICE")]
    [DataRow(
      new string[] { "NETWORK-SERVICE" }, // INetworkServices
      new string[] { "NETWORK-SERVICE", "ALIAS" }, // INetworkServices With Alias
      null, // NetworkNames
      null, // NetworkNames With Alias
      null, //alias
      "NETWORK-SERVICE")]
    [DataRow(
      new string[] { "NETWORK-SERVICE" }, // INetworkServices
      null, // INetworkServices With Alias
      null, // NetworkNames
      null, // NetworkNames With Alias
      null, //alias
      "NETWORK-SERVICE")]
    [DataRow(
      new string[] { "NETWORK-SERVICE" }, // INetworkServices
      null, // INetworkServices With Alias
      new string[] { "NETWORK-NAME" }, // NetworkNames
      new string[] { "NETWORK-NAME", "ALIAS" }, // NetworkNames With Alias
      null, //alias
      "NETWORK-SERVICE")]
    [DataRow(
      new string[] { "NETWORK-SERVICE" }, // INetworkServices
      null, // INetworkServices With Alias
      null, // NetworkNames
      new string[] { "NETWORK-NAME", "ALIAS" }, // NetworkNames With Alias
      null, //alias
      "NETWORK-SERVICE")]
    [DataRow(
      new string[] { "NETWORK-SERVICE" }, // INetworkServices
      null, // INetworkServices With Alias
      null, // NetworkNames
      null, // NetworkNames With Alias
      null, //alias
      "NETWORK-SERVICE")]
    [DataRow(
      new string[] { "NETWORK-SERVICE" }, // INetworkServices
      new string[] { "NETWORK-SERVICE", "ALIAS" }, // INetworkServices With Alias
      null, // NetworkNames
      new string[] { "NETWORK-NAME", "ALIAS" }, // NetworkNames With Alias
      null, //alias
      "NETWORK-SERVICE")]
    #endregion

    #region INetworkService With Alias
    [DataRow(
      null, // INetworkServices
      new string[] { "NETWORK-SERVICE", "ALIAS" }, // INetworkServices With Alias
      new string[] { "NETWORK-NAME" }, // NetworkNames
      new string[] { "NETWORK-NAME", "ALIAS" }, // NetworkNames With Alias
      "ALIAS", //alias
      "NETWORK-SERVICE")]
    [DataRow(
      null, // INetworkServices
      new string[] { "NETWORK-SERVICE", "ALIAS" }, // INetworkServices With Alias
      null, // NetworkNames
      new string[] { "NETWORK-NAME", "ALIAS" }, // NetworkNames With Alias
      "ALIAS", //alias
      "NETWORK-SERVICE")]
    [DataRow(
      null, // INetworkServices
      new string[] { "NETWORK-SERVICE", "ALIAS" }, // INetworkServices With Alias
      new string[] { "NETWORK-NAME" }, // NetworkNames
      null, // NetworkNames With Alias
      "ALIAS", //alias
      "NETWORK-SERVICE")]
    #endregion

    #region NetworkNames
    [DataRow(
      null, // INetworkServices
      null, // INetworkServices With Alias
      new string[] { "NETWORK-NAME" }, // NetworkNames
      new string[] { "NETWORK-NAME", "ALIAS" }, // NetworkNames With Alias
      null, //alias
      "NETWORK-NAME")]
    #endregion
    [DataTestMethod]
    public void FindFirstNetworkNameAndAlias(
      string[] networks,
      string[] networksWithAliases,
      string[] networkNames,
      string[] networkNamesWithAliases,
      string expectedAlias,
      string expectedNetworkName)
    {
      var config = new ContainerBuilderConfig()
      {
        Networks = AsINetworkServiceList(networks),
        NetworksWithAlias = AsINetworkServiceWithAliasList(networksWithAliases),
        NetworkNames = networkNames?.ToList(),
        NetworkNamesWithAlias = AsNetworkNameWithAliasList(networkNamesWithAliases)
      };

      var firstNetwork = config.FindFirstNetworkNameAndAlias();

      Assert.AreEqual(expectedAlias, firstNetwork.Alias);
      Assert.AreEqual(expectedNetworkName, firstNetwork.Network);
    }

    private List<NetworkWithAlias<string>> AsNetworkNameWithAliasList(string[] networkNamesWithAliases)
    {
      if (networkNamesWithAliases == null)
      {
        return null;
      }

      var rv = new List<NetworkWithAlias<string>>();

      for(var i = 0; i < networkNamesWithAliases.Length; i += 2)
      {
        rv.Add(
            new NetworkWithAlias<string>
            {
              Alias = networkNamesWithAliases[i+1],
              Network = networkNamesWithAliases[i]
            }
          );
      }

      return rv;
    }

    private List<NetworkWithAlias<INetworkService>> AsINetworkServiceWithAliasList(string[] networksWithAliases)
    {
      if (networksWithAliases == null)
      {
        return null;
      }

      var rv = new List<NetworkWithAlias<INetworkService>>();

      for (var i = 0; i < networksWithAliases.Length; i += 2)
      {
        rv.Add(
            new NetworkWithAlias<INetworkService>
            {
              Alias = networksWithAliases[i + 1],
              Network = AsINetworkService(networksWithAliases[i])
            }
          );
      }

      return rv;
    }

    private List<INetworkService> AsINetworkServiceList(string[] networks)
    {
      if(networks == null)
      {
        return null;
      }

      return networks.Select(AsINetworkService)
        .ToList();
    }

    private INetworkService AsINetworkService(string network)
    {
      return Mock.Of<INetworkService>(m => m.Name == network);
    }
  }
}
