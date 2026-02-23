using Catalogue.Core.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Catalogue.Infrastructure.Services;

public class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? CurrentUser =>
        _httpContextAccessor.HttpContext?.User?.Identity?.Name;
}
