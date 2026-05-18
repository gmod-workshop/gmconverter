using System.Security.Cryptography;
using System.Text.Json;
using CUE4Parse.UE4.Objects.Core.Misc;
using GMConverter.Common;

namespace GMConverter.Formats.Unreal;

internal static partial class UedbClient
{
    private const string _fortniteAesUrl = "https://uedb.dev/svc/api/v1/fortnite/aes";
    private const string _fortniteMappingsUrl = "https://uedb.dev/svc/api/v1/fortnite/mappings";
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient _httpClient = new();

    public static Cue4ParseGameData? TryGetFortniteData()
    {
        try
        {
            var aesData = GetJson<FortniteAesResponse>(_fortniteAesUrl)
                ?? throw new GMConverterException("UEDB did not return Fortnite AES data.");
            if (string.IsNullOrWhiteSpace(aesData.Version) ||
                string.IsNullOrWhiteSpace(aesData.MainKey))
            {
                throw new GMConverterException("UEDB Fortnite AES data is incomplete.");
            }

            Dictionary<FGuid, string> keys = new()
            {
                [new FGuid()] = aesData.MainKey
            };

            foreach (var dynamicKey in aesData.DynamicKeys ?? [])
            {
                if (!string.IsNullOrWhiteSpace(dynamicKey.Guid) &&
                    !string.IsNullOrWhiteSpace(dynamicKey.Key))
                {
                    keys[new FGuid(dynamicKey.Guid)] = dynamicKey.Key;
                }
            }

            var mappingsData = GetJson<FortniteMappingsResponse>(_fortniteMappingsUrl);
            var mappingsPath = mappingsData is not null &&
                string.Equals(mappingsData.Version, aesData.Version, StringComparison.OrdinalIgnoreCase)
                    ? TryDownloadMappings(mappingsData)
                    : null;
            return new Cue4ParseGameData(aesData.Version, mappingsPath, keys);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or GMConverterException)
        {
            return null;
        }
    }

    private static T? GetJson<T>(string url)
    {
        var json = _httpClient.GetStringAsync(url).GetAwaiter().GetResult();
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    private static string? TryDownloadMappings(FortniteMappingsResponse mappingsData)
    {
        var url = SelectMappingsUrl(mappingsData.Mappings);
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var uri = new Uri(url);
        var urlPathHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsolutePath)))[..12];
        var fileName = $"{urlPathHash}_{Path.GetFileName(uri.LocalPath)}";
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GMConverter",
            "UEDB");
        Directory.CreateDirectory(cacheDirectory);

        var mappingsPath = Path.Combine(cacheDirectory, fileName);
        if (File.Exists(mappingsPath))
        {
            return mappingsPath;
        }

        var bytes = _httpClient.GetByteArrayAsync(url).GetAwaiter().GetResult();
        File.WriteAllBytes(mappingsPath, bytes);
        return mappingsPath;
    }

    private static string? SelectMappingsUrl(IReadOnlyDictionary<string, string> mappings)
    {
        if (mappings.TryGetValue("Brotli", out var brotliUrl))
        {
            return brotliUrl;
        }

        if (mappings.TryGetValue("ZStandard", out var zstandardUrl))
        {
            return zstandardUrl;
        }

        return mappings.Values.FirstOrDefault();
    }

    private sealed record FortniteAesResponse(
        string Version,
        string MainKey,
        DateTimeOffset Updated,
        IReadOnlyList<FortniteDynamicKey> DynamicKeys);

    private sealed record FortniteDynamicKey(
        string Guid,
        string Key);

    private sealed record FortniteMappingsResponse(
        string Version,
        DateTimeOffset Updated,
        IReadOnlyDictionary<string, string> Mappings);
}
