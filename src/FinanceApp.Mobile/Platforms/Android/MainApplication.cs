using Android.App;
using Android.Runtime;

namespace FinanceApp.Mobile;

[Application]
public class MainApplication(IntPtr handle, JniHandleOwnership ownership) : MauiApplication(handle, ownership)
{
    public override void OnCreate()
    {
        // Whatever kills the app — on start or later — is kept for the next start to show (CrashReport).
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => CrashReport.Save(e.Exception);
        try
        {
            base.OnCreate();
        }
        catch (Exception ex)
        {
            CrashReport.Save(ex);
            throw;
        }
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
