using System.IO;
using Microsoft.Web.WebView2.Core;
using WinTilix.Diagnostics;

namespace WinTilix.Controls;

/// <summary>
/// One shared WebView2 environment for every terminal pane. Opening a group creates many panes at once;
/// letting each one spin up its own default environment races on the shared user-data folder and fails
/// with RPC_E_DISCONNECTED (0x80010108). Creation is serialized and the environment is rebuilt if the
/// browser process behind it has gone away.
/// </summary>
internal static class WebViewEnvironment
{
    private const int RpcDisconnected = unchecked((int)0x80010108);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly SemaphoreSlim InitGate = new(1, 1);
    private static Task<CoreWebView2Environment>? _environment;
    private static int _failureShown;

    public static string UserDataFolder => Path.Combine(DebugLog.DirectoryPath, "WebView2");

    public static async Task<CoreWebView2Environment> GetAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _environment ??= CoreWebView2Environment.CreateAsync(null, UserDataFolder);
            return await _environment;
        }
        catch
        {
            _environment = null;
            throw;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Initialize a pane's WebView2 against the shared environment, one pane at a time. If the browser
    /// process was lost (RPC_E_DISCONNECTED) the environment is discarded and the attempt is repeated.
    /// </summary>
    public static async Task InitializeAsync(Microsoft.Web.WebView2.Wpf.WebView2 view)
    {
        const int attempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            await InitGate.WaitAsync();
            try
            {
                var env = await GetAsync();
                await view.EnsureCoreWebView2Async(env);
                return;
            }
            catch (Exception ex) when (IsDisconnected(ex) && attempt < attempts)
            {
                DebugLog.Error($"WebView2 disconnected (attempt {attempt}/{attempts}); retrying", ex);
                _environment = null;
            }
            finally
            {
                InitGate.Release();
            }

            await Task.Delay(250 * attempt);
        }
    }

    /// <summary>Show one error dialog even when a whole group of panes fails together.</summary>
    public static void ShowFailureOnce(Exception ex)
    {
        if (Interlocked.Exchange(ref _failureShown, 1) == 1)
        {
            return;
        }

        var text = ex is WebView2RuntimeNotFoundException
            ? "Microsoft Edge WebView2 Runtime is required.\n\n" + ex.Message
            : "The terminal view could not start.\n\n" + ex.Message +
              "\n\nClose WinTilix, end any leftover msedgewebview2.exe processes, and try again. Details are in debug.log.";
        System.Windows.MessageBox.Show(
            text,
            "WinTilix",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }

    private static bool IsDisconnected(Exception ex) =>
        ex is System.Runtime.InteropServices.COMException com && com.HResult == RpcDisconnected;
}
