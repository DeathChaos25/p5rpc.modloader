using Reloaded.Hooks.Definitions;
using static p5rpc.modloader.Utilities.Native;

namespace p5rpc.modloader.Utilities;

/// <summary>
/// Hooks the <see cref="WndProc"/> function of a given window.
/// </summary>
public class WndProcHook
{
    /// <summary>
    /// Shared instance of the hook.
    /// </summary>
    public static WndProcHook Instance { get; private set; } = null!;

    /// <summary>
    /// The function that gets called when hooked.
    /// </summary>
    public WndProcFn HookFunction { get; private set; }

    /// <summary>
    /// Window handle of hooked window.
    /// </summary>
    public IntPtr WindowHandle { get; private set; }

    /// <summary>
    /// The hook created for the WndProc function.
    /// Can be used to call the original WndProc.
    /// </summary>
    public IHook<WndProcFn> Hook { get; private set; } = null!;

    private WndProcHook() { }
    
    private WndProcHook(IReloadedHooks hooks, IntPtr hWnd, WndProcFn wndProcHandler)
    {
        WindowHandle = hWnd;
        var windowProc = GetWindowLong(hWnd, GWL.GWL_WNDPROC);
        SetupHook(hooks, wndProcHandler, windowProc);
    }

    /// <summary>
    /// Creates a hook for the WindowProc function.
    /// </summary>
    /// <param name="hWnd">Handle of the window to hook.</param>
    /// <param name="wndProcHandler">Handles the WndProc function.</param>
    public static WndProcHook Create(IReloadedHooks hooks, IntPtr hWnd, WndProcFn wndProcHandler) =>
        Instance ??= new WndProcHook(hooks, hWnd, wndProcHandler);

    /// <summary>
    /// Initializes the hook class.
    /// </summary>
    private void SetupHook(IReloadedHooks hooks, WndProcFn proc, IntPtr address)
    {
        HookFunction = proc;
        Hook = hooks.CreateHook<WndProcFn>(HookFunction, (long)address).Activate();
    }
}