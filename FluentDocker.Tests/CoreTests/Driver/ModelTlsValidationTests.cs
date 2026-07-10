using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentDocker.Drivers.Models.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Security tests for <see cref="ModelTlsValidation.ValidateWithCustomRoot"/>: a
  /// configured custom CA must NOT relax hostname verification — a name mismatch or
  /// a missing certificate is always rejected, and only chain-trust errors are
  /// eligible for custom-root re-validation.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelTlsValidationTests
  {
    [Fact]
    public void NoErrors_WithNoCustomCaContext_Rejects()
    {
      Assert.False(ModelTlsValidation.ValidateWithCustomRoot((X509Certificate2)null!, null!, null!, SslPolicyErrors.None));
    }

    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)]
    [InlineData(SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateChainErrors)]
    public void NameMismatchOrMissing_AlwaysRejected_EvenWithChainError(SslPolicyErrors errors)
    {
      // A custom CA must never paper over a hostname mismatch / missing cert.
      Assert.False(ModelTlsValidation.ValidateWithCustomRoot((X509Certificate2)null!, null!, null!, errors));
    }

    [Fact]
    public void ChainError_WithNoCa_Rejected()
    {
      // Only chain errors are eligible for custom-root re-validation; without a CA
      // (or chain/cert), it must not silently pass.
      Assert.False(ModelTlsValidation.ValidateWithCustomRoot((X509Certificate2)null!, null!, null!, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void NameMismatch_RejectedEvenWhenChainWouldBuildAgainstCustomRoot()
    {
      // Use REAL non-null cert/chain/CA so the null-guard cannot short-circuit, and make
      // the cert its own valid CA root so a chain rebuild WOULD otherwise succeed. This
      // proves it is the hostname-mismatch rejection (not a missing CA / null path) that
      // blocks acceptance — i.e. a custom CA cannot paper over a wrong-host certificate.
      using var rsa = RSA.Create(2048);
      var request = new CertificateRequest("CN=wrong-host.example", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
      using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
      using var chain = new X509Chain();

      var accepted = ModelTlsValidation.ValidateWithCustomRoot(
          cert, cert, chain,
          SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors);

      Assert.False(accepted);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void NameMismatch_IsRejected_ByDefault_ButAccepted_WhenAllowed()
    {
      // Build a self-signed CA cert that also acts as its own server cert (CN=wrong-host).
      using var rsa = RSA.Create(2048);
      var request = new CertificateRequest("CN=wrong-host.example", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
      using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
      using var chain = new X509Chain();
      var errs = SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors;

      Assert.False(ModelTlsValidation.ValidateWithCustomRoot(cert, cert, chain, errs)); // strict default
      Assert.True(ModelTlsValidation.ValidateWithCustomRoot(cert, cert, chain, errs, allowHostnameMismatch: true));
    }

    [Fact]
    public void CustomRootBundle_TrustsCertMatchingAnyCaInBundle_NotJustFirst()
    {
      // DAPI-MAJ-3: every cert in a ca.pem bundle must be added to the trust store. Put an unrelated
      // CA first and the matching (self-signed) CA second; chain validation must still succeed.
      using var unrelatedRsa = RSA.Create(2048);
      var unrelatedReq = new CertificateRequest("CN=unrelated-ca", unrelatedRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      unrelatedReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
      using var unrelatedCa = unrelatedReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

      using var rsa = RSA.Create(2048);
      var req = new CertificateRequest("CN=server.example", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      req.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
      using var serverCa = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

      var bundle = new X509Certificate2Collection { unrelatedCa, serverCa };
      using var chain = new X509Chain();

      var accepted = ModelTlsValidation.ValidateWithCustomRoot(
          bundle, serverCa, chain, SslPolicyErrors.RemoteCertificateChainErrors);

      Assert.True(accepted);
    }
  }
}
