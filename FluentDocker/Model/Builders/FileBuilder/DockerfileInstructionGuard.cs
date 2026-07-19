#nullable enable
using System;
using FluentDocker.Common;
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  internal static class DockerfileInstructionGuard
  {
    internal static string Require(
        TemplateString? value, string instruction, string field, string emptyMessage)
    {
      if (value == null || string.IsNullOrEmpty(value.Rendered))
        throw new FluentDockerException(emptyMessage);
      return Validate(value.Rendered, instruction, field);
    }

    internal static string? Optional(TemplateString? value, string instruction, string field) =>
        value == null || string.IsNullOrEmpty(value.Rendered)
            ? null
            : Validate(value.Rendered, instruction, field);

    internal static string Require(
        string? value, string instruction, string field, string emptyMessage)
    {
      if (string.IsNullOrEmpty(value))
        throw new FluentDockerException(emptyMessage);
      return Validate(value, instruction, field);
    }

    internal static string Optional(
        string? value, string instruction, string field, string defaultValue) =>
        string.IsNullOrEmpty(value) ? defaultValue : Validate(value, instruction, field);

    internal static string Validate(string value, string instruction, string field)
    {
      foreach (var c in value)
      {
        if (char.IsControl(c))
          throw new FluentDockerException(
              $"Dockerfile {instruction} {field} cannot contain control characters/newlines; " +
              "use multiple Run() calls instead (each Run() creates a separate image layer).");
      }
      return value;
    }

    internal static string ValidateToken(string value, string instruction, string field)
    {
      Validate(value, instruction, field);
      foreach (var c in value)
      {
        if (char.IsWhiteSpace(c))
          throw new FluentDockerException(
              $"Dockerfile {instruction} {field} cannot contain whitespace.");
      }
      return value;
    }
  }
}
