using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using IQToolkit.Data;
using IQToolkit.Data.Common;
using IQToolkit.Data.Mapping;

namespace IQToolkit.Data.Advantage
{
    public class AdvantageQueryProvider : DbEntityProvider
    {
        public bool EnableQueryTiming { get; set; } = true;

        #region Factory Methods

        /// <summary>
        /// Creates a new AdvantageQueryProvider with the specified connection string.
        /// </summary>
        public static AdvantageQueryProvider Create(string connectionString)
        {
            return new AdvantageQueryProvider(connectionString);
        }

        #endregion

        #region Constructors

        public AdvantageQueryProvider(string connectionString, QueryPolicy policy = null)
            : this(CreateConnection(connectionString), new AdvantageMapping(), policy)
        {
        }

        public AdvantageQueryProvider(DbConnection connection)
            : this(connection, new AdvantageMapping(), null)
        {
        }

        public AdvantageQueryProvider(DbConnection connection, QueryMapping mapping, QueryPolicy policy)
            : base(connection, new AdvantageLanguage(), mapping, policy)
        {
        }

        #endregion

        // Override the Executor to ensure parameters are unnamed for positional parameters
        protected override QueryExecutor CreateExecutor()
        {
            return new AdvantageExecutor(this);
        }

        private static DbConnection CreateConnection(string connectionString)
        {
            var factory = AdvantageProviderFactory.Instance;
            var conn = factory.CreateConnection();
            conn.ConnectionString = connectionString;
            return conn;
        }

        class AdvantageExecutor : Executor
        {
            private readonly AdvantageQueryProvider _provider;

            public AdvantageExecutor(AdvantageQueryProvider provider) : base(provider) 
            { 
                _provider = provider;
            }

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

			public override int ExecuteCommand(QueryCommand query, object[] paramValues)
			{
				if (_provider.EnableQueryTiming && _provider.Log != null)
				{
					return ExecuteWithTiming(query, paramValues, base.ExecuteCommand);
				}
				return base.ExecuteCommand(query, paramValues);
			}

			public override IEnumerable<T> Execute<T>(QueryCommand query, Func<FieldReader, T> fnProjector, MappingEntity entity, object[] paramValues)
			{
				if (_provider.EnableQueryTiming && _provider.Log != null)
				{
					return ExecuteWithTiming(query, paramValues, (q, p) => base.Execute<T>(q, fnProjector, entity, p));
				}
				return base.Execute<T>(query, fnProjector, entity, paramValues);
			}

			private TResult ExecuteWithTiming<TResult>(QueryCommand query, object[] paramValues, Func<QueryCommand, object[], TResult> executeFunc)
			{
				var stopwatch = Stopwatch.StartNew();
				
				try
				{
					stopwatch = Stopwatch.StartNew();
					var result = executeFunc(query, paramValues);
					stopwatch.Stop();
					
					// Log execution time
					_provider.Log.WriteLine($"Query completed in {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");					
					return result;
				}
				catch (Exception ex)
				{
					stopwatch.Stop();
					_provider.Log.WriteLine($"Query FAILED after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}");
					throw;
				}
			}
        }
    }
}
