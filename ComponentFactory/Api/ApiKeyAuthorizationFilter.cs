using System.Security.Cryptography;
using System.Text;
using ComponentFactory.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ComponentFactory.Api;

public sealed class ApiKeyAuthorizationFilter(IOptions<FactoryOptions> options) : IAuthorizationFilter
{
    private const string HeaderName = "X-Api-Key";
    private readonly byte[] _expectedHash = Hash(options.Value.ApiKey);

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var suppliedKey = context.HttpContext.Request.Headers[HeaderName].ToString();
        var suppliedHash = Hash(suppliedKey);

        if (!CryptographicOperations.FixedTimeEquals(suppliedHash, _expectedHash))
        {
            context.Result = new UnauthorizedResult();
        }
    }

    private static byte[] Hash(string value)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(value));
    }
}
