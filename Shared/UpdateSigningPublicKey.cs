namespace Shared.UpdateSecurity
{
    /// <summary>
    ///     RSA public key for manifest.sig verification.
    ///     Regenerate via scripts/ensure-update-signing-key.ps1
    /// </summary>
    internal static class UpdateSigningPublicKey
    {
        internal const string Pem = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAsCFoSTVVpnGWgf+YICPmC1J8CLZEiGkx
ZgjA9ONImpxHN+yfTx5OOO859pWd2ZD+bkTz7CYA8Cy3TEdRYc7PhphWDgeG1ZvkAzYrSxP/sGOm
xU0wxVLsgXghpi8EpXwrHutNfspi3YOPLQa+9Wi4FxHLlt/8Ohv3RyA6ZwK08ZUbd46MejaMRaQG
8EubCJRNINfEjT/zhsK14rYQmfeQikiAJ3OwKhPtyq5wZXpZEPdmhzdUPJ+IyhyzTZvJEBnOfbTQ
S8MWRSHKZcvFlTdFQsJ94MrirhYnrE4txux24jf5Y9LYQq+XZA2Z49Ho9DkHjRPEkXFO1l3dbTeN
qx+B7QIDAQAB
-----END PUBLIC KEY-----";
    }
}