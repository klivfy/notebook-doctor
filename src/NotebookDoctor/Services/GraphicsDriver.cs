using System.Runtime.InteropServices;

namespace NotebookDoctor.Services;

/// <summary>Leva 10.2: socorro para a tela congelada.</summary>
public static class GraphicsDriver
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    /// <summary>
    /// Leva 10.2: Windows+Ctrl+Shift+B, o atalho do próprio Windows que reinicia o driver de vídeo (a tela pisca e volta,
    /// sem fechar programas).
    /// </summary>
    public static void Restart()
    {
        const byte Win = 0x5B, Ctrl = 0x11, Shift = 0x10, B = 0x42;
        const uint KeyUp = 0x0002;
        byte[] keys = [Win, Ctrl, Shift, B];
        foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
        foreach (var k in keys.Reverse()) keybd_event(k, 0, KeyUp, UIntPtr.Zero);
    }
}
