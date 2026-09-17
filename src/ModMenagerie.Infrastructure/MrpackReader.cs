using System.IO.Compression;
using System.Text.Json;
using ModMenagerie.Application;

namespace ModMenagerie.Infrastructure;

public static class MrpackReader
{
    public const int MaxIndexBytes = 4 * 1024 * 1024;
    public static ImportManifest Read(string path)
    {
        if (new FileInfo(path).Length > 1024L * 1024 * 1024) throw new InvalidDataException("Archive exceeds the 1 GiB import limit.");
        try
        {
            using var zip = ZipFile.OpenRead(path);
            if (zip.Entries.Count > 20000) throw new InvalidDataException("Archive contains too many entries.");
            var indices = zip.Entries.Where(e => e.FullName == "modrinth.index.json").ToArray();
            if (indices.Length != 1) throw new InvalidDataException("Archive must contain exactly one root modrinth.index.json.");
            var index = indices[0];
            if (index.Length > MaxIndexBytes) throw new InvalidDataException("Pack index exceeds the 4 MiB limit.");
            // Never extract archive content or follow its download URLs.
            using var stream = index.Open();
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = stream.Read(bytes)) > 0)
            {
                if (buffer.Length + read > MaxIndexBytes) throw new InvalidDataException("Expanded pack index exceeds its size limit.");
                buffer.Write(bytes, 0, read);
            }
            using var doc = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            string Text(JsonElement e, string key) => e.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
            if (root.GetProperty("formatVersion").GetInt32() != 1 || Text(root, "game") != "minecraft") throw new InvalidDataException("Only Minecraft mrpack format version 1 is supported.");
            var dependencies = root.GetProperty("dependencies");
            var current = Text(dependencies, "minecraft");
            var loaders = new[] { ("fabric-loader", "fabric"), ("quilt-loader", "quilt"), ("forge", "forge"), ("neoforge", "neoforge") }.Where(l => Text(dependencies, l.Item1).Length > 0).ToArray();
            if (loaders.Length != 1 || string.IsNullOrWhiteSpace(current)) throw new InvalidDataException("Import requires a Minecraft version and exactly one supported loader.");
            var files = root.GetProperty("files");
            if (files.GetArrayLength() > 2000) throw new InvalidDataException("Pack exceeds the 2,000-file preview limit.");
            var result = new List<ImportFile>();
            foreach (var file in files.EnumerateArray())
            {
                var filePath = Text(file, "path");
                var parts = filePath.Replace('\\', '/').Split('/');
                var unsafePath = string.IsNullOrWhiteSpace(filePath) || filePath.StartsWith('/') || filePath.StartsWith('\\') || filePath.Contains(':') || parts.Any(p => p is ".." or ".");
                string? hash = null;
                var algorithm = "sha512";
                if (file.TryGetProperty("hashes", out var h) && h.ValueKind == JsonValueKind.Object)
                {
                    hash = Text(h, "sha512");
                    if (hash.Length == 0) { algorithm = "sha1"; hash = Text(h, "sha1"); }
                }
                var validHash = hash != null && hash.Length == (algorithm == "sha512" ? 128 : 40) && hash.All(Uri.IsHexDigit);
                var env = file.TryGetProperty("env", out var environment) && environment.ValueKind == JsonValueKind.Object ? $"Client: {Text(environment, "client")}; server: {Text(environment, "server")}" : "Client/server unspecified";
                result.Add(new(filePath, validHash ? hash!.ToLowerInvariant() : null, algorithm, env, unsafePath ? "Unsafe or invalid archive path — skipped" : validHash ? null : "Missing or invalid hash — unresolved"));
            }
            var warnings = new List<string>();
            var overrides = zip.Entries.Count(e => e.FullName != "modrinth.index.json" && !e.FullName.EndsWith('/'));
            if (overrides > 0) warnings.Add($"{overrides} archive files outside the index are not identified or imported (including overrides).");
            foreach (var dependency in dependencies.EnumerateObject())
                if (dependency.Name != "minecraft" && dependency.Name != loaders[0].Item1) warnings.Add($"Unsupported pack dependency: {dependency.Name}");
            return new(Text(root, "name"), current, loaders[0].Item2, Text(dependencies, loaders[0].Item1), result.ToArray(), warnings.ToArray());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw new InvalidDataException("The modpack index is malformed or missing required fields.", ex); }
    }
}
