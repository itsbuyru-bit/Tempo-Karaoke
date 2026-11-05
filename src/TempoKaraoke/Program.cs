using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TempoKaraoke.Services;

namespace TempoKaraoke;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Array.Exists(args, arg => string.Equals(arg, "--wifi-direct-helper", StringComparison.OrdinalIgnoreCase)))
        {
            return WiFiDirectHelperRuntime.Run(args);
        }

        if (!AppInfo.IsPackaged)
        {
            // Стабильный AUMID нужен уведомлениям и группировке на панели задач.
            TrySetAppUserModelId();

            if (!HasLocalWindowsAppRuntime())
            {
                // Портативная сборка (WindowsPackageType=None): Windows App SDK не
                // инициализируется автоматически (WindowsAppSdkAutoInitialize=false),
                // поэтому поднимаем рантайм вручную. 0x00020006 — Windows App SDK 2.6.
                // Self-contained сборке bootstrap не нужен: он поднял бы установленный
                // рантайм вместо локального.
                global::Microsoft.Windows.ApplicationModel.DynamicDependency.Bootstrap.TryInitialize(0x00020006, out _);
            }
        }

        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        global::Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            global::System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }

    /// <summary>
    /// Self-contained сборка кладёт Windows App SDK рядом с exe: в этом случае
    /// bootstrap не нужен, иначе он поднимет установленный рантайм.
    /// </summary>
    private static bool HasLocalWindowsAppRuntime()
    {
        try
        {
            return File.Exists(Path.Combine(AppContext.BaseDirectory, "Microsoft.WindowsAppRuntime.dll"));
        }
        catch
        {
            return false;
        }
    }

    private const string AppUserModelId = "CappuccinoDev.TempoKaraoke";

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private static void TrySetAppUserModelId()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch
        {
            // Не критично: без AUMID просто не будет уведомлений/группировки.
        }
    }
}
