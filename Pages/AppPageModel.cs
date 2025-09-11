using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using FantasyToolbox.Models; // Ensure this matches your actual models namespace


public class AppPageModel : PageModel
{
    //private readonly IEspnSessionService _espnSessionService;
    private readonly ILogService _logger;
    private readonly IESPNService _espnService;
    public AppPageModel(ILogService logger, IESPNService espnService)
    {
        //_espnSessionService = espnSessionService;
        _logger = logger;
        _espnService = espnService;
    }

    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        UpdateEspnConnectedSessionAsync(context.HttpContext).GetAwaiter().GetResult();
        base.OnPageHandlerExecuting(context);
    }

    protected async Task UpdateEspnConnectedSessionAsync(HttpContext _context)
    {
        if (_espnService == null)
        {
            await _logger.LogAsync("IESPNService is not injected (null) in AppPageModel.", "Error");
            throw new InvalidOperationException("IESPNService is not available. Check DI configuration.");
        }
        await _espnService.UpdateEspnConnectedSessionAsync(_context);
    }

    
}