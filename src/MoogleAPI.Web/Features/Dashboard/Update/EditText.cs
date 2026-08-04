namespace MoogleAPI.Web.Features.Dashboard.Update;

public static class EditText
{
    /// <summary>
    /// Trims a hand-typed field and turns a blank one back into null.
    /// </summary>
    /// <remarks>
    /// An HTML form has no way to submit "absent" — a cleared input arrives as an empty string,
    /// and storing that would quietly change meaning across the app. Null in these columns is
    /// load-bearing: <c>RequireImage</c> filters on <c>ImageUrl != null</c>, and the scraper's
    /// copy stage treats a non-null <c>ImageSourceUrl</c> as "already copied", so an empty string
    /// there would make it skip a row forever.
    /// </remarks>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
