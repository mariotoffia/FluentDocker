#nullable disable warnings
using System;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Docker.Cli
{
  public abstract partial class DockerCliDriverBase
  {
    /// <summary>
    /// Merges a per-operation context over the component context.
    /// </summary>
    /// <remarks>
    /// Nullable values, including <see cref="DriverContext.VerifyTls"/>, inherit the
    /// component value when the operation does not set them.
    /// </remarks>
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
        VerifyTls = operationContext.VerifyTls ?? component?.VerifyTls,
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
