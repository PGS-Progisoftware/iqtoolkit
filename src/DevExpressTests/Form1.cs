using System;
using System.Windows.Forms;
using DevExpress.Data.Linq;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;

namespace DevExpressTests
{
    public partial class Form1 : Form
    {
        private GridControl gridControl1;
        private GridView gridView1;

        public Form1()
        {
            InitializeComponent();
            InitializeGrid();
            SetupServerMode();
        }

        private void InitializeGrid()
        {
            gridControl1 = new GridControl();
            gridView1 = new GridView(gridControl1);
            gridControl1.Dock = DockStyle.Fill;
            gridControl1.MainView = gridView1;
            this.Controls.Add(gridControl1);

            gridView1.OptionsMenu.ShowConditionalFormattingItem = true;
		}

        private void SetupServerMode()
        {
            string connectionString = "Data Source=C:\\PGS\\GONESS\\adt;ServerType=remote;User ID=adssys;";
            var provider = new IQToolkit.Data.Advantage.AdvantageQueryProvider(connectionString);
            provider.Log = Console.Out; // Log generated SQL to the console
            var clients = provider.GetTable<LocClt>();

            var serverModeSource = new LinqServerModeSource
            {
                KeyExpression = "CodeClt",
                QueryableSource = clients
			};
            gridControl1.DataSource = serverModeSource;
        }
    }
}
