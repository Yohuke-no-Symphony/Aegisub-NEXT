using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Validates and synchronizes the manually supplied Koumori runtime package.</summary>
public sealed class AegiNextKoumoriResources : Task
{
    [Required]
    public string ModelDirectory { get; set; } = "";
    [Required]
    public string DestinationDirectory { get; set; } = "";
    public string RuntimeIdentifier { get; set; } = "";
    public bool RemoveOnly { get; set; }
    [Output]
    public ITaskItem[] Files { get; set; } = Array.Empty<ITaskItem>();

    /// <inheritdoc />
    public override bool Execute()
    {
        try
        {
            var root = Path.GetFullPath(ModelDirectory);
            var target = Path.GetFullPath(DestinationDirectory);
            AssertSeparateDirectories(root, target);
            AssertRegularPath(target);
            if (RemoveOnly)
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, true);
                }
                return true;
            }
            Files = ReadPackage(root);
            Synchronize(target);
            return true;
        }
        catch (Exception error)
        {
            Log.LogError("Koumori package: {0}", error.Message);
            return false;
        }
    }

    private ITaskItem[] ReadPackage(string root)
    {
        AssertRegularPath(root);
        if (!Directory.Exists(root))
        {
            if (File.Exists(root))
            {
                throw new InvalidDataException("The Koumori model source must be a directory.");
            }
            return Array.Empty<ITaskItem>();
        }
        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            if (Directory.EnumerateFileSystemEntries(root).Any(path => Path.GetFileName(path) != "README.md" && Path.GetFileName(path) != ".gitkeep"))
            {
                throw new InvalidDataException("Missing Koumori model manifest: " + manifestPath);
            }
            return Array.Empty<ITaskItem>();
        }
        if (RuntimeIdentifier.Length != 0 && RuntimeIdentifier != "win-x64" && RuntimeIdentifier != "osx-arm64")
        {
            throw new InvalidDataException("Koumori is not supported on " + RuntimeIdentifier + ".");
        }
        AssertRegularPath(manifestPath);
        var bytes = File.ReadAllBytes(manifestPath);
        var manifest = JObject.Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'),
            new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
        AssertUniqueProperties(manifest);
        var schema = manifest["schemaVersion"];
        if (schema?.Type != JTokenType.Integer || (int)schema < 1 || (int)schema > 3 ||
            string.IsNullOrWhiteSpace((string)manifest["modelId"]) ||
            (string)manifest["inputName"] != "features" || (string)manifest["outputName"] != "probabilities" ||
            (int?)manifest["inputFrames"] != 2048 || (int?)manifest["outputFrames"] != 1024 || (int?)manifest["contextFrames"] != 512 ||
            (int?)manifest["slots"] < 1 || (int?)manifest["slots"] > 64 || manifest["slots"] == null ||
            !(manifest["channels"] is JArray channels) || !channels.Values<string>().SequenceEqual(new[] { "coverage", "start", "end" }))
        {
            throw new InvalidDataException("Unsupported Koumori model contract.");
        }
        var version = (int)schema;
        var frontendIntegers = new Dictionary<string, int> { ["featureCount"] = 82, ["sampleRate"] = 16000 };
        foreach (var field in frontendIntegers)
        {
            var value = manifest[field.Key];
            if (value == null || value.Type == JTokenType.Null)
            {
                if (version >= 2)
                {
                    throw new InvalidDataException("Missing Koumori frontend integer: " + field.Key);
                }
                continue;
            }
            if (value.Type != JTokenType.Integer || value.Value<long>() != field.Value)
            {
                throw new InvalidDataException("Invalid Koumori frontend integer: " + field.Key);
            }
        }
        if (version != 3 && (manifest.Properties().Any(property => property.Name.Equals("snapModelFile", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("snapModelSha256", StringComparison.OrdinalIgnoreCase)) || File.Exists(Path.Combine(root, "snap-model.onnx")) ||
                (string)manifest["architecture"] == "whisper-small-generation-snap-v1"))
        {
            throw new InvalidDataException("Koumori snap model fields and payload require schemaVersion 3.");
        }
        if (version >= 2 && ((string)manifest["architecture"] != (version == 3 ? "whisper-small-generation-snap-v1" : "whisper-small-local-fusion-v1") ||
            (string)manifest["encoderInputName"] != "mel" || (string)manifest["encoderOutputName"] != "hidden" || (string)manifest["contextInputName"] != "context"))
        {
            throw new InvalidDataException("Unsupported Koumori contextual model contract.");
        }
        var files = new List<ITaskItem> { CreateItem(manifestPath, Hash(bytes)) };
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json" };
        var kinds = new Dictionary<string, string>
        {
            ["model"] = "model.onnx", ["frontend"] = "frontend.json", ["decoder"] = "decoder.json"
        };
        if (version >= 2)
        {
            kinds.Add("encoder", "encoder.onnx");
            kinds.Add("encoderFrontend", "encoder-frontend.json");
            kinds.Add("encoderLicense", "WHISPER-LICENSE.txt");
        }
        if (version == 3)
        {
            kinds.Add("snapModel", "snap-model.onnx");
        }
        foreach (var kind in kinds)
        {
            var name = (string)manifest[kind.Key + "File"];
            var digest = (string)manifest[kind.Key + "Sha256"];
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 ||
                name == "." || name == ".." || digest == null || !Regex.IsMatch(digest, "\\A[0-9a-fA-F]{64}\\z") ||
                version == 3 && name != kind.Value || !names.Add(name))
            {
                throw new InvalidDataException("Invalid Koumori " + kind.Key + " file or digest.");
            }
            var path = Path.Combine(root, name);
            AssertRegularPath(path);
            if (!File.Exists(path))
            {
                throw new InvalidDataException("Missing Koumori package file: " + name);
            }
            if (!HashFile(path).Equals(digest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Koumori hash mismatch: " + name);
            }
            files.Add(CreateItem(path, digest));
        }
        return files.ToArray();
    }

    private void Synchronize(string target)
    {
        if (Files.Length == 0)
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
            return;
        }
        Directory.CreateDirectory(target);
        var names = new HashSet<string>(Files.Select(file => Path.GetFileName(file.ItemSpec)), StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFileSystemEntries(target))
        {
            AssertRegularPath(path);
            if (!names.Contains(Path.GetFileName(path)) || Directory.Exists(path))
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else
                {
                    File.Delete(path);
                }
            }
        }
        foreach (var file in Files)
        {
            var path = Path.Combine(target, Path.GetFileName(file.ItemSpec));
            var digest = file.GetMetadata("Sha256");
            if (!File.Exists(path) || !HashFile(path).Equals(digest, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file.ItemSpec, path, true);
                if (!HashFile(path).Equals(digest, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Koumori package changed while copying: " + file.ItemSpec);
                }
            }
        }
    }

    private static ITaskItem CreateItem(string path, string digest)
    {
        var item = new TaskItem(path);
        item.SetMetadata("RelativePath", "models/koumori/" + Path.GetFileName(path));
        item.SetMetadata("CopyToPublishDirectory", "PreserveNewest");
        item.SetMetadata("Sha256", digest);
        return item;
    }

    private static void AssertUniqueProperties(JToken token)
    {
        if (token.Type == JTokenType.Comment)
        {
            throw new InvalidDataException("Comments are not allowed in a Koumori manifest.");
        }
        if (token is JObject value)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.Properties())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Duplicate or aliased Koumori manifest property: " + property.Name);
                }
                AssertUniqueProperties(property.Value);
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array)
            {
                AssertUniqueProperties(child);
            }
        }
    }

    private static void AssertRegularPath(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Linked Koumori package path: " + current);
            }
        }
    }

    private static void AssertSeparateDirectories(string source, string target)
    {
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var first = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var second = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (first.StartsWith(second, comparison) || second.StartsWith(first, comparison))
        {
            throw new InvalidDataException("Koumori source and output directories must not overlap.");
        }
    }

    private static string HashFile(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var algorithm = SHA256.Create())
        {
            return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }

    private static string Hash(byte[] bytes)
    {
        using (var algorithm = SHA256.Create())
        {
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
