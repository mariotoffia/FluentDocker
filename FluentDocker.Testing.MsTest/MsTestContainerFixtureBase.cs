using System;

namespace FluentDocker.Testing.MsTest
{
  /// <summary>
  /// Deprecated alias for <see cref="MsTestPerTestContainerFixtureBase"/>. The old name was
  /// symmetric with <see cref="MsTestClassContainerFixtureBase{TFixture}"/> yet had a different
  /// (per-test-method) container lifetime, inviting accidental 10-100× container churn when a suite
  /// was copy-ported. Prefer the explicit name.
  /// </summary>
  [Obsolete(
      "Renamed to MsTestPerTestContainerFixtureBase to make the per-test-method container lifetime " +
      "explicit (vs. class-scoped MsTestClassContainerFixtureBase). This alias will be removed in a " +
      "future release.",
      error: false)]
  public abstract class MsTestContainerFixtureBase : MsTestPerTestContainerFixtureBase
  {
  }
}
