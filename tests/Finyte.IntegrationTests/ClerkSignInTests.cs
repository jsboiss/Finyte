using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Finyte.IntegrationTests;

public sealed class ClerkSignInTests
{
    private const string Issuer = "https://clerk.example.test";
    private const string App = "https://app.example.test";
    private static readonly SymmetricSecurityKey Key = new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task TokenIssuedForAnAuthorizedPartyIsAccepted()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClerk(baseFactory, $"{App}/");

        Assert.Equal(HttpStatusCode.OK, await Me(factory, App));
    }

    [Fact]
    public async Task TokenIssuedForAnotherPartyOrNoneIsRejected()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClerk(baseFactory, App);

        Assert.Equal(HttpStatusCode.Unauthorized, await Me(factory, "https://other.example.test"));
        Assert.Equal(HttpStatusCode.Unauthorized, await Me(factory, null));
    }

    [Fact]
    public async Task WithoutAuthorizedPartiesConfiguredTokensAreAcceptedAsBefore()
    {
        await using var baseFactory = new FinyteApiFactory();
        await using var factory = WithClerk(baseFactory, null);

        Assert.Equal(HttpStatusCode.OK, await Me(factory, null));
    }

    private static async Task<HttpStatusCode> Me(WebApplicationFactory<Program> factory, string? party)
    {
        using var client = factory.CreateClient();
        var claims = new List<Claim> { new("sub", "user_test") };
        if (party is not null)
        {
            claims.Add(new Claim("azp", party));
        }
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Subject = new ClaimsIdentity(claims), Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.HmacSha256)
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync("/api/auth/me")).StatusCode;
    }

    private static WebApplicationFactory<Program> WithClerk(FinyteApiFactory factory, string? party) => factory.WithWebHostBuilder(x =>
    {
        x.UseSetting("DevAuth:Enabled", "false");
        x.UseSetting("Clerk:Authority", Issuer);
        if (party is not null)
        {
            x.UseSetting("Clerk:AuthorizedParties:0", party);
        }
        x.ConfigureServices(y => y.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, z =>
        {
            z.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            z.TokenValidationParameters.ValidIssuer = Issuer;
            z.TokenValidationParameters.IssuerSigningKey = Key;
        }));
    });
}
