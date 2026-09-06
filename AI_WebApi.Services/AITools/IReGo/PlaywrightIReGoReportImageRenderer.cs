using AI_WebApi.IServices.AITools;
using AI_WebApi.Model.AITools;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace AI_WebApi.Services.AITools.IReGo;

public sealed class PlaywrightIReGoReportImageRenderer :
    IIReGoReportImageRenderer,
    IAsyncDisposable
{
    private readonly IReGoReportImageOptions _options;
    private readonly SemaphoreSlim _renderSlots;
    private readonly SemaphoreSlim _browserLock = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public PlaywrightIReGoReportImageRenderer(IOptions<IReGoToolOptions> options)
    {
        _options = options.Value.ReportImage;
        _renderSlots = new SemaphoreSlim(
            Math.Clamp(_options.MaxConcurrentRenders, 1, 4));
    }

    public async Task<byte[]> RenderPngAsync(
        string html,
        CancellationToken cancellationToken = default)
    {
        await _renderSlots.WaitAsync(cancellationToken);
        try
        {
            var browser = await BrowserAsync();
            await using var context = await browser.NewContextAsync(
                new BrowserNewContextOptions
                {
                    ViewportSize = new ViewportSize
                    {
                        Width = 1200,
                        Height = 900
                    },
                    DeviceScaleFactor = 1
                });
            var page = await context.NewPageAsync();
            var timeout = Math.Clamp(_options.RenderTimeoutSeconds, 5, 60) * 1000;
            page.SetDefaultTimeout(timeout);
            await page.SetContentAsync(
                html,
                new PageSetContentOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = timeout
                });
            return await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = true,
                Type = ScreenshotType.Png,
                Animations = ScreenshotAnimations.Disabled,
                Timeout = timeout
            });
        }
        finally
        {
            _renderSlots.Release();
        }
    }

    private async Task<IBrowser> BrowserAsync()
    {
        if (_browser is { IsConnected: true })
        {
            return _browser;
        }

        await _browserLock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true })
            {
                return _browser;
            }

            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(
                new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    ExecutablePath = ResolveExecutablePath(),
                    Args = new[]
                    {
                        "--no-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-gpu",
                        "--disable-extensions"
                    }
                });
            return _browser;
        }
        finally
        {
            _browserLock.Release();
        }
    }

    private string? ResolveExecutablePath()
    {
        var configured = _options.BrowserExecutablePath?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var candidates = new[]
        {
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser != null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        _browserLock.Dispose();
        _renderSlots.Dispose();
    }
}
