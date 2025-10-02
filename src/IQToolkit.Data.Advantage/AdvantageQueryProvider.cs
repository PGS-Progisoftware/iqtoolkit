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
        public AdvantageProviderSettings Settings { get; }
        public bool EnableQueryTiming { get; set; } = true;

        // Convenience specialized providers
        public static AdvantageQueryProvider CreateForCdx(string connectionString)
        {
            return new AdvantageQueryProvider(connectionString, new AdvantageProviderSettings { TableType = AdvantageTableType.Cdx });
        }

        public static AdvantageQueryProvider CreateForAdt(string connectionString)
        {
            return new AdvantageQueryProvider(connectionString, new AdvantageProviderSettings { TableType = AdvantageTableType.Adt });
        }

		public AdvantageQueryProvider(string connectionString, AdvantageProviderSettings settings = null, QueryPolicy policy = null)
            : this(CreateConnection(connectionString), null, policy, settings) { }

        public AdvantageQueryProvider(DbConnection connection, AdvantageProviderSettings settings = null)
            : this(connection, null, null, settings) { }

        public AdvantageQueryProvider(DbConnection connection, QueryMapping mapping, QueryPolicy policy, AdvantageProviderSettings settings = null)
            : base(connection, new AdvantageLanguage(), mapping, policy)
        {
            this.Settings = settings ?? new AdvantageProviderSettings();
            if (this.Language is AdvantageLanguage advLang)
            {
                advLang.Settings = this.Settings;
            }
        }

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
				var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
				
				try
				{
					// Log the query with timestamp
					_provider.Log.WriteLine($"[{timestamp}] Executing Query:");
					_provider.Log.WriteLine(query.CommandText);
					
					if (query.Parameters.Count > 0)
					{
						_provider.Log.WriteLine("Parameters:");
						for (int i = 0; i < query.Parameters.Count; i++)
						{
							var param = query.Parameters[i];
							var value = i < paramValues.Length ? paramValues[i] : "NULL";
							_provider.Log.WriteLine($"  {param.Name} = {value}");
						}
					}
					
					var result = executeFunc(query, paramValues);
					stopwatch.Stop();
					
					// Log execution time
					_provider.Log.WriteLine($"Query completed in {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");
					_provider.Log.WriteLine(new string('-', 50));
					
					return result;
				}
				catch (Exception ex)
				{
					stopwatch.Stop();
					_provider.Log.WriteLine($"Query FAILED after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}");
					_provider.Log.WriteLine(new string('-', 50));
					throw;
				}
			}
        }
    }
}
