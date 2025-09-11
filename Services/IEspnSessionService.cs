using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

public interface IEspnSessionService
{
    Task UpdateEspnConnectedSessionAsync(HttpContext httpContext);
    void SetEspnBannerIfNeeded(HttpContext httpContext, IDictionary<string, object> viewData);
}