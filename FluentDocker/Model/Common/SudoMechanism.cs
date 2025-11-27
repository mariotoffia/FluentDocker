using Ductus.FluentDocker.Common;

namespace Ductus.FluentDocker.Model.Common
{
  /// <summary>
  /// Sets the sudo mechanism the library shall use to talk to the docker daemon.
  /// </summary>
  [Experimental]
  public enum SudoMechanism
  {
    /// <summary>
    /// No sudo needed to talk to the docker daemon.
    /// </summary>
    None = 0,
    /// <summary>
    /// Sudo is needed but no password needs to be provided. The user do have NOPASSWD in /etc/sudoer.
    /// </summary>
    NoPassword = 1,
    /// <summary>
    /// Sudo is needed and a password must be provided that will be piped in the stdin to sudo using sudo -S.
    /// </summary>
    Password = 2
  }
}
