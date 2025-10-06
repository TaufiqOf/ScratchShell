namespace ScratchShell.Models;

public static class DoubleUtil
{
    public static bool AreClose(double a, double b) => Math.Abs(a - b) < 0.5;
}
