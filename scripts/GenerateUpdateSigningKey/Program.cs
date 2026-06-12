using System.Security.Cryptography;
using System.Text;

static string ToPem(string label, byte[] bytes)
{
    var builder = new StringBuilder();
    builder.AppendLine($"-----BEGIN {label}-----");
    builder.AppendLine(Convert.ToBase64String(bytes, Base64FormattingOptions.InsertLineBreaks));
    builder.AppendLine($"-----END {label}-----");
    return builder.ToString().TrimEnd();
}

static string FindRepositoryRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "GameOverlay.sln")) ||
            Directory.Exists(Path.Combine(dir.FullName, "Shared")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    throw new DirectoryNotFoundException(
        "Repository root not found (expected GameOverlay.sln or Shared/ folder).");
}

static void EnsureKeys(string root)
{
    var privateKeyPath = Path.Combine(root, "update-signing.key");
    var publicKeyPath = Path.Combine(root, "update-signing.pub");
    var publicKeySource = Path.Combine(root, "Shared", "UpdateSigningPublicKey.cs");

    if (!File.Exists(privateKeyPath))
    {
        using var rsa = RSA.Create(2048);
        File.WriteAllText(privateKeyPath, ToPem("RSA PRIVATE KEY", rsa.ExportRSAPrivateKey()));
        File.WriteAllText(publicKeyPath, ToPem("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo()));
        Console.WriteLine($"Created {privateKeyPath}");
    }
    else
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(privateKeyPath));
        File.WriteAllText(publicKeyPath, ToPem("PUBLIC KEY", rsa.ExportSubjectPublicKeyInfo()));
    }

    var publicPem = File.ReadAllText(publicKeyPath).TrimEnd().Replace("\"", "\"\"");
    var source = $$"""
namespace Shared.UpdateSecurity
{
    /// <summary>
    ///     RSA public key for manifest.sig verification.
    ///     Regenerate via scripts/ensure-update-signing-key.ps1
    /// </summary>
    internal static class UpdateSigningPublicKey
    {
        internal const string Pem = @"{{publicPem}}";
    }
}
""";
    File.WriteAllText(publicKeySource, source);
    Console.WriteLine($"Updated {publicKeySource}");
}

static void SignManifest(string root, string manifestPath)
{
    var privateKeyPath = Path.Combine(root, "update-signing.key");
    if (!File.Exists(privateKeyPath))
    {
        throw new FileNotFoundException("update-signing.key fehlt. Fuehre ensure-update-signing-key.ps1 aus.", privateKeyPath);
    }

    if (!File.Exists(manifestPath))
    {
        throw new FileNotFoundException("Manifest nicht gefunden.", manifestPath);
    }

    var manifestJson = File.ReadAllText(manifestPath);
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(manifestJson));

    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(privateKeyPath));
    var signature = rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    var sigPath = Path.Combine(Path.GetDirectoryName(manifestPath) ?? root, "manifest.sig");
    File.WriteAllText(sigPath, Convert.ToBase64String(signature));
    Console.WriteLine($"manifest.sig erstellt: {sigPath}");
}

var root = FindRepositoryRoot();
var command = args.Length > 0 ? args[0].ToLowerInvariant() : "ensure";

switch (command)
{
    case "sign":
        EnsureKeys(root);
        if (args.Length < 2)
        {
            throw new ArgumentException("Verwendung: sign <pfad-zu-manifest.json>");
        }

        SignManifest(root, Path.GetFullPath(args[1]));
        break;

    case "ensure":
    default:
        EnsureKeys(root);
        break;
}
