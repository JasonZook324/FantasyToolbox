using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using FantasyToolbox.Models; // Ensure this matches your actual models namespace


public class AppPageModel : PageModel
{
    private readonly IEspnSessionService _espnSessionService;
    private readonly ILogService _logger;

    public AppPageModel(IEspnSessionService espnSessionService, ILogService logger)
    {
        _espnSessionService = espnSessionService;
        _logger = logger;
    }

    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        UpdateEspnConnectedSessionAsync().GetAwaiter().GetResult();
        base.OnPageHandlerExecuting(context);
    }

    protected async Task UpdateEspnConnectedSessionAsync()
    {
        await _espnSessionService.UpdateEspnConnectedSessionAsync(HttpContext);
    }

    public static explicit operator AppPageModel(ConnectESPNModel v)
    {
        throw new NotImplementedException();
    }
}