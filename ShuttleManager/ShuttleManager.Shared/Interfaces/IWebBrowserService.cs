namespace ShuttleManager.Shared.Interfaces;

public interface IWebBrowserService
{
    Task OpenBrowserAsync(Uri uri);
}