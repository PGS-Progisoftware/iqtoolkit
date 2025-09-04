using System.Data.Common;
using IQToolkit.Data;
using IQToolkit.Data.Common;
using IQToolkit.Data.Mapping;

namespace IQToolkit.Data.Advantage
{
    public class AdvantageQueryProvider : DbEntityProvider
    {
        public AdvantageQueryProvider(string connectionString)
            : this(CreateConnection(connectionString), null, null) { }

        public AdvantageQueryProvider(DbConnection connection)
            : this(connection, null, null) { }

        public AdvantageQueryProvider(DbConnection connection, QueryMapping mapping, QueryPolicy policy)
            : base(connection, new AdvantageLanguage(), mapping, policy) { }

        private static DbConnection CreateConnection(string connectionString)
        {
            var factory = AdvantageProviderFactory.Instance;
            var conn = factory.CreateConnection();
            conn.ConnectionString = connectionString;
            return conn;
        }

        // Override the Executor to ensure parameters are unnamed for positional parameters
        protected override QueryExecutor CreateExecutor()
        {
            return new AdvantageExecutor(this);
        }

        class AdvantageExecutor : Executor
        {
            public AdvantageExecutor(DbEntityProvider provider) : base(provider) { }

            protected override void AddParameter(DbCommand command, QueryParameter parameter, object value)
            {
                var p = command.CreateParameter();
                p.ParameterName = parameter.Name;
                p.Value = value ?? System.DBNull.Value;
                command.Parameters.Add(p);
            }

			protected override DbCommand GetCommand(QueryCommand query, object[] paramValues)
			{
				return base.GetCommand(query, paramValues);
			}
        }
    }
}
