namespace RSTBPatcher.Core.Calculators;

public interface IResourceCalculator
{
    public static abstract uint CalculateSizeOffset(Stream stream, string romfsName);
}