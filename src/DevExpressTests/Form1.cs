using DevExpress.Data.Linq;
using DevExpress.XtraExport.Helpers;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using System;
using System.IO;
using System.Windows.Forms;

namespace DevExpressTests
{
    public partial class Form1 : Form
    {


        public Form1()
        {
            InitializeComponent();
            SetupInstantFeedback();
        }

        private void SetupInstantFeedback()
        {
            string connectionString = "Data Source=C:\\PGS\\LOCA RECEPTION\\DATA\\LYON;ServerType=remote;TableType=CDX";
            
            var instantFeedbackSource = new LinqInstantFeedbackSource
            {
                KeyExpression = "ROWID"
            };
            
            instantFeedbackSource.GetQueryable += (sender, e) =>
            {
                var provider = new IQToolkit.Data.Advantage.AdvantageQueryProvider(connectionString);
                provider.Log = Console.Out; // Log generated SQL to the console
                var clients = provider.GetTable<LocClt>();
                e.QueryableSource = clients;
                e.Tag = provider; // Store provider for disposal
            };
            
            instantFeedbackSource.DismissQueryable += (sender, e) =>
            {
                if (e.Tag is IDisposable disposable)
                    disposable.Dispose();
            };
            
            gridControl2.DataSource = instantFeedbackSource;

			gridView2.OptionsLayout.StoreAllOptions = true;
			gridView2.OptionsLayout.StoreFormatRules = true;

			gridView2.OptionsMenu.ShowConditionalFormattingItem = true;

            if(File.Exists("layout.xml"))
                gridView2.RestoreLayoutFromXml("layout.xml");

		}

		private void barButtonItem1_ItemClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
		{
			gridView2.SaveLayoutToXml("layout.xml");
		}
	}
}
