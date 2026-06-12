namespace Shared.UpdateSecurity
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    ///     Verifies RSA-SHA256 signatures on update manifests (manifest.sig).
    /// </summary>
    public static class UpdateManifestVerifier
    {
        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(UpdateSigningPublicKey.Pem);

        public static bool TryVerify(string manifestJson, string signatureBase64, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                error = "Manifest is empty.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(signatureBase64))
            {
                error = "Manifest signature is missing.";
                return false;
            }

            if (!IsConfigured)
            {
                error = "Update signing public key is not configured.";
                return false;
            }

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(signatureBase64.Trim());
            }
            catch (FormatException)
            {
                error = "Manifest signature is not valid Base64.";
                return false;
            }

            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(UpdateSigningPublicKey.Pem);
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(manifestJson));
                if (rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                {
                    return true;
                }

                error = "Manifest signature verification failed.";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Manifest signature verification error: {ex.Message}";
                return false;
            }
        }
    }
}
