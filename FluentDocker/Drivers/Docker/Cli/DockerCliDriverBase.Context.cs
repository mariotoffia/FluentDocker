using System;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Docker.Cli
{
  public abstract partial class DockerCliDriverBase
  {
    protected DriverContext CreateEffectiveContext(DriverContext operationContext)
    {
      var component = Context;
      if (operationContext == null)
        return component;

      return new DriverContext(operationContext.DriverId ?? component?.DriverId)
      {
        LoggerFactory = operationContext.LoggerFactory ?? component?.LoggerFactory ?? NullLoggerFactory.Instance,
        Host = string.IsNullOrEmpty(operationContext.Host) ? component?.Host : operationContext.Host,
        CertificatePath = string.IsNullOrEmpty(operationContext.CertificatePath) ? component?.CertificatePath : operationContext.CertificatePath,
        VerifyTls = !string.IsNullOrEmpty(operationContext.Host) || !string.IsNullOrEmpty(operationContext.CertificatePath)
            ? operationContext.VerifyTls
            : component?.VerifyTls ?? operationContext.VerifyTls,
        OperationId = operationContext.OperationId ?? component?.OperationId,
        Metadata = operationContext.Metadata ?? component?.Metadata,
        Sudo = operationContext.Sudo != SudoMechanism.None ? operationContext.Sudo : component?.Sudo ?? SudoMechanism.None,
        SudoPassword = operationContext.SudoPassword ?? component?.SudoPassword,
        DefaultShell = operationContext.DefaultShell ?? component?.DefaultShell,
        BinaryName = operationContext.BinaryName ?? component?.BinaryName,
        SearchPaths = operationContext.SearchPaths ?? component?.SearchPaths,
        AutoStartMachine = operationContext.AutoStartMachine ?? component?.AutoStartMachine,
        ModelRunnerEndpoint = operationContext.ModelRunnerEndpoint ?? component?.ModelRunnerEndpoint,
        ConnectionTimeout = operationContext.ConnectionTimeout ?? component?.ConnectionTimeout,
        RequestTimeout = operationContext.RequestTimeout ?? component?.RequestTimeout,
        ApiVersion = operationContext.ApiVersion ?? component?.ApiVersion
      };
    }
  }
}
