using System.Runtime.Versioning;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;

namespace FinanceApp.Mobile;

/// <summary>Android's own fingerprint dialog (BiometricPrompt, Android 10+); «PIN» closes it for the keypad.</summary>
public sealed class AndroidBiometrics : IBiometrics
{
    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(29)
                || Platform.AppContext.GetSystemService(Context.BiometricService) is not BiometricManager manager)
            {
                return false;
            }

            return OperatingSystem.IsAndroidVersionAtLeast(30)
                ? manager.CanAuthenticate((int)BiometricManagerAuthenticators.BiometricWeak) == BiometricCode.Success
#pragma warning disable CA1422 // The no-argument check is the one Android 10 has.
                : manager.CanAuthenticate() == BiometricCode.Success;
#pragma warning restore CA1422
        }
    }

    public Task<bool> AuthenticateAsync(string title)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) || Platform.CurrentActivity is not { } activity)
        {
            return Task.FromResult(false);
        }

        var result = new TaskCompletionSource<bool>();
        var executor = activity.MainExecutor!;
        var prompt = new BiometricPrompt.Builder(activity)
            .SetTitle(title)
            .SetNegativeButton("Ввести PIN", executor, new Answer(result))
            .Build();
        prompt.Authenticate(new CancellationSignal(), executor, new Callback(result));
        return result.Task;
    }

    [SupportedOSPlatform("android28.0")]
    private sealed class Callback(TaskCompletionSource<bool> result) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? authResult) => result.TrySetResult(true);

        // Cancelled, too many tries, no sensor: the PIN pad stays.
        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) => result.TrySetResult(false);
    }

    private sealed class Answer(TaskCompletionSource<bool> result) : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which) => result.TrySetResult(false);
    }
}
