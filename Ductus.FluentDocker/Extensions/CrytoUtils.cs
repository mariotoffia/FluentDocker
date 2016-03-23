using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using X509Certificate = Org.BouncyCastle.X509.X509Certificate;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

namespace Ductus.FluentDocker.Extensions
{
  internal static class CrytoUtils
  {
    internal static X509Certificate2 ToCertificate(this string dockerCertPath, string certificate)
    {
      return ToCertificate(dockerCertPath, certificate, null);
    }

    internal static X509Certificate2 ToCertificate(this string dockerCertPath, string certificate, string key)
    {
      var cert = new X509Certificate2();
      using (var reader = File.OpenText(Path.Combine(dockerCertPath, certificate)))
      {
        var obj = (X509Certificate) new PemReader(reader).ReadObject();
        cert.Import(obj.GetEncoded());
      }

      if (string.IsNullOrEmpty(key))
      {
        return cert;
      }

      using (var reader = File.OpenText(Path.Combine(dockerCertPath, key)))
      {
        var obj = (AsymmetricCipherKeyPair) new PemReader(reader).ReadObject();
        var rsa = (RSACryptoServiceProvider) ToRsa((RsaPrivateCrtKeyParameters) obj.Private);
        cert.PrivateKey = rsa;
      }

      return cert;
    }

    private static RSA ToRsa(RsaPrivateCrtKeyParameters privKey)
    {
      var cspPrms = new CspParameters {KeyContainerName = "TempContainerName"};
      var rsaCsp = new RSACryptoServiceProvider(cspPrms);
      var rp = ToRsaParameters(privKey);
      rsaCsp.ImportParameters(rp);
      return rsaCsp;
    }

    public static RSAParameters ToRsaParameters(RsaPrivateCrtKeyParameters privKey)
    {
      var rp = new RSAParameters
      {
        Modulus = privKey.Modulus.ToByteArrayUnsigned(),
        Exponent = privKey.PublicExponent.ToByteArrayUnsigned(),
        P = privKey.P.ToByteArrayUnsigned(),
        Q = privKey.Q.ToByteArrayUnsigned()
      };

      rp.D = ConvertRsaParametersField(privKey.Exponent, rp.Modulus.Length);
      rp.DP = ConvertRsaParametersField(privKey.DP, rp.P.Length);
      rp.DQ = ConvertRsaParametersField(privKey.DQ, rp.Q.Length);
      rp.InverseQ = ConvertRsaParametersField(privKey.QInv, rp.Q.Length);
      return rp;
    }

    private static byte[] ConvertRsaParametersField(BigInteger n, int size)
    {
      var bs = n.ToByteArrayUnsigned();
      if (bs.Length == size)
        return bs;
      if (bs.Length > size)
        throw new ArgumentException("Specified size too small", nameof(size));
      var padded = new byte[size];
      Array.Copy(bs, 0, padded, size - bs.Length, bs.Length);
      return padded;
    }
  }
}