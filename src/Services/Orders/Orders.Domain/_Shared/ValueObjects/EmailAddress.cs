using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace Orders.Domain.Shared.ValueObjects;

//insight - a class, not a record: the base type defines value equality and a record would override it
public class EmailAddress : StringValueObject
{
    [JsonConstructor]
    private EmailAddress(string value) => Value = value;

    public static EmailAddress Create(string value)
    {
        Guard.ThrowIfNullOrWhiteSpace(value);
        Guard.ThrowIfFalse(IsValidEmail(value), "Invalid email format.");

        return new EmailAddress(value.Trim().ToLowerInvariant());
    }

    private static bool IsValidEmail(string email)
        => Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
}
