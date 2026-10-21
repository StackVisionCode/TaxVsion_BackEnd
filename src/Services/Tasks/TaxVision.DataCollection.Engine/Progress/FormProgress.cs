namespace TaxVision.DataCollection.Engine.Progress;

public sealed record FormProgress(int Required, int Answered)
{
    public decimal Percentage => Required == 0 ? 100m : Math.Round(Answered * 100m / Required, 2);
}
