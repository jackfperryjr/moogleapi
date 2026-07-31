using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace MoogleAPI.Scraper;

/// <summary>
/// Configuration for the image bucket. Everything comes from the environment, because these
/// are credentials and must never sit in a checked-in file.
/// </summary>
/// <param name="PublicBaseUrl">
/// The origin the bucket is served from — an r2.dev subdomain or a custom domain. Absent until
/// public access is turned on, and the copy refuses to rewrite any row without it: pointing
/// the API at a bucket nobody can read would replace working art with broken links.
/// </param>
public record ImageStoreOptions(
    string AccountId,
    string AccessKey,
    string SecretKey,
    string Bucket,
    string? PublicBaseUrl)
{
    public const string DefaultBucket = "moogleapi-images";

    public static ImageStoreOptions? FromEnvironment()
    {
        var accountId = Environment.GetEnvironmentVariable("R2_ACCOUNT_ID");
        var accessKey = Environment.GetEnvironmentVariable("ACCESS_KEY");
        var secretKey = Environment.GetEnvironmentVariable("SECRET_KEY");

        if (string.IsNullOrWhiteSpace(accountId) ||
            string.IsNullOrWhiteSpace(accessKey) ||
            string.IsNullOrWhiteSpace(secretKey))
            return null;

        return new ImageStoreOptions(
            accountId,
            accessKey,
            secretKey,
            Environment.GetEnvironmentVariable("R2_BUCKET") ?? DefaultBucket,
            Environment.GetEnvironmentVariable("R2_PUBLIC_BASE_URL")?.TrimEnd('/'));
    }
}

/// <summary>
/// Copies artwork into Cloudflare R2, re-encoded on the way in.
/// </summary>
/// <remarks>
/// Storing the wiki's originals verbatim would be roughly 4.6 GB across the library and would
/// serve megabyte PNGs to phones. Resizing to a sane bound and re-encoding as WebP lands the
/// whole set near 0.5 GB while still being sharper than the 400px thumbnails half the rows
/// currently point at.
/// </remarks>
public class ImageStore(ImageStoreOptions options, HttpClient http, ILogger<ImageStore> logger)
{
    /// <summary>Longest edge. Enough for a detail page; far below the originals' size.</summary>
    private const int MaxEdge = 800;

    private const int WebpQuality = 82;

    private readonly AmazonS3Client _s3 = new(
        new BasicAWSCredentials(options.AccessKey, options.SecretKey),
        new AmazonS3Config
        {
            ServiceURL = $"https://{options.AccountId}.r2.cloudflarestorage.com",
            // R2 exposes one global endpoint and ignores regions, but the SDK insists on one.
            AuthenticationRegion = "auto",
            ForcePathStyle = true,
        });

    public string Bucket => options.Bucket;
    public string? PublicBaseUrl => options.PublicBaseUrl;

    /// <summary>
    /// Confirms the bucket is writable.
    /// </summary>
    /// <remarks>
    /// Deliberately avoids ListBuckets: an R2 token scoped to one bucket — the sensible way to
    /// issue them — can write objects all day but is denied account-level listing, so checking
    /// that way reports a failure for a setup that actually works. A HEAD against the bucket
    /// itself only needs the permission the copy is about to use.
    /// </remarks>
    public async Task<bool> EnsureBucketAsync(CancellationToken ct)
    {
        try
        {
            await _s3.GetBucketLocationAsync(options.Bucket, ct);
            logger.LogInformation("Bucket {Bucket} is reachable.", options.Bucket);
            return true;
        }
        catch (AmazonS3Exception head)
        {
            try
            {
                await _s3.PutBucketAsync(new PutBucketRequest { BucketName = options.Bucket }, ct);
                logger.LogInformation("Created bucket {Bucket}.", options.Bucket);
                return true;
            }
            catch (AmazonS3Exception create)
            {
                logger.LogError(
                    "Cannot use bucket {Bucket} — reading it said '{Head}', creating it said '{Create}'. " +
                    "Create the bucket in the Cloudflare dashboard, or issue a token with admin " +
                    "read/write, then re-run.",
                    options.Bucket, head.Message, create.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Downloads, re-encodes and uploads one image. Returns the object key, or null when the
    /// source could not be fetched or decoded — a missing image is never fatal to a run.
    /// </summary>
    public async Task<string?> CopyAsync(string sourceUrl, string key, CancellationToken ct)
    {
        try
        {
            // No Referer: the wiki's CDN rejects any request that carries one.
            using var response = await http.GetAsync(OriginalOf(sourceUrl), ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("  ! {Status} fetching {Url}", (int)response.StatusCode, sourceUrl);
                return null;
            }

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            using var image = await Image.LoadAsync(source, ct);

            if (image.Width > MaxEdge || image.Height > MaxEdge)
                image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(MaxEdge, MaxEdge), Mode = ResizeMode.Max }));

            using var encoded = new MemoryStream();
            await image.SaveAsync(encoded, new WebpEncoder { Quality = WebpQuality }, ct);
            encoded.Position = 0;

            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = options.Bucket,
                Key = key,
                InputStream = encoded,
                ContentType = "image/webp",
                DisablePayloadSigning = true,
            }, ct);

            return key;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ImageFormatException or AmazonS3Exception)
        {
            logger.LogWarning("  ! {Type} on {Url}: {Message}", ex.GetType().Name, sourceUrl, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Upgrades a MediaWiki thumbnail URL to the full-size original by dropping the scaling
    /// segment. Half the stored monster art points at a 400px thumbnail, and the original sits
    /// at the same address without it — so better source images cost nothing extra to fetch.
    /// </summary>
    public static string OriginalOf(string url)
    {
        var trimmed = System.Text.RegularExpressions.Regex.Replace(
            url, @"/scale-to-width-down/\d+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return System.Text.RegularExpressions.Regex.Replace(
            trimmed, @"/revision/latest/thumbnail(/width/\d+)?", "/revision/latest",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public string PublicUrlFor(string key) => $"{options.PublicBaseUrl}/{key}";

    /// <summary>
    /// Stores bytes we already hold — generated art, rather than something fetched from a URL.
    /// Returns the public URL, or null when the image cannot be decoded or stored.
    /// </summary>
    public async Task<string?> UploadAsync(string key, byte[] bytes, int maxEdge, CancellationToken ct)
    {
        try
        {
            using var image = Image.Load(bytes);

            if (image.Width > maxEdge || image.Height > maxEdge)
                image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(maxEdge, maxEdge), Mode = ResizeMode.Max }));

            using var encoded = new MemoryStream();
            await image.SaveAsync(encoded, new WebpEncoder { Quality = WebpQuality }, ct);
            encoded.Position = 0;

            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = options.Bucket,
                Key = key,
                InputStream = encoded,
                ContentType = "image/webp",
                DisablePayloadSigning = true,
            }, ct);

            return PublicUrlFor(key);
        }
        catch (Exception ex) when (ex is ImageFormatException or AmazonS3Exception)
        {
            logger.LogWarning("  ! could not store {Key}: {Message}", key, ex.Message);
            return null;
        }
    }
}
