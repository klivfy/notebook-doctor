using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace NotebookDoctor;

/// <summary>
/// M01: atalho global (Ctrl+Alt+N). Usa uma janela só de mensagens, então funciona mesmo com o app só na bandeja.
/// Se outro programa já usa a combinação, <see cref="Register"/> devolve false e nada é tomado à força.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    public const string Label = "Ctrl+Alt+N";
    private const int Id = 0x4E44; // "ND"
    private const uint ModAlt = 0x1, ModControl = 0x2, ModNoRepeat = 0x4000, VkN = 0x4E;
    private const int WmHotkey = 0x0312;

    private readonly HwndSource _source;
    private bool _registered;
    public event Action? Pressed;

    public GlobalHotkey()
    {
        // HWND_MESSAGE (-3): janela invisível que só recebe mensagens
        _source = new HwndSource(new HwndSourceParameters("NotebookDoctor.Hotkey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(Hook);
    }

    public bool IsRegistered => _registered;

    public bool Register()
    {
        if (_registered) return true;
        _registered = RegisterHotKey(_source.Handle, Id, ModControl | ModAlt | ModNoRepeat, VkN);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) UnregisterHotKey(_source.Handle, Id);
        _registered = false;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == Id) { Pressed?.Invoke(); handled = true; }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
