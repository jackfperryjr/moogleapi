using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace MoogleAPI.Web.Infrastructure.Images;

/// <summary>
/// R2 credentials, read from the same environment variables the scraper uses so one Railway
/// configuration serves both.
/// </summary>
/// <remarks>
/// Every field is optional at startup. The API's whole job is serving data, and refusing to boot
/// because nobody can hand-upload a portrait would trade a working site for a broken one — so an
/// unconfigured bucket disables the upload endpoint and leaves everything else running.
/// </remarks>
public record ImageUploadOptions(
    string AccountId,
    string AccessKey,
    string SecretKey,
    string Bucket,
    string PublicBaseUrl)
{
    public const string DefaultBucket = "moogleapi-images";

    public static ImageUploadOptions? FromConfiguration(IConfiguration config)
    {
        var accountId = config["R2_ACCOUNT_ID"];
        var accessKey = config["ACCESS_KEY"];
        var secretKey = config["SECRET_KEY"];
        var publicBase = config["R2_PUBLIC_BASE_URL"]?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(accountId) ||
            string.IsNullOrWhiteSpace(accessKey) ||
            string.IsNullOrWhiteSpace(secretKey) ||
            string.IsNullOrWhiteSpace(publicBase))
            return null;

        return new ImageUploadOptions(
            accountId, accessKey, secretKey,
            config["R2_BUCKET"] ?? DefaultBucket,
            publicBase);
    }
}

/// <summary>
/// Stores one hand-picked image in the art bucket, re-encoded on the way in.
/// </summary>
/// <remarks>
/// <para>
/// Narrower than the scraper's <c>ImageStore</c> and deliberately separate from it. The scraper
/// references this project, not the other way round, so sharing the type would mean moving the
/// bulk-copy pipeline — MediaWiki thumbnail rewriting, quota accounting, concurrency — into the
/// API to gain one PutObject call. What is duplicated is the encode settings, and those have to
/// match: art uploaded here sits in the same bucket, at the same keys, as art the scraper wrote.
/// </para>
/// </remarks>
public class ImageUploadStore
{
    /// <summary>Longest edge, matching the scraper so hand-uploads are not the odd ones out.</summary>
    private const int MaxEdge = 800;

    private const int WebpQuality = 82;

    private readonly ImageUploadOptions? _options;
    private readonly AmazonS3Client? _s3;
    private readonly ILogger<ImageUploadStore> _logger;

    public ImageUploadStore(ImageUploadOptions? options, ILogger<ImageUploadStore> logger)
    {
        _options = options;
        _logger = logger;

        if (options is null) return;

        _s3 = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = $"https://{options.AccountId}.r2.cloudflarestorage.com",
                // R2 exposes one global endpoint and ignores regions, but the SDK insists on one.
                AuthenticationRegion = "auto",
                ForcePathStyle = true,
            });
    }

    public bool IsConfigured => _options is not null;

    public string PublicUrlFor(string key) => $"{_options!.PublicBaseUrl}/{key}";

    /// <summary>
    /// Re-encodes the uploaded bytes to WebP and writes them to <paramref name="key"/>, returning
    /// the public URL. Throws <see cref="InvalidImageException"/> when the bytes are not a picture.
    /// </summary>
    public async Task<string> UploadAsync(string key, Stream content, CancellationToken ct)
    {
        if (_s3 is null) throw new InvalidOperationException("The image bucket is not configured.");

        using var image = await LoadAsync(content, ct);

        if (image.Width > MaxEdge || image.Height > MaxEdge)
            image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(MaxEdge, MaxEdge), Mode = ResizeMode.Max }));

        using var encoded = new MemoryStream();
        await image.SaveAsync(encoded, new WebpEncoder { Quality = WebpQuality }, ct);
        encoded.Position = 0;

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options!.Bucket,
            Key = key,
            InputStream = encoded,
            ContentType = "image/webp",
            DisablePayloadSigning = true,
        }, ct);

        _logger.LogInformation("Uploaded {Key} ({Width}x{Height}).", key, image.Width, image.Height);
        return PublicUrlFor(key);
    }

    private static async Task<Image> LoadAsync(Stream content, CancellationToken ct)
    {
        try
        {
            return await Image.LoadAsync(content, ct);
        }
        catch (Exception ex) when (ex is ImageFormatException or UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidImageException(ex.Message);
        }
    }
}

/// <summary>The uploaded file was not an image this server can read.</summary>
public class InvalidImageException(string message) : Exception(message);
