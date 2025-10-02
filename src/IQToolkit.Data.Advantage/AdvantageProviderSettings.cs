namespace IQToolkit.Data.Advantage
{
	public enum AdvantageTableType
	{
		Cdx,
		Vfp,
		Adt
	}

	public sealed class AdvantageProviderSettings
	{
		public AdvantageProviderSettings()
		{
			this.TableType = AdvantageTableType.Cdx;
		}

		public AdvantageTableType TableType { get; set; }
	}
}


