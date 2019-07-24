// ReSharper disable InconsistentNaming
// ReSharper disable CommentTypo
namespace Ductus.FluentDocker.Model.Containers
{
  /// <summary>
  ///   ULimit
  /// </summary>
  /// <remarks>
  ///   All items support the values -1, unlimited or infinity indicating no limit, except for priority and nice.
  ///   If a hard limit or soft limit of a resource is set to a valid value, but outside of the supported range of the local
  ///   system, the system may reject the new limit or unexpected behavior may occur. If the control value required is used,
  ///   the module will reject the login if a limit could not be set.
  ///   In general, individual limits have priority over group limits, so if you impose no limits for admin group, but one of
  ///   the members in this group have a limits line, the user will have its limits set according to this line.
  ///   Also, please note that all limit settings are set per login. They are not global, nor are they permanent; existing
  ///   only for the duration of the session.
  /// </remarks>
  public enum Ulimit
  {
    /// <summary>
    /// No or unknown ulimit.
    /// </summary>
    Unknown,

    /// <summary>
    ///   limits the core file size (KB)
    /// </summary>
    Core,

    /// <summary>
    ///   maximum data size (KB)
    /// </summary>
    Data,

    /// <summary>
    ///   maximum filesize (KB)
    /// </summary>
    FSize,

    /// <summary>
    ///   maximum locked-in-memory address space (KB)
    /// </summary>
    MemLock,

    /// <summary>
    ///   maximum number of open files
    /// </summary>
    NoFile,

    /// <summary>
    ///   maximum resident set size (KB) (Ignored in Linux 2.4.30 and higher)
    /// </summary>
    RSS,

    /// <summary>
    ///   maximum stack size (KB)
    /// </summary>
    Stack,

    /// <summary>
    ///   maximum CPU time (minutes)
    /// </summary>
    Cpu,

    /// <summary>
    ///   maximum number of processes
    /// </summary>
    NProc,

    /// <summary>
    ///   address space limit (KB)
    /// </summary>
    /// <remarks>
    ///   NOT SUPPORTED ON Docker (See docker manual for more details).
    /// </remarks>
    As,

    /// <summary>
    ///   maximum number of logins for this user except for this with uid=0
    /// </summary>
    MaxLogins,

    /// <summary>
    ///   maximum number of all logins on system
    /// </summary>
    MaxSysLogins,

    /// <summary>
    ///   the priority to run user process with (negative values boost process priority
    /// </summary>
    Priority,

    /// <summary>
    ///   maximum locked files (Linux 2.4 and higher)
    /// </summary>
    Locks,

    /// <summary>
    ///   maximum number of pending signals (Linux 2.6 and higher)
    /// </summary>
    SigPending,

    /// <summary>
    ///   maximum memory used by POSIX message queues (bytes) (Linux 2.6 and higher)
    /// </summary>
    MsgQueue,

    /// <summary>
    ///   maximum nice priority allowed to raise to (Linux 2.6.12 and higher) values: [-20,19]
    /// </summary>
    Nice,

    /// <summary>
    ///   maximum realtime priority allowed for non-privileged processes (Linux 2.6.12 and higher)
    /// </summary>
    RTPrio
  }
}