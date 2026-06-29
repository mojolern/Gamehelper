namespace Shared.UpdateSecurity
{
    /// <summary>
    ///     RSA public key for manifest.sig verification.
    ///     Regenerate via scripts/ensure-update-signing-key.ps1
    /// </summary>
    internal static class UpdateSigningPublicKey
    {
        internal const string Pem = @"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA1xkGp7b3684mfWOdd00eVxERwKWlzT/3
GV64Emf31kgI+3QdCg1rgesLGMRC0H+9YxfwV4qH2R7Y/ONoZe02qQHlYxDrmHebNTMJYnQjAQos
bcB64S8+Ae0+s2N+/pRXEWq/y8P/uoN7M6bRRWBZlmXHsKeVNnaWt+P1T6BkoZuzw0CWMYwa6rqp
f6taNTXnN/CvOESjlPUlIX609+9hpGH93Rerla2scNJ927X1IoH5yrLlEYY3o4NCy6wO/5B/zLJq
4+9I7Ork9iOIHqW/vBGwzakbGWgTbY+p10YExjdMKrhvJvDmaiXmC3QABRIibPnZpTi3HCpCWXkm
8lCUBQIDAQAB
-----END PUBLIC KEY-----";
    }
}