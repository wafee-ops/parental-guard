using System.Security.Principal;

namespace ParentalGuard.Service;

internal static class WindowsPrivilegeChecker
{
    public static bool IsRunningElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
