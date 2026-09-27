using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// Development-only JWT minting, so the services can be called before an identity service exists.
// Claims mirror what an identity service must issue: NameIdentifier (user id), Name, Email, one Role claim per role.
//
//   dotnet run --project tools/Starter.DevToken -- --user 1 --roles CUSTOMER
//   dotnet run --project tools/Starter.DevToken -- --user 2 --roles ORDERMANAGER --minutes 120
//
// Defaults match JwtOptions in the services' appsettings.Development.json.

var options = ParseArgs(args);
var userId  = options.GetValueOrDefault("user", "1");
var roles   = options.GetValueOrDefault("roles", "CUSTOMER").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var name    = options.GetValueOrDefault("name", "Dev User");
var email   = options.GetValueOrDefault("email", $"user{userId}@starter.local");
var issuer  = options.GetValueOrDefault("issuer", "Starter");
var secret  = options.GetValueOrDefault("secret", "local-development-signing-key-change-me-0123456789");
var minutes = int.Parse(options.GetValueOrDefault("minutes", "60"));

var claims = new List<Claim>
{
    new(JwtRegisteredClaimNames.Sub, userId),
    new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
    new(ClaimTypes.NameIdentifier, userId),
    new(ClaimTypes.Name, name),
    new(ClaimTypes.Email, email),
};
claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256);
var token = new JwtSecurityToken(issuer, issuer, claims, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(minutes), credentials);

Console.WriteLine(new JwtSecurityTokenHandler().WriteToken(token));

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i].StartsWith("--"))
            result[args[i][2..]] = args[++i];
    return result;
}
