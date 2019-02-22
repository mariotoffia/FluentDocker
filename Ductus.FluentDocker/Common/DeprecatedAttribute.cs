using System;

namespace Ductus.FluentDocker.Common
{
    /// <summary>
    ///     Specifies that the implementation is deprecated and is subject to be removed.
    /// </summary>
    [AttributeUsage(AttributeTargets.All)]
    public class DeprecatedAttribute : Attribute
    {
        public DeprecatedAttribute(string documentation = null, string targetVersion = null)
        {
            Documentation = documentation ?? string.Empty;
            TargetVersion = targetVersion ?? string.Empty;
        }

        /// <summary>
        ///     Current target version when this is to be removed.
        /// </summary>
        public string TargetVersion { get; set; }

        /// <summary>
        ///     Optional documentation for the deprecation.
        /// </summary>
        public string Documentation { get; set; }
    }
}