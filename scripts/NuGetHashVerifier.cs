using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using NuGet.Packaging;

// SDK-owned NuGet implementation; no new application or NuGet dependency.
internal static class Program
{
    internal static string Normalize(string value)
    {
        value = value.Trim().TrimStart('\uFEFF').Trim();
        if (value.StartsWith("sha512-", StringComparison.Ordinal)) value = value.Substring(7);
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length != 64) throw new InvalidDataException("Expected SHA512 value.");
        return Convert.ToBase64String(bytes);
    }

    internal static object Verify(string archive, string package, string expectedContent, string expectedArchive)
    {
        using var raw = File.OpenRead(archive);
        var rawHash = Convert.ToBase64String(SHA512.HashData(raw));
        if (rawHash != Normalize(expectedArchive)) throw new InvalidDataException("Archive SHA512 mismatch: " + package);
        raw.Position = 0;
        var archiveSha256 = Convert.ToHexStringLower(SHA256.HashData(raw));
        using var reader = new PackageArchiveReader(archive);
        var identity = reader.GetIdentity();
        if (!string.Equals(identity.Id + "/" + identity.Version.ToNormalizedString(), package, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package identity mismatch: " + package);
        var token = CancellationToken.None;
        var signed = reader.IsSignedAsync(token).GetAwaiter().GetResult();
        if (signed)
        {
            var signature = reader.GetPrimarySignatureAsync(token).GetAwaiter().GetResult();
            reader.ValidateIntegrityAsync(signature.SignatureContent, token).GetAwaiter().GetResult();
        }
        // No cache callback: hash the actual archive with NuGet's signature-aware algorithm.
        var content = reader.GetContentHash(token);
        if (content != Normalize(expectedContent)) throw new InvalidDataException("NuGet content hash mismatch: " + package);
        return new { package, archiveBytes = new FileInfo(archive).Length, archiveSha512 = rawHash, archiveSha256,
            sourceUrl = "https://api.nuget.org/v3-flatcontainer/" + package.ToLowerInvariant() + "/" + package.Replace("/", ".").ToLowerInvariant() + ".nupkg",
            contentHash = content, signed, signedArchiveIntegrity = signed ? "verified" : "unsigned",
            nugetVersion = typeof(PackageArchiveReader).Assembly.GetName().Version.ToString(),
            nugetInformationalVersion = typeof(PackageArchiveReader).Assembly.GetCustomAttributes(false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion };
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args[0] == "--regression") { Regression(args[1], args[2]); return 0; }
            using var input = JsonDocument.Parse(File.ReadAllText(args[0]));
            var output = input.RootElement.EnumerateArray().Select(row => Verify(
                row.GetProperty("archive").GetString(), row.GetProperty("package").GetString(),
                row.GetProperty("contentHash").GetString(), row.GetProperty("archiveSha512").GetString())).ToArray();
            File.WriteAllText(args[1], JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine("PASS NuGet raw/archive and signature-aware content hashes: " + output.Length);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.GetType().Name + ": " + e.Message); return 1; }
    }

    private static void Regression(string inputFile, string temporary)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(inputFile));
        var row = input.RootElement[0];
        var path = row.GetProperty("archive").GetString(); var package = row.GetProperty("package").GetString();
        var content = row.GetProperty("contentHash").GetString(); var archive = row.GetProperty("archiveSha512").GetString();
        Verify(path, package, content, archive);
        Verify(path, package, "\uFEFF sha512-" + content + "\r\n", " " + archive + "\n");
        int rejected = 0;
        void Reject(Action action)
        {
            try { action(); } catch (Exception e) { rejected++; Console.WriteLine("PASS rejected: " + e.GetType().Name + ": " + e.Message); return; }
            throw new InvalidOperationException("Bad package evidence was accepted.");
        }
        var wrong = Convert.ToBase64String(new byte[64]);
        Reject(() => Verify(path, package, wrong, archive));
        Reject(() => Verify(path, package, content, wrong));
        Reject(() => Verify(path, "incorrect.package/0.0.0", content, archive));
        Directory.CreateDirectory(temporary);
        var modified = Path.Combine(temporary, "changed.nupkg"); File.Copy(path, modified, true);
        using (var zip = ZipFile.Open(modified, ZipArchiveMode.Update))
        {
            var original = zip.Entries.First(x => x.Name.EndsWith(".dll"));
            var name = original.FullName; using var source = original.Open();
            using var memory = new MemoryStream(); source.CopyTo(memory); source.Dispose(); original.Delete();
            var bytes = memory.ToArray(); bytes[bytes.Length / 2] ^= 1;
            using var target = zip.CreateEntry(name).Open(); target.Write(bytes);
        }
        Reject(() => Verify(modified, package, content, archive));
        using var raw = File.OpenRead(modified); var changedRaw = Convert.ToBase64String(SHA512.HashData(raw));
        // Even a recomputed raw hash must not turn changed signed content into a pass.
        Reject(() => Verify(modified, package, content, changedRaw));
        Console.WriteLine("PASS NuGet regression: 2 valid/normalized, " + rejected + " rejected (hash/identity/payload/rehashed payload)");
    }
}
